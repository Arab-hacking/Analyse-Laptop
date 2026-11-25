#include "li_native.h"

#include "licore/li_core.h"
#include "native_common.h"

#include <cstdlib>
#include <exception>
#include <string>

extern "C" LI_EXPORT uint32_t li_get_abi_version(void) noexcept
{
    return LI_NATIVE_ABI_VERSION;
}

extern "C" LI_EXPORT const char* li_get_last_error(void) noexcept
{
    return li_native::get_last_error();
}

extern "C" LI_EXPORT void li_free(void* pointer) noexcept
{
    std::free(pointer);
}

extern "C" LI_EXPORT int32_t li_collect_runtime_json(char** output) noexcept
{
    if (output == nullptr) {
        li_native::set_last_error("Аргумент output равен null.");
        return -1;
    }

    *output = nullptr;
    try {
        const unsigned char self_test[] = { 'a', 'b', 'c' };
        char sha256[65] = {};
        const int32_t hash_status = li_core_sha256_hex(self_test, sizeof(self_test), sha256);
        if (hash_status != LI_STATUS_OK) {
            li_native::set_last_error("Не удалось выполнить SHA-256 self-test licore.");
            return -2;
        }

        li_native::JsonWriter writer;
        writer.begin_object();
        writer.key("nativeAbiVersion");
        writer.unsigned_number(LI_NATIVE_ABI_VERSION);
        writer.raw(",\"coreAbiVersion\":");
        writer.unsigned_number(li_core_get_abi_version());
        writer.raw(",\"mode\":\"stage7-readonly\",\"licoreSelfTestSha256OfAbc\":");
        writer.string(sha256);
        writer.raw(",\"capabilities\":[");
        const char* capabilities[] = {
            "setupapi-device-tree",
            "storage-ioctl",
            "battery-ioctl",
            "usb-setupapi",
            "dxgi-display",
            "wlan-api",
            "firmware-tables",
            "tpm-tbs",
            "sensor-api",
            "gpu-vendor-dynamic-probes",
            "smbios",
            "edid",
            "acpi",
            "cpuid",
            "ata-smart",
            "nvme-health",
            "crc32",
            "sha256"
        };
        bool first = true;
        for (const char* capability : capabilities) {
            writer.comma(first);
            writer.string(capability);
        }

        writer.raw("]}");
        return li_native::return_json_string(output, writer.str());
    }
    catch (const std::exception& exception) {
        li_native::set_last_error(exception.what());
        return -3;
    }
    catch (...) {
        li_native::set_last_error("Необработанное исключение в li_collect_runtime_json.");
        return -4;
    }
}
