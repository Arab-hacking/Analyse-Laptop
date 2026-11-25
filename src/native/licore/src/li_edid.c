#include "li_internal.h"

#include <stdio.h>
#include <string.h>

static const uint8_t edid_header[8] = { 0x00u, 0xFFu, 0xFFu, 0xFFu, 0xFFu, 0xFFu, 0xFFu, 0x00u };

static void edid_manufacturer(char output[4], const uint8_t* edid)
{
    uint16_t word = li_read_be16(edid + 8u);
    output[0] = (char)('@' + ((word >> 10u) & 0x1Fu));
    output[1] = (char)('@' + ((word >> 5u) & 0x1Fu));
    output[2] = (char)('@' + (word & 0x1Fu));
    output[3] = '\0';
    for (uintptr_t index = 0u; index < 3u; ++index) {
        if (output[index] < 'A' || output[index] > 'Z') {
            output[index] = '?';
        }
    }
}

static const char* edid_extension_name(uint8_t tag)
{
    switch (tag) {
    case 0x02u: return "CTA-861";
    case 0x10u: return "VTB-EXT";
    case 0x20u: return "EDID 2.0";
    case 0x30u: return "Color Information";
    case 0x40u: return "DVI Feature Data";
    case 0x50u: return "Localized String";
    case 0x60u: return "Microdisplay Interface";
    case 0x70u: return "DisplayID";
    case 0xF0u: return "Block Map";
    case 0xFFu: return "Manufacturer Defined";
    default: return "Unknown";
    }
}

static const char* cta_block_name(uint8_t tag)
{
    switch (tag) {
    case 1u: return "Audio Data Block";
    case 2u: return "Video Data Block";
    case 3u: return "Vendor-Specific Data Block";
    case 4u: return "Speaker Allocation Data Block";
    case 5u: return "VESA Display Transfer Characteristic";
    case 6u: return "Reserved";
    case 7u: return "Extended Data Block";
    default: return "Reserved";
    }
}

static const char* cta_extended_block_name(uint8_t tag)
{
    switch (tag) {
    case 0u: return "Video Capability Data Block";
    case 1u: return "Vendor-Specific Video Data Block";
    case 2u: return "VESA Video Display Device Data Block";
    case 5u: return "Colorimetry Data Block";
    case 6u: return "HDR Static Metadata Data Block";
    case 7u: return "HDR Dynamic Metadata Data Block";
    case 13u: return "Video Format Preference Data Block";
    case 14u: return "YCbCr 4:2:0 Video Data Block";
    case 15u: return "YCbCr 4:2:0 Capability Map Data Block";
    case 32u: return "InfoFrame Data Block";
    case 34u: return "HDMI Forum VSDB";
    default: return "Unknown Extended Data Block";
    }
}

static void append_fixed_descriptor_text(li_json_builder* builder, const uint8_t* descriptor)
{
    char value[14];
    li_fixed_ascii(value, sizeof(value), descriptor + 5u, 13u);
    li_json_append_escaped(builder, value);
}

static void append_descriptor(li_json_builder* builder, const uint8_t* descriptor)
{
    uint16_t pixel_clock = li_read_le16(descriptor);
    li_json_append_char(builder, '{');
    if (pixel_clock == 0u) {
        uint8_t tag = descriptor[3u];
        li_json_append(builder, "\"kind\":\"monitorDescriptor\"");
        li_json_append_format(builder, ",\"tag\":%u", (unsigned int)tag);
        switch (tag) {
        case 0xFCu:
            li_json_append(builder, ",\"descriptorType\":\"displayName\",\"text\":");
            append_fixed_descriptor_text(builder, descriptor);
            break;
        case 0xFFu:
            li_json_append(builder, ",\"descriptorType\":\"serialNumber\",\"text\":");
            append_fixed_descriptor_text(builder, descriptor);
            break;
        case 0xFEu:
            li_json_append(builder, ",\"descriptorType\":\"asciiString\",\"text\":");
            append_fixed_descriptor_text(builder, descriptor);
            break;
        case 0xFDu:
            li_json_append(builder, ",\"descriptorType\":\"displayRangeLimits\"");
            li_json_append_format(builder, ",\"minVerticalHz\":%u,\"maxVerticalHz\":%u", (unsigned int)descriptor[5u], (unsigned int)descriptor[6u]);
            li_json_append_format(builder, ",\"minHorizontalKHz\":%u,\"maxHorizontalKHz\":%u", (unsigned int)descriptor[7u], (unsigned int)descriptor[8u]);
            li_json_append_format(builder, ",\"maxPixelClockMHz\":%u", (unsigned int)descriptor[9u] * 10u);
            break;
        default:
            li_json_append(builder, ",\"descriptorType\":\"unknown\",\"payloadHex\":");
            li_json_append_hex(builder, descriptor, 18u);
            break;
        }
    }
    else {
        uint16_t h_active = (uint16_t)descriptor[2u] | (uint16_t)((descriptor[4u] & 0xF0u) << 4u);
        uint16_t h_blank = (uint16_t)descriptor[3u] | (uint16_t)((descriptor[4u] & 0x0Fu) << 8u);
        uint16_t v_active = (uint16_t)descriptor[5u] | (uint16_t)((descriptor[7u] & 0xF0u) << 4u);
        uint16_t v_blank = (uint16_t)descriptor[6u] | (uint16_t)((descriptor[7u] & 0x0Fu) << 8u);
        uint16_t h_size = (uint16_t)descriptor[12u] | (uint16_t)((descriptor[14u] & 0xF0u) << 4u);
        uint16_t v_size = (uint16_t)descriptor[13u] | (uint16_t)((descriptor[14u] & 0x0Fu) << 8u);
        li_json_append(builder, "\"kind\":\"detailedTiming\"");
        li_json_append_format(builder, ",\"pixelClockKHz\":%u", (unsigned int)pixel_clock * 10u);
        li_json_append_format(builder, ",\"horizontalActive\":%u,\"horizontalBlanking\":%u", (unsigned int)h_active, (unsigned int)h_blank);
        li_json_append_format(builder, ",\"verticalActive\":%u,\"verticalBlanking\":%u", (unsigned int)v_active, (unsigned int)v_blank);
        li_json_append_format(builder, ",\"imageWidthMillimeters\":%u,\"imageHeightMillimeters\":%u", (unsigned int)h_size, (unsigned int)v_size);
        li_json_append_format(builder, ",\"interlaced\":%s", (descriptor[17u] & 0x80u) != 0u ? "true" : "false");
    }

    li_json_append_char(builder, '}');
}

static void append_standard_timings(li_json_builder* builder, const uint8_t* edid)
{
    li_json_append(builder, ",\"standardTimings\":[");
    int first = 1;
    for (uintptr_t index = 0u; index < 8u; ++index) {
        const uint8_t* timing = edid + 38u + (index * 2u);
        if (timing[0u] == 0x01u && timing[1u] == 0x01u) {
            continue;
        }

        unsigned int horizontal = ((unsigned int)timing[0u] + 31u) * 8u;
        unsigned int aspect_code = (timing[1u] >> 6u) & 0x03u;
        unsigned int vertical = 0u;
        switch (aspect_code) {
        case 0u: vertical = horizontal * 10u / 16u; break;
        case 1u: vertical = horizontal * 3u / 4u; break;
        case 2u: vertical = horizontal * 4u / 5u; break;
        default: vertical = horizontal * 9u / 16u; break;
        }

        unsigned int refresh = (timing[1u] & 0x3Fu) + 60u;
        if (!first) {
            li_json_append_char(builder, ',');
        }

        first = 0;
        li_json_append_format(builder, "{\"horizontal\":%u,\"vertical\":%u,\"refreshHz\":%u}", horizontal, vertical, refresh);
    }

    li_json_append_char(builder, ']');
}

static void append_chromaticity(li_json_builder* builder, const uint8_t* edid)
{
    uint16_t rx = (uint16_t)(((edid[25u] >> 6u) & 0x03u) | ((uint16_t)edid[27u] << 2u));
    uint16_t ry = (uint16_t)(((edid[25u] >> 4u) & 0x03u) | ((uint16_t)edid[28u] << 2u));
    uint16_t gx = (uint16_t)(((edid[25u] >> 2u) & 0x03u) | ((uint16_t)edid[29u] << 2u));
    uint16_t gy = (uint16_t)((edid[25u] & 0x03u) | ((uint16_t)edid[30u] << 2u));
    uint16_t bx = (uint16_t)(((edid[26u] >> 6u) & 0x03u) | ((uint16_t)edid[31u] << 2u));
    uint16_t by = (uint16_t)(((edid[26u] >> 4u) & 0x03u) | ((uint16_t)edid[32u] << 2u));
    uint16_t wx = (uint16_t)(((edid[26u] >> 2u) & 0x03u) | ((uint16_t)edid[33u] << 2u));
    uint16_t wy = (uint16_t)((edid[26u] & 0x03u) | ((uint16_t)edid[34u] << 2u));
    li_json_append(builder, ",\"chromaticity\":{");
    li_json_append_format(builder, "\"redX\":%.4f,\"redY\":%.4f", (double)rx / 1024.0, (double)ry / 1024.0);
    li_json_append_format(builder, ",\"greenX\":%.4f,\"greenY\":%.4f", (double)gx / 1024.0, (double)gy / 1024.0);
    li_json_append_format(builder, ",\"blueX\":%.4f,\"blueY\":%.4f", (double)bx / 1024.0, (double)by / 1024.0);
    li_json_append_format(builder, ",\"whiteX\":%.4f,\"whiteY\":%.4f", (double)wx / 1024.0, (double)wy / 1024.0);
    li_json_append_char(builder, '}');
}

static void append_base_block(li_json_builder* builder, const uint8_t* edid)
{
    char manufacturer[4];
    edid_manufacturer(manufacturer, edid);
    li_json_append(builder, "\"baseBlock\":{");
    li_json_append(builder, "\"headerValid\":");
    li_json_append_bool(builder, memcmp(edid, edid_header, sizeof(edid_header)) == 0);
    li_json_append(builder, ",\"checksumValid\":");
    li_json_append_bool(builder, li_checksum8_is_valid(edid, 128u));
    li_json_append(builder, ",\"manufacturerId\":");
    li_json_append_escaped(builder, manufacturer);
    li_json_append_format(builder, ",\"productCode\":%u", (unsigned int)li_read_le16(edid + 10u));
    li_json_append_format(builder, ",\"serialNumber\":%lu", (unsigned long)li_read_le32(edid + 12u));
    li_json_append_format(builder, ",\"manufactureWeek\":%u", (unsigned int)edid[16u]);
    li_json_append_format(builder, ",\"manufactureYear\":%u", (unsigned int)edid[17u] + 1990u);
    li_json_append_format(builder, ",\"version\":%u,\"revision\":%u", (unsigned int)edid[18u], (unsigned int)edid[19u]);
    li_json_append_format(builder, ",\"videoInputRaw\":%u", (unsigned int)edid[20u]);
    li_json_append_format(builder, ",\"digitalInput\":%s", (edid[20u] & 0x80u) != 0u ? "true" : "false");
    li_json_append_format(builder, ",\"widthCentimeters\":%u,\"heightCentimeters\":%u", (unsigned int)edid[21u], (unsigned int)edid[22u]);
    if (edid[23u] == 0xFFu) {
        li_json_append(builder, ",\"gamma\":null");
    }
    else {
        li_json_append_format(builder, ",\"gamma\":%.2f", ((double)edid[23u] + 100.0) / 100.0);
    }

    li_json_append_format(builder, ",\"featuresRaw\":%u", (unsigned int)edid[24u]);
    append_chromaticity(builder, edid);
    li_json_append(builder, ",\"establishedTimingsHex\":");
    li_json_append_hex(builder, edid + 35u, 3u);
    append_standard_timings(builder, edid);
    li_json_append(builder, ",\"descriptors\":[");
    for (uintptr_t index = 0u; index < 4u; ++index) {
        if (index != 0u) {
            li_json_append_char(builder, ',');
        }

        append_descriptor(builder, edid + 54u + (index * 18u));
    }

    li_json_append_format(builder, "],\"extensionCount\":%u", (unsigned int)edid[126u]);
    li_json_append_char(builder, '}');
}

static void append_cta_extension(li_json_builder* builder, const uint8_t* extension)
{
    uint8_t dtd_start = extension[2u] == 0u ? 127u : extension[2u];
    if (dtd_start > 127u) {
        dtd_start = 127u;
    }

    li_json_append_format(builder, ",\"revision\":%u,\"dtdStart\":%u,\"flags\":%u", (unsigned int)extension[1u], (unsigned int)dtd_start, (unsigned int)extension[3u]);
    li_json_append(builder, ",\"dataBlocks\":[");
    uintptr_t offset = 4u;
    int first = 1;
    while (offset < dtd_start) {
        uint8_t header = extension[offset++];
        uint8_t tag = (uint8_t)((header >> 5u) & 0x07u);
        uint8_t block_length = (uint8_t)(header & 0x1Fu);
        if (offset + block_length > dtd_start) {
            break;
        }

        if (!first) {
            li_json_append_char(builder, ',');
        }

        first = 0;
        li_json_append_format(builder, "{\"tag\":%u,\"name\":", (unsigned int)tag);
        li_json_append_escaped(builder, cta_block_name(tag));
        li_json_append_format(builder, ",\"length\":%u", (unsigned int)block_length);
        if (tag == 7u && block_length > 0u) {
            uint8_t extended_tag = extension[offset];
            li_json_append_format(builder, ",\"extendedTag\":%u,\"extendedName\":", (unsigned int)extended_tag);
            li_json_append_escaped(builder, cta_extended_block_name(extended_tag));
        }
        else if (tag == 2u) {
            li_json_append(builder, ",\"videoIdentificationCodes\":[");
            for (uint8_t vic_index = 0u; vic_index < block_length; ++vic_index) {
                if (vic_index != 0u) {
                    li_json_append_char(builder, ',');
                }

                li_json_append_format(builder, "%u", (unsigned int)(extension[offset + vic_index] & 0x7Fu));
            }

            li_json_append_char(builder, ']');
        }
        else if (tag == 3u && block_length >= 3u) {
            uint32_t oui = (uint32_t)extension[offset] | ((uint32_t)extension[offset + 1u] << 8u) | ((uint32_t)extension[offset + 2u] << 16u);
            li_json_append_format(builder, ",\"ieeeOui\":\"%06lx\"", (unsigned long)oui);
        }

        li_json_append(builder, ",\"payloadHex\":");
        li_json_append_hex(builder, extension + offset, block_length);
        li_json_append_char(builder, '}');
        offset += block_length;
    }

    li_json_append_char(builder, ']');
}

static void append_displayid_extension(li_json_builder* builder, const uint8_t* extension)
{
    uint8_t payload_length = extension[2u];
    if (payload_length > 121u) {
        payload_length = 121u;
    }

    li_json_append_format(builder, ",\"displayIdRevision\":%u,\"payloadLength\":%u,\"productType\":%u", (unsigned int)extension[1u], (unsigned int)payload_length, (unsigned int)extension[3u]);
    li_json_append(builder, ",\"dataBlocks\":[");
    uintptr_t offset = 5u;
    uintptr_t end = 5u + payload_length;
    int first = 1;
    while (offset + 3u <= end && offset + 3u <= 127u) {
        uint8_t tag = extension[offset];
        uint8_t revision = extension[offset + 1u];
        uint8_t block_length = extension[offset + 2u];
        offset += 3u;
        if (offset + block_length > end || offset + block_length > 127u) {
            break;
        }

        if (!first) {
            li_json_append_char(builder, ',');
        }

        first = 0;
        li_json_append_format(builder, "{\"tag\":%u,\"revision\":%u,\"length\":%u,\"payloadHex\":", (unsigned int)tag, (unsigned int)revision, (unsigned int)block_length);
        li_json_append_hex(builder, extension + offset, block_length);
        li_json_append_char(builder, '}');
        offset += block_length;
    }

    li_json_append_char(builder, ']');
}

static void append_extension(li_json_builder* builder, const uint8_t* extension, unsigned int index)
{
    uint8_t tag = extension[0u];
    li_json_append_char(builder, '{');
    li_json_append_format(builder, "\"index\":%u,\"tag\":%u,\"name\":", index, (unsigned int)tag);
    li_json_append_escaped(builder, edid_extension_name(tag));
    li_json_append(builder, ",\"checksumValid\":");
    li_json_append_bool(builder, li_checksum8_is_valid(extension, 128u));

    if (tag == 0x02u) {
        append_cta_extension(builder, extension);
    }
    else if (tag == 0x70u) {
        append_displayid_extension(builder, extension);
    }
    else {
        li_json_append(builder, ",\"payloadHex\":");
        li_json_append_hex(builder, extension + 1u, 126u);
    }

    li_json_append_char(builder, '}');
}

int32_t li_edid_to_json(const uint8_t* data, uintptr_t length, li_json_output* output)
{
    if ((data == NULL && length != 0u) || output == NULL) {
        return LI_STATUS_INVALID_ARGUMENT;
    }

    output->data = NULL;
    output->length = 0u;
    li_json_builder builder;
    li_json_init(&builder);
    li_json_append(&builder, "{\"parser\":\"edid\",");
    if (length < 128u) {
        li_json_append_format(&builder, "\"status\":\"truncated\",\"inputLength\":%llu}", (unsigned long long)length);
        int32_t detach_status = li_json_detach(&builder, output);
        return detach_status == LI_STATUS_OK ? LI_STATUS_TRUNCATED : detach_status;
    }

    li_json_append(&builder, "\"status\":\"ok\",");
    li_json_append_format(&builder, "\"inputLength\":%llu,", (unsigned long long)length);
    append_base_block(&builder, data);
    uint8_t extension_count = data[126u];
    uintptr_t available_extensions = length / 128u;
    if (available_extensions > 0u) {
        --available_extensions;
    }

    int partial = available_extensions < (uintptr_t)extension_count;
    li_json_append(&builder, ",\"extensions\":[");
    uintptr_t parse_count = available_extensions < (uintptr_t)extension_count ? available_extensions : (uintptr_t)extension_count;
    for (uintptr_t index = 0u; index < parse_count; ++index) {
        if (index != 0u) {
            li_json_append_char(&builder, ',');
        }

        append_extension(&builder, data + 128u + (index * 128u), (unsigned int)index + 1u);
    }

    li_json_append(&builder, "]");
    li_json_append(&builder, ",\"partial\":");
    li_json_append_bool(&builder, partial);
    li_json_append_char(&builder, '}');

    int32_t status = li_json_detach(&builder, output);
    if (status != LI_STATUS_OK) {
        return status;
    }

    return partial ? LI_STATUS_TRUNCATED : LI_STATUS_OK;
}
