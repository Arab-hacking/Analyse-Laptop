#include "li_native.h"
#include "native_common.h"

#include <exception>
#include <string>
#include <vector>

#ifdef _WIN32
#define NOMINMAX
#include <windows.h>
#include <cfgmgr32.h>
#include <setupapi.h>
#endif

namespace {
#ifdef _WIN32
void append_tpm_context_probe(li_native::JsonWriter& writer)
{
    writer.raw("\"tbsContext\":");
    // Только System32: иначе DLL можно подменить из рабочего каталога.
    HMODULE module = LoadLibraryExW(L"tbs.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (module == nullptr) {
        writer.begin_object();
        writer.key("available");
        writer.boolean(false);
        writer.raw(",\"error\":");
        li_native::append_win32_error(writer, GetLastError());
        writer.end_object();
        return;
    }

    struct ModuleGuard final {
        HMODULE value;
        ~ModuleGuard() { FreeLibrary(value); }
    } module_guard{ module };

    struct TbsContextParams final {
        UINT32 version;
    };

    using TbsiContextCreate = HRESULT(WINAPI*)(const TbsContextParams*, void**);
    using TbsipContextClose = HRESULT(WINAPI*)(void*);
    auto create_context = reinterpret_cast<TbsiContextCreate>(GetProcAddress(module, "Tbsi_Context_Create"));
    auto close_context = reinterpret_cast<TbsipContextClose>(GetProcAddress(module, "Tbsip_Context_Close"));
    if (create_context == nullptr || close_context == nullptr) {
        writer.raw("{\"available\":false,\"error\":\"TBS entry points not found\"}");
        return;
    }

    TbsContextParams params{};
    params.version = 1u;
    void* context = nullptr;
    HRESULT hr = create_context(&params, &context);
    writer.begin_object();
    writer.key("available");
    writer.boolean(SUCCEEDED(hr));
    writer.raw(",\"hresult\":");
    writer.signed_number(hr);
    writer.raw(",\"message\":");
    writer.string(li_native::hresult_message(hr));
    if (SUCCEEDED(hr) && context != nullptr) {
        close_context(context);
    }

    writer.end_object();
}

std::string get_device_id(DEVINST instance)
{
    wchar_t buffer[MAX_DEVICE_ID_LEN]{};
    if (CM_Get_Device_IDW(instance, buffer, MAX_DEVICE_ID_LEN, 0u) != CR_SUCCESS) {
        return {};
    }

    return li_native::wide_to_utf8(buffer);
}

void append_registry_string(li_native::JsonWriter& writer, HDEVINFO info, SP_DEVINFO_DATA& data, DWORD property, const char* key)
{
    DWORD type = 0u;
    DWORD size = 0u;
    SetupDiGetDeviceRegistryPropertyW(info, &data, property, &type, nullptr, 0u, &size);
    writer.raw(",\"");
    writer.raw(key);
    writer.raw("\":");
    if (size == 0u) {
        writer.null_value();
        return;
    }

    std::vector<BYTE> buffer(size + sizeof(wchar_t));
    if (!SetupDiGetDeviceRegistryPropertyW(info, &data, property, &type, buffer.data(), static_cast<DWORD>(buffer.size()), nullptr)) {
        writer.null_value();
        return;
    }

    if (type == REG_SZ || type == REG_EXPAND_SZ) {
        auto* wide = reinterpret_cast<const wchar_t*>(buffer.data());
        writer.string(li_native::wide_to_utf8(std::wstring_view(wide, wcsnlen_s(wide, buffer.size() / sizeof(wchar_t)))));
    }
    else {
        writer.hex_bytes(buffer.data(), size);
    }
}

void append_sensor_setupapi_probe(li_native::JsonWriter& writer)
{
    writer.raw("\"sensorDevices\":[");
    DWORD required = 0u;
    SetupDiClassGuidsFromNameW(L"Sensor", nullptr, 0u, &required);
    if (required == 0u) {
        writer.raw("],\"sensorClassAvailable\":false");
        return;
    }

    std::vector<GUID> class_guids(required);
    if (!SetupDiClassGuidsFromNameW(L"Sensor", class_guids.data(), required, &required)) {
        writer.raw("],\"sensorClassAvailable\":false");
        return;
    }

    bool first = true;
    unsigned long count = 0u;
    for (const GUID& guid : class_guids) {
        HDEVINFO info = SetupDiGetClassDevsW(&guid, nullptr, nullptr, DIGCF_PRESENT);
        if (info == INVALID_HANDLE_VALUE) {
            continue;
        }

        struct DeviceInfoSetGuard final {
            HDEVINFO value;
            ~DeviceInfoSetGuard() { SetupDiDestroyDeviceInfoList(value); }
        } guard{ info };

        for (DWORD index = 0u;; ++index) {
            SP_DEVINFO_DATA data{};
            data.cbSize = sizeof(data);
            if (!SetupDiEnumDeviceInfo(info, index, &data)) {
                if (GetLastError() == ERROR_NO_MORE_ITEMS) {
                    break;
                }

                continue;
            }

            writer.comma(first);
            writer.begin_object();
            writer.key("instanceId");
            writer.string(get_device_id(data.DevInst));
            append_registry_string(writer, info, data, SPDRP_DEVICEDESC, "description");
            append_registry_string(writer, info, data, SPDRP_FRIENDLYNAME, "friendlyName");
            append_registry_string(writer, info, data, SPDRP_MFG, "manufacturer");
            writer.end_object();
            ++count;
        }
    }

    writer.raw("],\"sensorClassAvailable\":true,\"sensorDeviceCount\":");
    writer.unsigned_number(count);
}
#endif
}  // namespace

extern "C" LI_EXPORT int32_t li_collect_tpm_json(char** output) noexcept
{
    if (output == nullptr) {
        li_native::set_last_error("Аргумент output равен null.");
        return -1;
    }

    *output = nullptr;
#ifndef _WIN32
    return li_native::return_json_string(output, li_native::unsupported_json("tpm", "TBS API доступен только на Windows."));
#else
    try {
        li_native::JsonWriter writer;
        writer.raw("{\"collector\":\"tpm\",\"status\":\"ok\",\"source\":\"TBS dynamic API\",");
        append_tpm_context_probe(writer);
        writer.raw("}");
        return li_native::return_json_string(output, writer.str());
    }
    catch (const std::exception& exception) {
        return li_native::return_error_json(output, "tpm", exception.what());
    }
    catch (...) {
        return li_native::return_error_json(output, "tpm", "Необработанное исключение при сборе TPM.");
    }
#endif
}

extern "C" LI_EXPORT int32_t li_collect_sensor_json(char** output) noexcept
{
    if (output == nullptr) {
        li_native::set_last_error("Аргумент output равен null.");
        return -1;
    }

    *output = nullptr;
#ifndef _WIN32
    return li_native::return_json_string(output, li_native::unsupported_json("sensors", "Перечисление сенсоров доступно только на Windows."));
#else
    try {
        li_native::JsonWriter writer;
        writer.raw("{\"collector\":\"sensors\",\"status\":\"ok\",\"source\":\"SetupAPI Sensor class\",");
        append_sensor_setupapi_probe(writer);
        writer.raw("}");
        return li_native::return_json_string(output, writer.str());
    }
    catch (const std::exception& exception) {
        return li_native::return_error_json(output, "sensors", exception.what());
    }
    catch (...) {
        return li_native::return_error_json(output, "sensors", "Необработанное исключение при сборе сенсоров.");
    }
#endif
}
