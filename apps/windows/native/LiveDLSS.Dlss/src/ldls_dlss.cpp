#include "ldls_dlss.h"

#include <d3d12.h>
#include <sl.h>
#include <sl_consts.h>
#include <sl_dlss.h>

#include <cstring>
#include <iterator>

static_assert(sizeof(LdlsResources) == 4 * 8 + 8 * 4, "LdlsResources layout changed");
static_assert(sizeof(LdlsOptions) == 16, "LdlsOptions layout changed");
static_assert(sizeof(LdlsConstants) == (4 * 16 + 4 * 3 + 6) * 4, "LdlsConstants layout changed");

namespace {

bool g_initialized = false;
bool g_deviceSet = false;
uint32_t g_lastOutputW = 0;
uint32_t g_lastOutputH = 0;
const sl::ViewportHandle kViewport{0};

sl::float4x4 ToSl(const float (&m)[16]) {
  sl::float4x4 r;
  for (int i = 0; i < 4; ++i) {
    r.row[i] = sl::float4(m[i * 4 + 0], m[i * 4 + 1], m[i * 4 + 2], m[i * 4 + 3]);
  }
  return r;
}

sl::float3 ToSl(const float (&v)[3]) { return sl::float3(v[0], v[1], v[2]); }

sl::DLSSMode ToSlMode(int32_t mode) {
  switch (mode) {
    case 0: return sl::DLSSMode::eDLAA;
    case 1: return sl::DLSSMode::eMaxQuality;
    case 2: return sl::DLSSMode::eBalanced;
    case 3: return sl::DLSSMode::eMaxPerformance;
    case 4: return sl::DLSSMode::eUltraPerformance;
    default: return sl::DLSSMode::eBalanced;
  }
}

}

extern "C" {

LDLS_API int32_t ldls_dlss_init(uint32_t appId) {
  if (g_initialized) return LDLS_OK;

  static const sl::Feature features[] = {sl::kFeatureDLSS};
  sl::Preferences pref{};
  pref.showConsole = false;
#ifdef _DEBUG
  pref.logLevel = sl::LogLevel::eVerbose;
#else
  pref.logLevel = sl::LogLevel::eDefault;
#endif
  pref.featuresToLoad = features;
  pref.numFeaturesToLoad = 1;
  pref.renderAPI = sl::RenderAPI::eD3D12;
  pref.engine = sl::EngineType::eCustom;
  pref.engineVersion = "LiveDLSS " LIVEDLSS_VERSION;
  pref.applicationId = appId;
  pref.flags |= sl::PreferenceFlags::eUseManualHooking;

  if (slInit(pref, sl::kSDKVersion) != sl::Result::eOk) return LDLS_ERR_SL_INIT;
  g_initialized = true;
  return LDLS_OK;
}

LDLS_API int32_t ldls_dlss_set_device(void* d3d12Device, uint64_t adapterLuid) {
  if (!g_initialized) return LDLS_ERR_NOT_INITIALIZED;
  if (slSetD3DDevice(d3d12Device) != sl::Result::eOk) return LDLS_ERR_SET_DEVICE;

  LUID luid{};
  luid.LowPart = static_cast<DWORD>(adapterLuid & 0xFFFFFFFFu);
  luid.HighPart = static_cast<LONG>(adapterLuid >> 32);
  sl::AdapterInfo adapter{};
  adapter.deviceLUID = reinterpret_cast<uint8_t*>(&luid);
  adapter.deviceLUIDSizeInBytes = sizeof(LUID);
  if (slIsFeatureSupported(sl::kFeatureDLSS, adapter) != sl::Result::eOk) return LDLS_ERR_NOT_SUPPORTED;

  g_deviceSet = true;
  return LDLS_OK;
}

LDLS_API int32_t ldls_dlss_evaluate(void* commandList, const LdlsResources* res, const LdlsOptions* opt,
                                    const LdlsConstants* k, uint32_t frameIndex) {
  if (!g_deviceSet) return LDLS_ERR_NOT_INITIALIZED;

  bool reset = opt->reset != 0;
  if (res->outputWidth != g_lastOutputW || res->outputHeight != g_lastOutputH) {
    if (g_lastOutputW != 0) slFreeResources(sl::kFeatureDLSS, kViewport);
    g_lastOutputW = res->outputWidth;
    g_lastOutputH = res->outputHeight;
    reset = true;
  }

  sl::FrameToken* frame = nullptr;
  if (slGetNewFrameToken(frame, &frameIndex) != sl::Result::eOk) return LDLS_ERR_EVALUATE;

  sl::DLSSOptions options{};
  options.mode = ToSlMode(opt->mode);
  options.outputWidth = res->outputWidth;
  options.outputHeight = res->outputHeight;
  options.sharpness = opt->sharpness;
  options.colorBuffersHDR = sl::Boolean::eFalse;
  const auto preset = static_cast<sl::DLSSPreset>(opt->preset);
  options.dlaaPreset = options.qualityPreset = options.balancedPreset = options.performancePreset =
      options.ultraPerformancePreset = options.ultraQualityPreset = preset;
  if (slDLSSSetOptions(kViewport, options) != sl::Result::eOk) return LDLS_ERR_OPTIONS;

  sl::Constants c{};
  c.cameraViewToClip = ToSl(k->cameraViewToClip);
  c.clipToCameraView = ToSl(k->clipToCameraView);
  c.clipToLensClip = sl::float4x4{};
  for (int i = 0; i < 4; ++i) c.clipToLensClip.row[i] = sl::float4(i == 0, i == 1, i == 2, i == 3);
  c.clipToPrevClip = ToSl(k->clipToPrevClip);
  c.prevClipToClip = ToSl(k->prevClipToClip);
  c.jitterOffset = sl::float2(0, 0);
  c.mvecScale = sl::float2(k->mvecScaleX, k->mvecScaleY);
  c.cameraPinholeOffset = sl::float2(0, 0);
  c.cameraPos = ToSl(k->cameraPos);
  c.cameraUp = ToSl(k->cameraUp);
  c.cameraRight = ToSl(k->cameraRight);
  c.cameraFwd = ToSl(k->cameraFwd);
  c.cameraNear = k->cameraNear;
  c.cameraFar = k->cameraFar;
  c.cameraFOV = k->cameraFov;
  c.cameraAspectRatio = k->cameraAspect;
  c.depthInverted = sl::Boolean::eTrue;
  c.cameraMotionIncluded = sl::Boolean::eTrue;
  c.motionVectors3D = sl::Boolean::eFalse;
  c.reset = reset ? sl::Boolean::eTrue : sl::Boolean::eFalse;
  c.orthographicProjection = sl::Boolean::eFalse;
  c.motionVectorsDilated = sl::Boolean::eFalse;
  c.motionVectorsJittered = sl::Boolean::eFalse;
  if (slSetConstants(c, *frame, kViewport) != sl::Result::eOk) return LDLS_ERR_EVALUATE;

  sl::Resource color(sl::ResourceType::eTex2d, res->color, nullptr, nullptr, res->colorState);
  sl::Resource depth(sl::ResourceType::eTex2d, res->depth, nullptr, nullptr, res->depthState);
  sl::Resource motion(sl::ResourceType::eTex2d, res->motion, nullptr, nullptr, res->motionState);
  sl::Resource output(sl::ResourceType::eTex2d, res->output, nullptr, nullptr, res->outputState);

  const sl::Extent renderExtent{0, 0, res->renderWidth, res->renderHeight};
  const sl::Extent outputExtent{0, 0, res->outputWidth, res->outputHeight};
  constexpr auto kLife = sl::ResourceLifecycle::eValidUntilEvaluate;
  sl::ResourceTag tags[] = {
      sl::ResourceTag(&color, sl::kBufferTypeScalingInputColor, kLife, &renderExtent),
      sl::ResourceTag(&depth, sl::kBufferTypeDepth, kLife, &renderExtent),
      sl::ResourceTag(&motion, sl::kBufferTypeMotionVectors, kLife, &renderExtent),
      sl::ResourceTag(&output, sl::kBufferTypeScalingOutputColor, kLife, &outputExtent),
  };
  auto* cmd = static_cast<sl::CommandBuffer*>(commandList);
  if (slSetTag(kViewport, tags, static_cast<uint32_t>(std::size(tags)), cmd) != sl::Result::eOk) {
    return LDLS_ERR_EVALUATE;
  }

  const sl::BaseStructure* inputs[] = {&kViewport};
  if (slEvaluateFeature(sl::kFeatureDLSS, *frame, inputs, 1, cmd) != sl::Result::eOk) return LDLS_ERR_EVALUATE;
  return LDLS_OK;
}

LDLS_API void ldls_dlss_free_resources(void) {
  if (g_deviceSet) slFreeResources(sl::kFeatureDLSS, kViewport);
  g_lastOutputW = g_lastOutputH = 0;
}

LDLS_API void ldls_dlss_shutdown(void) {
  if (g_initialized) slShutdown();
  g_initialized = g_deviceSet = false;
}

}
