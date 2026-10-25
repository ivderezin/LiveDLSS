#pragma once
#include <stdint.h>
#include <stddef.h>

#define LDLS_NR_API __declspec(dllexport)

// C ABI mirrored by NrNative.cs. Resource states are UAV on entry and exit.
struct LdlsNrResources {
  void* color;
  void* depth;
  void* motion;
  void* output;
  uint32_t width;
  uint32_t height;
  uint32_t guideWidth;
  uint32_t guideHeight;
  float mvScaleX;
  float mvScaleY;
  int32_t reset;
  int32_t depthInverted;
};

struct LdlsNrOptions {
  float intensity;
  int32_t style;
  int32_t preset;
  float localStructure;
  float localTone;
  float skinStructure;
  int32_t autoMask;
};

extern "C" {
LDLS_NR_API int32_t ldls_nr_init(void* device, const wchar_t* runtimeDirectory,
                               const wchar_t* cacheDirectory, uint32_t applicationId);
// Record creation separately; the caller must execute and fence it before evaluate.
LDLS_NR_API int32_t ldls_nr_create(void* commandList, uint32_t width, uint32_t height,
                                 const LdlsNrOptions* options);
LDLS_NR_API int32_t ldls_nr_evaluate(void* commandList, const LdlsNrResources* resources);
// Only after the GPU fence. Never call while any evaluate is in flight.
LDLS_NR_API void ldls_nr_free();
LDLS_NR_API void ldls_nr_shutdown();
LDLS_NR_API const char* ldls_nr_last_error();
}
