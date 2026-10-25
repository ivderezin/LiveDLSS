#ifndef LDLS_DLSS_H
#define LDLS_DLSS_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#define LDLS_API __declspec(dllexport)

enum {
  LDLS_OK = 0,
  LDLS_ERR_NOT_INITIALIZED = 1,
  LDLS_ERR_SL_INIT = 2,
  LDLS_ERR_SET_DEVICE = 3,
  LDLS_ERR_NOT_SUPPORTED = 4,
  LDLS_ERR_OPTIONS = 5,
  LDLS_ERR_EVALUATE = 6,
};

typedef struct LdlsResources {
  void* color;
  void* depth;
  void* motion;
  void* output;
  uint32_t colorState, depthState, motionState, outputState;
  uint32_t renderWidth, renderHeight;
  uint32_t outputWidth, outputHeight;
} LdlsResources;

typedef struct LdlsOptions {
  int32_t mode;
  int32_t preset;
  float sharpness;
  int32_t reset;
} LdlsOptions;

typedef struct LdlsConstants {
  float cameraViewToClip[16];
  float clipToCameraView[16];
  float clipToPrevClip[16];
  float prevClipToClip[16];
  float cameraPos[3];
  float cameraUp[3];
  float cameraRight[3];
  float cameraFwd[3];
  float cameraNear;
  float cameraFar;
  float cameraFov;
  float cameraAspect;
  float mvecScaleX;
  float mvecScaleY;
} LdlsConstants;

LDLS_API int32_t ldls_dlss_init(uint32_t appId);

LDLS_API int32_t ldls_dlss_set_device(void* d3d12Device, uint64_t adapterLuid);

LDLS_API int32_t ldls_dlss_evaluate(void* commandList, const LdlsResources* resources,
                                    const LdlsOptions* options, const LdlsConstants* constants,
                                    uint32_t frameIndex);

LDLS_API void ldls_dlss_free_resources(void);
LDLS_API void ldls_dlss_shutdown(void);

#ifdef __cplusplus
}
#endif

#endif
