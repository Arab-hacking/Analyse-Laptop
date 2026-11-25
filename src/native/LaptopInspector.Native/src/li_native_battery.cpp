#include "li_native.h"
#include "native_common.h"

#include <exception>
#include <string>
#include <vector>

#ifdef _WIN32
#define NOMINMAX
#include <windows.h>
#include <batclass.h>
#include <setupapi.h>
#endif

namespace {
#ifdef _WIN32
struct DeviceInfoSetGuard final {
    HDEVINFO value;
    ~DeviceInfoSetGuard() { SetupDiDestroyDeviceInfoList(value); }
};

struct HandleGuard final {
    HANDLE value;
    ~HandleGuard() { CloseHandle(value); }
};

std::wstring battery_device_path(HDEVINFO info, SP_DEVICE_INTERFACE_DATA& interface_data)
{
    DWORD required_size = 0u;
    SetupDiGetDeviceInterfaceDetailW(info, &interface_data, nullptr, 0u, &required_size, nullptr);
    if (required_size == 0u) {
        return {};
    }

    std::vector<BYTE> buffer(required_size);
    auto* detail = reinterpret_cast<SP_DEVICE_INTERFACE_DETAIL_DATA_W*>(buffer.data());
    detail->cbSize = sizeof(SP_DEVICE_INTERFACE_DETAIL_DATA_W);
    if (!SetupDiGetDeviceInterfaceDetailW(info, &interface_data, detail, required_size, nullptr, nullptr)) {
        return {};
    }

    return detail->DevicePath;
}

bool battery_query_tag(HANDLE handle, ULONG& tag)
{
    DWORD bytes_returned = 0u;
    ULONG wait = 0u;
    tag = 0u;
    return DeviceIoControl(handle, IOCTL_BATTERY_QUERY_TAG, &wait, sizeof(wait), &tag, sizeof(tag), &bytes_returned, nullptr) != FALSE && tag != 0u;
}

bool battery_query_information(HANDLE handle, ULONG tag, BATTERY_QUERY_INFORMATION& query, void* output, DWORD output_size)
{
    DWORD bytes_returned = 0u;
    query.BatteryTag = tag;
    return DeviceIoControl(handle, IOCTL_BATTERY_QUERY_INFORMATION, &query, sizeof(query), output, output_size, &bytes_returned, nullptr) != FALSE;
}

std::string battery_query_string(HANDLE handle, ULONG tag, BATTERY_QUERY_INFORMATION_LEVEL level)
{
    BATTERY_QUERY_INFORMATION query{};
    query.InformationLevel = level;
    std::vector<wchar_t> buffer(256u);
    if (!battery_query_information(handle, tag, query, buffer.data(), static_cast<DWORD>(buffer.size() * sizeof(wchar_t)))) {
        return {};
    }

    std::size_t length = wcsnlen_s(buffer.data(), buffer.size());
    return li_native::wide_to_utf8(std::wstring_view(buffer.data(), length));
}

void append_battery_information(li_native::JsonWriter& writer, HANDLE handle, ULONG tag)
{
    BATTERY_QUERY_INFORMATION query{};
    query.InformationLevel = BatteryInformation;
    BATTERY_INFORMATION information{};
    writer.raw(",\"information\":");
    if (!battery_query_information(handle, tag, query, &information, sizeof(information))) {
        writer.null_value();
        return;
    }

    char chemistry[5]{};
    for (std::size_t index = 0u; index < 4u; ++index) {
        chemistry[index] = static_cast<char>(information.Chemistry[index]);
    }

    writer.begin_object();
    writer.key("capabilities");
    writer.unsigned_number(information.Capabilities);
    writer.raw(",\"technology\":");
    writer.string(information.Technology == 0u ? "Non-rechargeable" : "Rechargeable");
    writer.raw(",\"chemistry\":");
    writer.string(chemistry);
    writer.raw(",\"designedCapacityMWh\":");
    writer.unsigned_number(information.DesignedCapacity);
    writer.raw(",\"fullChargedCapacityMWh\":");
    writer.unsigned_number(information.FullChargedCapacity);
    writer.raw(",\"defaultAlert1MWh\":");
    writer.unsigned_number(information.DefaultAlert1);
    writer.raw(",\"defaultAlert2MWh\":");
    writer.unsigned_number(information.DefaultAlert2);
    writer.raw(",\"criticalBiasMWh\":");
    writer.unsigned_number(information.CriticalBias);
    writer.raw(",\"cycleCount\":");
    if (information.CycleCount == 0u) {
        writer.null_value();
    }
    else {
        writer.unsigned_number(information.CycleCount);
    }

    writer.raw(",\"wearPercent\":");
    if (information.DesignedCapacity != 0u && information.DesignedCapacity != BATTERY_UNKNOWN_CAPACITY && information.FullChargedCapacity != BATTERY_UNKNOWN_CAPACITY) {
        double wear = (1.0 - (static_cast<double>(information.FullChargedCapacity) / static_cast<double>(information.DesignedCapacity))) * 100.0;
        writer.double_number(wear < 0.0 ? 0.0 : wear);
    }
    else {
        writer.null_value();
    }

    writer.end_object();
}

void append_battery_status(li_native::JsonWriter& writer, HANDLE handle, ULONG tag)
{
    BATTERY_WAIT_STATUS wait_status{};
    wait_status.BatteryTag = tag;
    BATTERY_STATUS status{};
    DWORD bytes_returned = 0u;
    writer.raw(",\"statusInfo\":");
    if (DeviceIoControl(handle, IOCTL_BATTERY_QUERY_STATUS, &wait_status, sizeof(wait_status), &status, sizeof(status), &bytes_returned, nullptr) == FALSE) {
        writer.null_value();
        return;
    }

    writer.begin_object();
    writer.key("powerState");
    writer.unsigned_number(status.PowerState);
    writer.raw(",\"capacityMWh\":");
    if (status.Capacity == BATTERY_UNKNOWN_CAPACITY) {
        writer.null_value();
    }
    else {
        writer.unsigned_number(status.Capacity);
    }

    writer.raw(",\"voltageMillivolts\":");
    if (status.Voltage == BATTERY_UNKNOWN_VOLTAGE) {
        writer.null_value();
    }
    else {
        writer.unsigned_number(status.Voltage);
    }

    writer.raw(",\"rateMilliwatts\":");
    if (status.Rate == BATTERY_UNKNOWN_RATE) {
        writer.null_value();
    }
    else {
        writer.signed_number(status.Rate);
    }

    writer.raw(",\"charging\":");
    writer.boolean((status.PowerState & BATTERY_CHARGING) != 0u);
    writer.raw(",\"discharging\":");
    writer.boolean((status.PowerState & BATTERY_DISCHARGING) != 0u);
    writer.raw(",\"acOnline\":");
    writer.boolean((status.PowerState & BATTERY_POWER_ON_LINE) != 0u);
    writer.end_object();
}
#endif
}  // namespace

extern "C" LI_EXPORT int32_t li_collect_battery_json(char** output) noexcept
{
    if (output == nullptr) {
        li_native::set_last_error("Аргумент output равен null.");
        return -1;
    }

    *output = nullptr;
#ifndef _WIN32
    return li_native::return_json_string(output, li_native::unsupported_json("battery", "Battery IOCTL доступен только на Windows."));
#else
    try {
        HDEVINFO info = SetupDiGetClassDevsW(&GUID_DEVINTERFACE_BATTERY, nullptr, nullptr, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (info == INVALID_HANDLE_VALUE) {
            return li_native::return_error_json(output, "battery", li_native::last_win32_error_message());
        }

        DeviceInfoSetGuard guard{ info };
        li_native::JsonWriter writer;
        writer.raw("{\"collector\":\"battery\",\"status\":\"ok\",\"source\":\"SetupAPI+IOCTL_BATTERY\",\"batteries\":[");
        bool first = true;
        unsigned long count = 0u;
        for (DWORD index = 0u;; ++index) {
            SP_DEVICE_INTERFACE_DATA interface_data{};
            interface_data.cbSize = sizeof(interface_data);
            if (!SetupDiEnumDeviceInterfaces(info, nullptr, &GUID_DEVINTERFACE_BATTERY, index, &interface_data)) {
                if (GetLastError() == ERROR_NO_MORE_ITEMS) {
                    break;
                }

                continue;
            }

            std::wstring path = battery_device_path(info, interface_data);
            if (path.empty()) {
                continue;
            }

            HANDLE handle = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
            if (handle == INVALID_HANDLE_VALUE) {
                continue;
            }

            HandleGuard handle_guard{ handle };
            ULONG tag = 0u;
            writer.comma(first);
            writer.begin_object();
            writer.key("devicePath");
            writer.string(li_native::wide_to_utf8(path));
            if (!battery_query_tag(handle, tag)) {
                writer.raw(",\"status\":\"noBatteryTag\"");
                writer.end_object();
                continue;
            }

            writer.raw(",\"tag\":");
            writer.unsigned_number(tag);
            writer.raw(",\"manufacturer\":");
            writer.string(battery_query_string(handle, tag, BatteryManufactureName));
            writer.raw(",\"deviceName\":");
            writer.string(battery_query_string(handle, tag, BatteryDeviceName));
            writer.raw(",\"serialNumber\":");
            writer.string(battery_query_string(handle, tag, BatterySerialNumber));
            writer.raw(",\"uniqueId\":");
            writer.string(battery_query_string(handle, tag, BatteryUniqueID));
            append_battery_information(writer, handle, tag);
            append_battery_status(writer, handle, tag);
            writer.end_object();
            ++count;
        }

        writer.raw("],\"batteryCount\":");
        writer.unsigned_number(count);
        writer.raw("}");
        return li_native::return_json_string(output, writer.str());
    }
    catch (const std::exception& exception) {
        return li_native::return_error_json(output, "battery", exception.what());
    }
    catch (...) {
        return li_native::return_error_json(output, "battery", "Необработанное исключение при сборе батарей.");
    }
#endif
}
