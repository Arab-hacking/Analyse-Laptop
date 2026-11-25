#pragma once

#include <stdint.h>

#if defined(_WIN32)
#define LI_EXPORT __declspec(dllexport)
#else
#define LI_EXPORT __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
#define LI_NOEXCEPT noexcept
extern "C" {
#else
#define LI_NOEXCEPT
#endif

#define LI_NATIVE_ABI_VERSION 3u

LI_EXPORT uint32_t li_get_abi_version(void) LI_NOEXCEPT;
LI_EXPORT const char* li_get_last_error(void) LI_NOEXCEPT;
LI_EXPORT void li_free(void* pointer) LI_NOEXCEPT;
LI_EXPORT int32_t li_collect_runtime_json(char** output) LI_NOEXCEPT;
LI_EXPORT int32_t li_collect_device_tree_json(char** output) LI_NOEXCEPT;
LI_EXPORT int32_t li_collect_storage_json(char** output) LI_NOEXCEPT;
LI_EXPORT int32_t li_collect_battery_json(char** output) LI_NOEXCEPT;
LI_EXPORT int32_t li_collect_usb_json(char** output) LI_NOEXCEPT;
LI_EXPORT int32_t li_collect_display_json(char** output) LI_NOEXCEPT;
LI_EXPORT int32_t li_collect_wlan_json(char** output) LI_NOEXCEPT;
LI_EXPORT int32_t li_collect_firmware_json(char** output) LI_NOEXCEPT;
LI_EXPORT int32_t li_collect_tpm_json(char** output) LI_NOEXCEPT;
LI_EXPORT int32_t li_collect_sensor_json(char** output) LI_NOEXCEPT;
LI_EXPORT int32_t li_collect_gpu_vendor_json(char** output) LI_NOEXCEPT;

#ifdef __cplusplus
}
#endif
