#include "li_internal.h"

#include <string.h>

static const char* smart_attribute_name(uint8_t id)
{
    switch (id) {
    case 1u: return "Read Error Rate";
    case 2u: return "Throughput Performance";
    case 3u: return "Spin-Up Time";
    case 4u: return "Start/Stop Count";
    case 5u: return "Reallocated Sectors Count";
    case 7u: return "Seek Error Rate";
    case 8u: return "Seek Time Performance";
    case 9u: return "Power-On Hours";
    case 10u: return "Spin Retry Count";
    case 11u: return "Recalibration Retries";
    case 12u: return "Power Cycle Count";
    case 13u: return "Soft Read Error Rate";
    case 22u: return "Current Helium Level";
    case 170u: return "Available Reserved Space";
    case 171u: return "SSD Program Fail Count";
    case 172u: return "SSD Erase Fail Count";
    case 173u: return "SSD Wear Leveling Count";
    case 174u: return "Unexpected Power Loss Count";
    case 175u: return "Program Fail Count";
    case 176u: return "Erase Fail Count";
    case 177u: return "Wear Leveling Count";
    case 178u: return "Used Reserved Block Count";
    case 179u: return "Used Reserved Block Count Total";
    case 180u: return "Unused Reserved Block Count Total";
    case 181u: return "Program Fail Count Total";
    case 182u: return "Erase Fail Count Total";
    case 183u: return "SATA Downshift Count";
    case 184u: return "End-to-End Error";
    case 187u: return "Reported Uncorrectable Errors";
    case 188u: return "Command Timeout";
    case 189u: return "High Fly Writes";
    case 190u: return "Airflow Temperature";
    case 191u: return "G-Sense Error Rate";
    case 192u: return "Power-Off Retract Count";
    case 193u: return "Load/Unload Cycle Count";
    case 194u: return "Temperature";
    case 195u: return "Hardware ECC Recovered";
    case 196u: return "Reallocation Event Count";
    case 197u: return "Current Pending Sector Count";
    case 198u: return "Offline Uncorrectable Sector Count";
    case 199u: return "UltraDMA CRC Error Count";
    case 200u: return "Multi-Zone Error Rate";
    case 201u: return "Soft Read Error Rate";
    case 202u: return "Data Address Mark Errors";
    case 203u: return "Run Out Cancel";
    case 204u: return "Soft ECC Correction";
    case 205u: return "Thermal Asperity Rate";
    case 206u: return "Flying Height";
    case 207u: return "Spin High Current";
    case 208u: return "Spin Buzz";
    case 209u: return "Offline Seek Performance";
    case 220u: return "Disk Shift";
    case 221u: return "G-Sense Error Rate";
    case 222u: return "Loaded Hours";
    case 223u: return "Load/Unload Retry Count";
    case 224u: return "Load Friction";
    case 225u: return "Load/Unload Cycle Count";
    case 226u: return "Load-In Time";
    case 227u: return "Torque Amplification Count";
    case 228u: return "Power-Off Retract Cycle";
    case 230u: return "Drive Life Protection Status";
    case 231u: return "SSD Life Left";
    case 232u: return "Available Reserved Space";
    case 233u: return "Media Wearout Indicator";
    case 234u: return "Average Erase Count";
    case 235u: return "Good Block Count";
    case 240u: return "Head Flying Hours";
    case 241u: return "Total LBAs Written";
    case 242u: return "Total LBAs Read";
    case 243u: return "Total LBAs Written Expanded";
    case 244u: return "Total LBAs Read Expanded";
    case 245u: return "Remaining Rated Write Endurance";
    case 246u: return "Total LBAs Written To NAND";
    case 247u: return "Host Program Page Count";
    case 248u: return "FTL Program Page Count";
    default: return "Vendor Specific";
    }
}

static uint64_t smart_raw48(const uint8_t* data)
{
    uint64_t value = 0u;
    for (uintptr_t index = 0u; index < 6u; ++index) {
        value |= ((uint64_t)data[index]) << (index * 8u);
    }

    return value;
}

static void append_smart_raw_interpretation(li_json_builder* builder, uint8_t id, const uint8_t* raw)
{
    uint64_t raw_value = smart_raw48(raw);
    li_json_append_format(builder, ",\"rawValue\":%llu", (unsigned long long)raw_value);
    li_json_append(builder, ",\"rawHex\":");
    li_json_append_hex(builder, raw, 6u);

    if (id == 194u || id == 190u) {
        li_json_append_format(builder, ",\"temperatureCelsius\":%u", (unsigned int)raw[0u]);
    }
    else if (id == 9u || id == 240u) {
        li_json_append_format(builder, ",\"hours\":%llu", (unsigned long long)raw_value);
    }
    else if (id == 241u || id == 242u) {
        double tib = ((double)raw_value * 512.0) / (1024.0 * 1024.0 * 1024.0 * 1024.0);
        li_json_append_format(builder, ",\"estimatedTebibytes\":%.3f", tib);
    }
}

int32_t li_smart_ata_to_json(const uint8_t* data, uintptr_t length, li_json_output* output)
{
    if ((data == NULL && length != 0u) || output == NULL) {
        return LI_STATUS_INVALID_ARGUMENT;
    }

    output->data = NULL;
    output->length = 0u;
    li_json_builder builder;
    li_json_init(&builder);
    if (length < 512u) {
        li_json_append_format(&builder, "{\"parser\":\"ataSmart\",\"status\":\"truncated\",\"inputLength\":%llu}", (unsigned long long)length);
        int32_t detach_status = li_json_detach(&builder, output);
        return detach_status == LI_STATUS_OK ? LI_STATUS_TRUNCATED : detach_status;
    }

    li_json_append(&builder, "{\"parser\":\"ataSmart\",\"status\":\"ok\"");
    li_json_append_format(&builder, ",\"revision\":%u", (unsigned int)li_read_le16(data));
    li_json_append(&builder, ",\"checksum\":");
    li_json_append_bool(&builder, li_checksum8_is_valid(data, 512u));
    li_json_append(&builder, ",\"attributes\":[");
    int first = 1;
    unsigned int count = 0u;
    for (uintptr_t index = 0u; index < 30u; ++index) {
        const uint8_t* attribute = data + 2u + (index * 12u);
        uint8_t id = attribute[0u];
        if (id == 0u) {
            continue;
        }

        uint16_t flags = li_read_le16(attribute + 1u);
        if (!first) {
            li_json_append_char(&builder, ',');
        }

        first = 0;
        ++count;
        li_json_append_format(&builder, "{\"id\":%u,\"name\":", (unsigned int)id);
        li_json_append_escaped(&builder, smart_attribute_name(id));
        li_json_append_format(&builder, ",\"flags\":%u,\"prefailure\":%s,\"onlineCollection\":%s", (unsigned int)flags, (flags & 0x0001u) != 0u ? "true" : "false", (flags & 0x0002u) != 0u ? "true" : "false");
        li_json_append_format(&builder, ",\"value\":%u,\"worst\":%u,\"threshold\":null", (unsigned int)attribute[3u], (unsigned int)attribute[4u]);
        append_smart_raw_interpretation(&builder, id, attribute + 5u);
        li_json_append_char(&builder, '}');
    }

    li_json_append(&builder, "]");
    li_json_append_format(&builder, ",\"attributeCount\":%u", count);
    li_json_append_char(&builder, '}');
    int32_t status = li_json_detach(&builder, output);
    return status;
}

static void append_nvme_counter(li_json_builder* builder, const char* key, const uint8_t* data)
{
    char decimal[50];
    li_uint128_le_to_decimal(data, decimal, sizeof(decimal));
    li_json_append_format(builder, ",\"%s\":{\"raw\":", key);
    li_json_append_escaped(builder, decimal);
    li_json_append(builder, ",\"rawHex\":");
    li_json_append_hex(builder, data, 16u);
    li_json_append_char(builder, '}');
}

static void append_nvme_data_units(li_json_builder* builder, const char* key, const uint8_t* data)
{
    char decimal[50];
    double units = li_uint128_le_to_double(data);
    double terabytes = (units * 512000.0) / 1000000000000.0;
    li_uint128_le_to_decimal(data, decimal, sizeof(decimal));
    li_json_append_format(builder, ",\"%s\":{\"raw\":", key);
    li_json_append_escaped(builder, decimal);
    li_json_append_format(builder, ",\"estimatedTerabytesDecimal\":%.3f", terabytes);
    li_json_append(builder, ",\"rawHex\":");
    li_json_append_hex(builder, data, 16u);
    li_json_append_char(builder, '}');
}

static void append_nvme_temperature(li_json_builder* builder, const char* key, uint16_t kelvin)
{
    li_json_append_format(builder, ",\"%s\":", key);
    if (kelvin == 0u) {
        li_json_append(builder, "null");
    }
    else {
        li_json_append_format(builder, "%.2f", (double)kelvin - 273.15);
    }
}

static void append_critical_warning_bits(li_json_builder* builder, uint8_t warning)
{
    li_json_append(builder, ",\"criticalWarningBits\":{");
    li_json_append_format(builder, "\"availableSpareBelowThreshold\":%s", (warning & 0x01u) != 0u ? "true" : "false");
    li_json_append_format(builder, ",\"temperatureThreshold\":%s", (warning & 0x02u) != 0u ? "true" : "false");
    li_json_append_format(builder, ",\"reliabilityDegraded\":%s", (warning & 0x04u) != 0u ? "true" : "false");
    li_json_append_format(builder, ",\"mediaReadOnly\":%s", (warning & 0x08u) != 0u ? "true" : "false");
    li_json_append_format(builder, ",\"volatileMemoryBackupFailed\":%s", (warning & 0x10u) != 0u ? "true" : "false");
    li_json_append_format(builder, ",\"persistentMemoryRegionReadOnly\":%s", (warning & 0x20u) != 0u ? "true" : "false");
    li_json_append_char(builder, '}');
}

int32_t li_nvme_health_to_json(const uint8_t* data, uintptr_t length, li_json_output* output)
{
    if ((data == NULL && length != 0u) || output == NULL) {
        return LI_STATUS_INVALID_ARGUMENT;
    }

    output->data = NULL;
    output->length = 0u;
    li_json_builder builder;
    li_json_init(&builder);
    if (length < 512u) {
        li_json_append_format(&builder, "{\"parser\":\"nvmeHealth\",\"status\":\"truncated\",\"inputLength\":%llu}", (unsigned long long)length);
        int32_t detach_status = li_json_detach(&builder, output);
        return detach_status == LI_STATUS_OK ? LI_STATUS_TRUNCATED : detach_status;
    }

    uint8_t warning = data[0u];
    uint16_t composite_kelvin = li_read_le16(data + 1u);
    li_json_append(&builder, "{\"parser\":\"nvmeHealth\",\"status\":\"ok\"");
    li_json_append_format(&builder, ",\"criticalWarning\":%u", (unsigned int)warning);
    append_critical_warning_bits(&builder, warning);
    append_nvme_temperature(&builder, "temperatureCelsius", composite_kelvin);
    li_json_append_format(&builder, ",\"availableSparePercent\":%u,\"availableSpareThresholdPercent\":%u,\"percentageUsed\":%u", (unsigned int)data[3u], (unsigned int)data[4u], (unsigned int)data[5u]);
    append_nvme_data_units(&builder, "dataUnitsRead", data + 32u);
    append_nvme_data_units(&builder, "dataUnitsWritten", data + 48u);
    append_nvme_counter(&builder, "hostReadCommands", data + 64u);
    append_nvme_counter(&builder, "hostWriteCommands", data + 80u);
    append_nvme_counter(&builder, "controllerBusyTimeMinutes", data + 96u);
    append_nvme_counter(&builder, "powerCycles", data + 112u);
    append_nvme_counter(&builder, "powerOnHours", data + 128u);
    append_nvme_counter(&builder, "unsafeShutdowns", data + 144u);
    append_nvme_counter(&builder, "mediaAndDataIntegrityErrors", data + 160u);
    append_nvme_counter(&builder, "errorInformationLogEntries", data + 176u);
    li_json_append_format(&builder, ",\"warningCompositeTemperatureTimeMinutes\":%lu", (unsigned long)li_read_le32(data + 192u));
    li_json_append_format(&builder, ",\"criticalCompositeTemperatureTimeMinutes\":%lu", (unsigned long)li_read_le32(data + 196u));
    li_json_append(&builder, ",\"temperatureSensorsCelsius\":[");
    for (uintptr_t index = 0u; index < 8u; ++index) {
        if (index != 0u) {
            li_json_append_char(&builder, ',');
        }

        uint16_t sensor_kelvin = li_read_le16(data + 200u + (index * 2u));
        if (sensor_kelvin == 0u) {
            li_json_append(&builder, "null");
        }
        else {
            li_json_append_format(&builder, "%.2f", (double)sensor_kelvin - 273.15);
        }
    }

    li_json_append_char(&builder, ']');
    li_json_append_format(&builder, ",\"thermalManagementTemperature1TransitionCount\":%lu", (unsigned long)li_read_le32(data + 216u));
    li_json_append_format(&builder, ",\"thermalManagementTemperature2TransitionCount\":%lu", (unsigned long)li_read_le32(data + 220u));
    li_json_append_format(&builder, ",\"totalTimeForThermalManagementTemperature1Minutes\":%lu", (unsigned long)li_read_le32(data + 224u));
    li_json_append_format(&builder, ",\"totalTimeForThermalManagementTemperature2Minutes\":%lu", (unsigned long)li_read_le32(data + 228u));
    li_json_append_char(&builder, '}');
    int32_t status = li_json_detach(&builder, output);
    return status;
}
