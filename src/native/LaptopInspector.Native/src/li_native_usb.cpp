#include "li_native.h"
#include "native_common.h"

#include <exception>
#include <iterator>
#include <string>
#include <vector>

#ifdef _WIN32
#define NOMINMAX
#include <windows.h>
#include <cfgmgr32.h>
#include <devpkey.h>
#include <setupapi.h>
#include <usbiodef.h>
#endif

namespace {
#ifdef _WIN32
struct DeviceInfoSetGuard final {
    HDEVINFO value;
    ~DeviceInfoSetGuard() { SetupDiDestroyDeviceInfoList(value); }
};

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
    else if (type == REG_MULTI_SZ) {
        writer.begin_array();
        bool first = true;
        const wchar_t* wide = reinterpret_cast<const wchar_t*>(buffer.data());
        std::size_t offset = 0u;
        std::size_t max = buffer.size() / sizeof(wchar_t);
        while (offset < max && wide[offset] != L'\0') {
            std::size_t start = offset;
            while (offset < max && wide[offset] != L'\0') {
                ++offset;
            }

            writer.comma(first);
            writer.string(li_native::wide_to_utf8(std::wstring_view(wide + start, offset - start)));
            ++offset;
        }

        writer.end_array();
    }
    else {
        writer.hex_bytes(buffer.data(), size);
    }
}

void append_usb_interface_devices(li_native::JsonWriter& writer)
{
    HDEVINFO info = SetupDiGetClassDevsW(&GUID_DEVINTERFACE_USB_DEVICE, nullptr, nullptr, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
    writer.raw(",\"deviceInterfaces\":[");
    if (info == INVALID_HANDLE_VALUE) {
        writer.raw("],\"deviceInterfaceError\":");
        li_native::append_win32_error(writer, GetLastError());
        return;
    }

    DeviceInfoSetGuard guard{ info };
    bool first = true;
    for (DWORD index = 0u;; ++index) {
        SP_DEVICE_INTERFACE_DATA interface_data{};
        interface_data.cbSize = sizeof(interface_data);
        if (!SetupDiEnumDeviceInterfaces(info, nullptr, &GUID_DEVINTERFACE_USB_DEVICE, index, &interface_data)) {
            if (GetLastError() == ERROR_NO_MORE_ITEMS) {
                break;
            }

            continue;
        }

        DWORD required = 0u;
        SetupDiGetDeviceInterfaceDetailW(info, &interface_data, nullptr, 0u, &required, nullptr);
        if (required == 0u) {
            continue;
        }

        std::vector<BYTE> buffer(required);
        auto* detail = reinterpret_cast<SP_DEVICE_INTERFACE_DETAIL_DATA_W*>(buffer.data());
        detail->cbSize = sizeof(SP_DEVICE_INTERFACE_DETAIL_DATA_W);
        SP_DEVINFO_DATA devinfo{};
        devinfo.cbSize = sizeof(devinfo);
        if (!SetupDiGetDeviceInterfaceDetailW(info, &interface_data, detail, required, nullptr, &devinfo)) {
            continue;
        }

        writer.comma(first);
        writer.begin_object();
        writer.key("devicePath");
        writer.string(li_native::wide_to_utf8(detail->DevicePath));
        writer.raw(",\"instanceId\":");
        writer.string(get_device_id(devinfo.DevInst));
        append_registry_string(writer, info, devinfo, SPDRP_DEVICEDESC, "description");
        append_registry_string(writer, info, devinfo, SPDRP_HARDWAREID, "hardwareIds");
        append_registry_string(writer, info, devinfo, SPDRP_MFG, "manufacturer");
        writer.end_object();
    }

    writer.end_array();
}
#endif
}  // namespace

extern "C" LI_EXPORT int32_t li_collect_usb_json(char** output) noexcept
{
    if (output == nullptr) {
        li_native::set_last_error("Аргумент output равен null.");
        return -1;
    }

    *output = nullptr;
#ifndef _WIN32
    return li_native::return_json_string(output, li_native::unsupported_json("usb", "USB SetupAPI доступен только на Windows."));
#else
    try {
        HDEVINFO info = SetupDiGetClassDevsW(nullptr, L"USB", nullptr, DIGCF_PRESENT | DIGCF_ALLCLASSES);
        if (info == INVALID_HANDLE_VALUE) {
            return li_native::return_error_json(output, "usb", li_native::last_win32_error_message());
        }

        DeviceInfoSetGuard guard{ info };
        li_native::JsonWriter writer;
        writer.raw("{\"collector\":\"usb\",\"status\":\"ok\",\"source\":\"SetupAPI USB enumerator\",\"devices\":[");
        bool first = true;
        unsigned long count = 0u;
        for (DWORD index = 0u;; ++index) {
            SP_DEVINFO_DATA data{};
            data.cbSize = sizeof(data);
            if (!SetupDiEnumDeviceInfo(info, index, &data)) {
                if (GetLastError() == ERROR_NO_MORE_ITEMS) {
                    break;
                }

                continue;
            }

            ULONG status = 0u;
            ULONG problem = 0u;
            CM_Get_DevNode_Status(&status, &problem, data.DevInst, 0u);
            writer.comma(first);
            writer.begin_object();
            writer.key("instanceId");
            writer.string(get_device_id(data.DevInst));
            append_registry_string(writer, info, data, SPDRP_DEVICEDESC, "description");
            append_registry_string(writer, info, data, SPDRP_FRIENDLYNAME, "friendlyName");
            append_registry_string(writer, info, data, SPDRP_HARDWAREID, "hardwareIds");
            append_registry_string(writer, info, data, SPDRP_LOCATION_INFORMATION, "locationInformation");
            writer.raw(",\"cmStatus\":");
            writer.unsigned_number(status);
            writer.raw(",\"problemCode\":");
            writer.unsigned_number(problem);
            writer.end_object();
            ++count;
        }

        writer.raw("],\"deviceCount\":");
        writer.unsigned_number(count);
        append_usb_interface_devices(writer);
        writer.raw("}");
        return li_native::return_json_string(output, writer.str());
    }
    catch (const std::exception& exception) {
        return li_native::return_error_json(output, "usb", exception.what());
    }
    catch (...) {
        return li_native::return_error_json(output, "usb", "Необработанное исключение при сборе USB.");
    }
#endif
}
