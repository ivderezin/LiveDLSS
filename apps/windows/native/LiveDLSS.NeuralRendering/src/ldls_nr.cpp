// Experimental NGX feature 18 ABI; the public SDK only describes the base parameter interface.
#include "ldls_nr.h"
#include "ngx_parameters.h"
#include <windows.h>
#include <d3d12.h>
#include <nvsdk_ngx_params.h>
#include <cmath>
#include <filesystem>
#include <string>
#include <optional>

static_assert(sizeof(LdlsNrResources) == 64);
static_assert(offsetof(LdlsNrResources, width) == 32);
static_assert(sizeof(LdlsNrOptions) == 28);

namespace {
using Init = int(__cdecl*)(uint64_t, const wchar_t*, ID3D12Device*, int, const void*);
using GetParams = int(__cdecl*)(NVSDK_NGX_Parameter**);
using DestroyParams = int(__cdecl*)(NVSDK_NGX_Parameter*);
using Create = int(__cdecl*)(ID3D12GraphicsCommandList*, int, const void*, void**);
using Evaluate = int(__cdecl*)(ID3D12GraphicsCommandList*, const void*, const void*, void*);
using Release = int(__cdecl*)(void*);
using Shutdown = int(__cdecl*)(ID3D12Device*);

HMODULE g_core = nullptr, g_model = nullptr;
ID3D12Device* g_device = nullptr;
NVSDK_NGX_Parameter* g_params = nullptr;
DestroyParams g_destroy = nullptr;
Create g_create = nullptr;
Evaluate g_evaluate = nullptr;
Release g_release = nullptr;
Shutdown g_shutdown = nullptr;
void* g_feature = nullptr;
std::optional<ldls::NgxParameters> g_access;
uint32_t g_width = 0, g_height = 0;
bool g_modelInitialized = false;
std::string g_error;

int Fail(const char* stage, int code = 0) {
  g_error = stage;
  if (code != 0) g_error += " (code " + std::to_string(code) + ")";
  return -1;
}

template<class T> T Symbol(HMODULE module, const char* name) {
  return reinterpret_cast<T>(GetProcAddress(module, name));
}

void UInt(const char* key, uint32_t value) { g_access->UInt(key, value); }
void Float(const char* key, float value) { g_access->Float(key, value); }
void Resource(const char* key, void* resource) { g_access->Resource(key, resource); }

HMODULE LoadCore() {
  // Use the installed NVIDIA driver, not a game/application proxy nvngx.dll.
  wchar_t directory[32768]{};
  DWORD bytes = sizeof(directory);
  if (RegGetValueW(HKEY_LOCAL_MACHINE,
      L"SYSTEM\\CurrentControlSet\\Services\\nvlddmkm\\NGXCore", L"NGXPath",
      RRF_RT_REG_SZ | RRF_RT_REG_EXPAND_SZ, nullptr, directory, &bytes) == ERROR_SUCCESS) {
    auto path = std::filesystem::path(directory) / L"_nvngx.dll";
    if (auto module = LoadLibraryExW(path.c_str(), nullptr,
        LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32)) return module;
  }
  return LoadLibraryExW(L"_nvngx.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
}

void Rect(const char* widthKey, const char* heightKey, uint32_t width, uint32_t height) {
  UInt(widthKey, width);
  UInt(heightKey, height);
}
} // namespace

extern "C" {
int32_t ldls_nr_init(void* device, const wchar_t* runtimeDirectory,
                     const wchar_t* cacheDirectory, uint32_t applicationId) {
  if (g_device) return Fail("NR is already initialized");
  if (!device || !runtimeDirectory || !cacheDirectory) return Fail("Invalid NR init arguments");
  g_error.clear();
  g_device = static_cast<ID3D12Device*>(device);
  g_core = LoadCore();
  if (!g_core) return Fail("NVIDIA NGX driver core _nvngx.dll not found", static_cast<int>(GetLastError()));
  auto init = Symbol<Init>(g_core, "NVSDK_NGX_D3D12_Init_Ext");
  auto get = Symbol<GetParams>(g_core, "NVSDK_NGX_D3D12_GetCapabilityParameters");
  g_destroy = Symbol<DestroyParams>(g_core, "NVSDK_NGX_D3D12_DestroyParameters");
  if (!init || !get || !g_destroy) return Fail("NGX core is missing required exports");
  const wchar_t* paths[] = {runtimeDirectory};
  NVSDK_NGX_FeatureCommonInfo common{};
  common.PathListInfo.Path = paths;
  common.PathListInfo.Length = 1;
  const int initialized = init(applicationId, cacheDirectory, g_device, 0x15, &common);
  if (initialized != 1) return Fail("NGX core initialization failed", initialized);
  const int parameters = get(&g_params);
  if (parameters != 1 || !g_params) return Fail("NGX capability parameters unavailable", parameters);
  g_access.emplace(g_params);
  if (!g_access->DiscoverFloatSetter()) return Fail("NGX paired float ABI not recognized (bridge ABI v2)");

  const auto modelPath = std::filesystem::path(runtimeDirectory) / L"nvngx_dlssnr.dll";
  g_model = LoadLibraryExW(modelPath.c_str(), nullptr,
      LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
  if (!g_model) return Fail("nvngx_dlssnr.dll could not load", static_cast<int>(GetLastError()));
  auto modelInit = Symbol<Init>(g_model, "NVSDK_NGX_D3D12_Init_Ext");
  g_create = Symbol<Create>(g_model, "NVSDK_NGX_D3D12_CreateFeature");
  g_evaluate = Symbol<Evaluate>(g_model, "NVSDK_NGX_D3D12_EvaluateFeature");
  g_release = Symbol<Release>(g_model, "NVSDK_NGX_D3D12_ReleaseFeature");
  g_shutdown = Symbol<Shutdown>(g_model, "NVSDK_NGX_D3D12_Shutdown1");
  if (!modelInit || !g_create || !g_evaluate || !g_release) return Fail("NR library is missing required exports");
  // The NR snippet consumes the driver's capability block here, unlike the core init.
  volatile int result = modelInit(applicationId, cacheDirectory, g_device, 0x15, g_params);
  if (result != 1) return Fail("NR library initialization failed", result);
  g_modelInitialized = true;
  return 0;
}

int32_t ldls_nr_create(void* commandList, uint32_t width, uint32_t height, const LdlsNrOptions* opt) {
  if (!g_modelInitialized || !g_params || !commandList || !opt || width == 0 || height == 0)
    return Fail("Invalid NR create state");
  if (!std::isfinite(opt->intensity) || opt->intensity < 0 || opt->intensity > 2 ||
      opt->style < 0 || opt->style > 2 || opt->preset < 0 || opt->preset > 2)
    return Fail("Invalid NR model options");
  ldls_nr_free();
  UInt("DLSSNR.Enabled", 1);
  Rect("DLSSNR.Width", "DLSSNR.Height", width, height);
  UInt("CreationNodeMask", 1);
  UInt("VisibilityNodeMask", 1);
  UInt("DLSSNR.Hint.Render.Preset", static_cast<uint32_t>(opt->preset));
  Float("DLSSNR.Intensity", opt->intensity);
  UInt("DLSSNR.Style", static_cast<uint32_t>(opt->style));
  Float("DLSSNR.LocalStructureStrength", opt->localStructure);
  Float("DLSSNR.LocalToneStrength", opt->localTone);
  Float("DLSSNR.SkinStructureStrength", opt->skinStructure);
  UInt("DLSSNR.UseAutoMask", static_cast<uint32_t>(opt->autoMask));
  UInt("DLSSNR.UICorrection", 0);
  // Retaining the return value prevents a tail jump out of the named caller module.
  volatile int result = g_create(static_cast<ID3D12GraphicsCommandList*>(commandList), 18, g_params, &g_feature);
  if (result != 1 || !g_feature) return Fail("NR feature 18 creation failed", result);
  g_width = width;
  g_height = height;
  return 0;
}

int32_t ldls_nr_evaluate(void* commandList, const LdlsNrResources* res) {
  if (!g_feature || !commandList || !res || !res->color || !res->depth || !res->motion || !res->output ||
      res->color == res->output || res->width != g_width || res->height != g_height ||
      res->guideWidth == 0 || res->guideHeight == 0) return Fail("Invalid NR evaluate resources");
  Resource("DLSSNR.Color", res->color);
  Resource("DLSSNR.Depth", res->depth);
  Resource("DLSSNR.MVec", res->motion);
  Resource("DLSSNR.Output", res->output);
  UInt("DLSSNR.Enabled", 1);
  Rect("DLSSNR.Width", "DLSSNR.Height", res->width, res->height);
  UInt("DLSSNR.DepthInverted", static_cast<uint32_t>(res->depthInverted));
  UInt("DLSSNR.Reset", static_cast<uint32_t>(res->reset));
  const char* bases[] = {"DLSSNR.ColorSubrectBaseX", "DLSSNR.ColorSubrectBaseY",
    "DLSSNR.OutputSubrectBaseX", "DLSSNR.OutputSubrectBaseY", "DLSSNR.DepthSubrectBaseX",
    "DLSSNR.DepthSubrectBaseY", "DLSSNR.MVecSubrectBaseX", "DLSSNR.MVecSubrectBaseY"};
  for (auto key : bases) UInt(key, 0);
  Rect("DLSSNR.ColorSubrectWidth", "DLSSNR.ColorSubrectHeight", res->width, res->height);
  Rect("DLSSNR.OutputSubrectWidth", "DLSSNR.OutputSubrectHeight", res->width, res->height);
  Rect("DLSSNR.DepthSubrectWidth", "DLSSNR.DepthSubrectHeight", res->guideWidth, res->guideHeight);
  Rect("DLSSNR.MVecSubrectWidth", "DLSSNR.MVecSubrectHeight", res->guideWidth, res->guideHeight);
  Float("DLSSNR.MVecScaleX", res->mvScaleX);
  Float("DLSSNR.MVecScaleY", res->mvScaleY);
  volatile int result = g_evaluate(static_cast<ID3D12GraphicsCommandList*>(commandList), g_feature, g_params, nullptr);
  return result == 1 ? 0 : Fail("NR feature 18 evaluation failed", result);
}

void ldls_nr_free() {
  if (g_feature && g_release) g_release(g_feature);
  g_feature = nullptr;
  g_width = g_height = 0;
}

void ldls_nr_shutdown() {
  ldls_nr_free();
  if (g_modelInitialized && g_shutdown) g_shutdown(g_device);
  if (g_params && g_destroy) g_destroy(g_params);
  g_params = nullptr;
  // Do not shut down the driver's NGX core: Streamline SR shares it on the same device.
  if (g_model) FreeLibrary(g_model);
  if (g_core) FreeLibrary(g_core);
  g_core = g_model = nullptr;
  g_device = nullptr;
  g_create = nullptr;
  g_evaluate = nullptr;
  g_release = nullptr;
  g_destroy = nullptr;
  g_shutdown = nullptr;
  g_modelInitialized = false;
  g_access.reset();
}

const char* ldls_nr_last_error() { return g_error.c_str(); }
} // extern "C"
