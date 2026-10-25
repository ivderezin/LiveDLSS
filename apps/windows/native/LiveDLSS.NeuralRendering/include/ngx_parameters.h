#pragma once
#include <cstdint>
#include <cstring>

#if defined(_WIN32)
#define LDLS_NGX_CALL __thiscall
#else
#define LDLS_NGX_CALL
#endif

namespace ldls {

// Use explicit slots: MSVC and MinGW assign different vtable indices to virtual overloads.
class NgxParameters {
 public:
  explicit NgxParameters(void* block) : block_(block), table_(*static_cast<void***>(block)) {}

  void UInt(const char* key, uint32_t value) const {
    reinterpret_cast<SetUInt>(table_[3])(block_, key, value);
  }

  void Resource(const char* key, void* value) const {
    reinterpret_cast<SetU64>(table_[0])(block_, key, reinterpret_cast<uint64_t>(value));
  }

  void Float(const char* key, float value) const {
    reinterpret_cast<SetFloat>(table_[floatSlot_])(block_, key, value);
  }

  bool DiscoverFloatSetter() {
    constexpr int candidates[] = {1, 6, 2, 5, 7, 4, 3, 0};
    constexpr float probes[] = {0.375f, 0.8125f};
    for (int slot : candidates) {
      bool matches = true;
      for (float expected : probes) {
        reinterpret_cast<SetFloat>(table_[slot])(block_, "DLSSNR.LiveDLSS.FloatProbe", expected);
        // A paired getter is at +8 and may write eight bytes. The upper guard
        // rejects double/u64 getters whose low bits happen to match the float.
        uint32_t result[2] = {0, 0xD3A5917Bu};
        const int code = reinterpret_cast<Get>(table_[slot + 8])(
            block_, "DLSSNR.LiveDLSS.FloatProbe", result);
        float actual = 0;
        std::memcpy(&actual, result, sizeof(actual));
        if (code != 1 || actual != expected || result[1] != 0xD3A5917Bu) {
          matches = false;
          break;
        }
      }
      if (matches) {
        floatSlot_ = slot;
        return true;
      }
    }
    floatSlot_ = -1;
    return false;
  }

  int FloatSlot() const { return floatSlot_; }

 private:
  using SetUInt = void(LDLS_NGX_CALL*)(void*, const char*, uint32_t);
  using SetU64 = void(LDLS_NGX_CALL*)(void*, const char*, uint64_t);
  using SetFloat = void(LDLS_NGX_CALL*)(void*, const char*, float);
  using Get = int(LDLS_NGX_CALL*)(void*, const char*, void*);
  void* block_;
  void** table_;
  int floatSlot_ = -1;
};

} // namespace ldls
