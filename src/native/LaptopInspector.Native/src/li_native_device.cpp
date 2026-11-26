#include "li_native.h"
#include "native_common.h"

#include <exception>
#include <iterator>
#include <string>
#include <vector>

#ifdef _WIN32
#define NOMINMAX
// INITGUID нужен, чтобы DEFINE_GUID/DEVPROPKEY из usbiodef.h и devpkey.h попали
// определениями в нашу DLL: иначе MSVC оставляет только объявления и линковка падает
// с LNK2019 (в mingw эти константы объявлены иначе и проблема не видна).
#define INITGUID
#include <initguid.h>
#include <windows.h>
#include <cfgmgr32.h>
#include <devpkey.h>
#include <setupapi.h>
#endif

namespace {
#ifdef _WIN32
std::string multi_sz_to_json_array_text(const wchar_t* value, std::size_t wchar_count)
{
    li_native::JsonWriter writer;
    writer.begin_array();
    bool first = true;
    std::size_t offset = 0u;
    while (offset < wchar_count && value[offset] != L'\0') {
        std::size_t start = offset;
        while (offset < wchar_count && value[offset] != L'\0') {
            ++offset;
        }

        writer.comma(first);
        writer.string(li_native::wide_to_utf8(std::wstring_view(value + start, offset - start)));
        ++offset;
    }

    writer.end_array();
    return writer.str();
}

void append_registry_property(li_native::JsonWriter& writer, HDEVINFO device_info, SP_DEVINFO_DATA& data, DWORD property, const char* json_name)
{
    DWORD registry_type = 0u;
    DWORD required_size = 0u;
    SetupDiGetDeviceRegistryPropertyW(device_info, &data, property, &registry_type, nullptr, 0u, &required_size);
    if (required_size == 0u) {
        writer.raw(",\"");
        writer.raw(json_name);
        writer.raw("\":null");
        return;
    }

    std::vector<BYTE> buffer(required_size + sizeof(wchar_t));
    if (!SetupDiGetDeviceRegistryPropertyW(device_info, &data, property, &registry_type, buffer.data(), static_cast<DWORD>(buffer.size()), nullptr)) {
        writer.raw(",\"");
        writer.raw(json_name);
        writer.raw("\":null");
        return;
    }

    writer.raw(",\"");
    writer.raw(json_name);
    writer.raw("\":");
    if (registry_type == REG_SZ || registry_type == REG_EXPAND_SZ) {
        auto* wide = reinterpret_cast<const wchar_t*>(buffer.data());
        std::size_t count = wcsnlen_s(wide, buffer.size() / sizeof(wchar_t));
        writer.string(li_native::wide_to_utf8(std::wstring_view(wide, count)));
    }
    else if (registry_type == REG_MULTI_SZ) {
        writer.raw(multi_sz_to_json_array_text(reinterpret_cast<const wchar_t*>(buffer.data()), buffer.size() / sizeof(wchar_t)));
    }
    else if (registry_type == REG_DWORD && buffer.size() >= sizeof(DWORD)) {
        writer.unsigned_number(*reinterpret_cast<const DWORD*>(buffer.data()));
    }
    else {
        writer.hex_bytes(buffer.data(), required_size);
    }
}

void append_device_property_string(li_native::JsonWriter& writer, HDEVINFO device_info, SP_DEVINFO_DATA& data, const DEVPROPKEY& key, const char* json_name)
{
    DEVPROPTYPE property_type = 0u;
    DWORD required_size = 0u;
    SetupDiGetDevicePropertyW(device_info, &data, &key, &property_type, nullptr, 0u, &required_size, 0u);
    writer.raw(",\"");
    writer.raw(json_name);
    writer.raw("\":");
    if (required_size == 0u) {
        writer.null_value();
        return;
    }

    std::vector<BYTE> buffer(required_size + sizeof(wchar_t));
    if (!SetupDiGetDevicePropertyW(device_info, &data, &key, &property_type, buffer.data(), static_cast<DWORD>(buffer.size()), nullptr, 0u)) {
        writer.null_value();
        return;
    }

    if (property_type == DEVPROP_TYPE_STRING) {
        auto* wide = reinterpret_cast<const wchar_t*>(buffer.data());
        std::size_t count = wcsnlen_s(wide, buffer.size() / sizeof(wchar_t));
        writer.string(li_native::wide_to_utf8(std::wstring_view(wide, count)));
    }
    else if (property_type == DEVPROP_TYPE_STRING_LIST) {
        writer.raw(multi_sz_to_json_array_text(reinterpret_cast<const wchar_t*>(buffer.data()), buffer.size() / sizeof(wchar_t)));
    }
    else if (property_type == DEVPROP_TYPE_UINT32 && buffer.size() >= sizeof(ULONG)) {
        writer.unsigned_number(*reinterpret_cast<const ULONG*>(buffer.data()));
    }
    else if (property_type == DEVPROP_TYPE_BOOLEAN && !buffer.empty()) {
        writer.boolean(buffer[0] != 0u);
    }
    else {
        writer.hex_bytes(buffer.data(), required_size);
    }
}

void append_device_status(li_native::JsonWriter& writer, DEVINST dev_inst)
{
    ULONG status = 0u;
    ULONG problem = 0u;
    CONFIGRET result = CM_Get_DevNode_Status(&status, &problem, dev_inst, 0u);
    writer.raw(",\"cmStatus\":{");
    writer.raw("\"configRet\":");
    writer.unsigned_number(result);
    writer.raw(",\"status\":");
    writer.unsigned_number(status);
    writer.raw(",\"problemCode\":");
    writer.unsigned_number(problem);
    writer.raw(",\"hasProblem\":");
    writer.boolean(result == CR_SUCCESS && problem != 0u);
    writer.raw("}");
}
#endif
}  // namespace

extern "C" LI_EXPORT int32_t li_collect_device_tree_json(char** output) noexcept
{
    if (output == nullptr) {
        li_native::set_last_error("Аргумент output равен null.");
        return -1;
    }

    *output = nullptr;
#ifndef _WIN32
    return li_native::return_json_string(output, li_native::unsupported_json("deviceTree", "SetupAPI доступен только на Windows."));
#else
    try {
        HDEVINFO device_info = SetupDiGetClassDevsW(nullptr, nullptr, nullptr, DIGCF_ALLCLASSES | DIGCF_PRESENT);
        if (device_info == INVALID_HANDLE_VALUE) {
            return li_native::return_error_json(output, "deviceTree", li_native::last_win32_error_message());
        }

        struct DeviceInfoSetGuard final {
            HDEVINFO value;
            ~DeviceInfoSetGuard() { SetupDiDestroyDeviceInfoList(value); }
        } guard{ device_info };

        li_native::JsonWriter writer;
        writer.raw("{\"collector\":\"deviceTree\",\"status\":\"ok\",\"source\":\"SetupAPI+CfgMgr32\",\"devices\":[");
        bool first = true;
        DWORD index = 0u;
        unsigned long count = 0u;
        for (;;) {
            SP_DEVINFO_DATA data{};
            data.cbSize = sizeof(data);
            if (!SetupDiEnumDeviceInfo(device_info, index++, &data)) {
                if (GetLastError() == ERROR_NO_MORE_ITEMS) {
                    break;
                }

                continue;
            }

            wchar_t instance_id[MAX_DEVICE_ID_LEN]{};
            if (CM_Get_Device_IDW(data.DevInst, instance_id, MAX_DEVICE_ID_LEN, 0u) != CR_SUCCESS) {
                instance_id[0] = L'\0';
            }

            wchar_t class_name[128]{};
            SetupDiClassNameFromGuidW(&data.ClassGuid, class_name, static_cast<DWORD>(std::size(class_name)), nullptr);

            writer.comma(first);
            writer.begin_object();
            writer.key("instanceId");
            writer.string(li_native::wide_to_utf8(instance_id));
            writer.raw(",\"classGuid\":");
            wchar_t guid_string[64]{};
            StringFromGUID2(data.ClassGuid, guid_string, static_cast<int>(std::size(guid_string)));
            writer.string(li_native::wide_to_utf8(guid_string));
            writer.raw(",\"className\":");
            writer.string(li_native::wide_to_utf8(class_name));
            append_registry_property(writer, device_info, data, SPDRP_DEVICEDESC, "description");
            append_registry_property(writer, device_info, data, SPDRP_FRIENDLYNAME, "friendlyName");
            append_registry_property(writer, device_info, data, SPDRP_MFG, "manufacturer");
            append_registry_property(writer, device_info, data, SPDRP_SERVICE, "service");
            append_registry_property(writer, device_info, data, SPDRP_DRIVER, "driverKey");
            append_registry_property(writer, device_info, data, SPDRP_HARDWAREID, "hardwareIds");
            append_registry_property(writer, device_info, data, SPDRP_COMPATIBLEIDS, "compatibleIds");
            append_registry_property(writer, device_info, data, SPDRP_LOCATION_INFORMATION, "locationInformation");
            append_device_property_string(writer, device_info, data, DEVPKEY_Device_LocationPaths, "locationPaths");
            append_device_property_string(writer, device_info, data, DEVPKEY_Device_Parent, "parentInstanceId");
            append_device_property_string(writer, device_info, data, DEVPKEY_Device_Children, "childrenInstanceIds");
            append_device_status(writer, data.DevInst);
            writer.end_object();
            ++count;
        }

        writer.raw("],\"deviceCount\":");
        writer.unsigned_number(count);
        writer.raw("}");
        return li_native::return_json_string(output, writer.str());
    }
    catch (const std::exception& exception) {
        return li_native::return_error_json(output, "deviceTree", exception.what());
    }
    catch (...) {
        return li_native::return_error_json(output, "deviceTree", "Необработанное исключение при перечислении устройств.");
    }
#endif
}
