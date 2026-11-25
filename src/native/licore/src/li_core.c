#include "li_internal.h"

#include <ctype.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

uint32_t li_core_get_abi_version(void)
{
    return LI_CORE_ABI_VERSION;
}

void li_core_free(void* pointer)
{
    free(pointer);
}

void li_json_output_free(li_json_output* output)
{
    if (output == NULL) {
        return;
    }

    free(output->data);
    output->data = NULL;
    output->length = 0u;
}

uint32_t li_core_crc32(const uint8_t* data, uintptr_t length)
{
    if (data == NULL && length != 0u) {
        return 0u;
    }

    uint32_t crc = 0xFFFFFFFFu;
    for (uintptr_t index = 0u; index < length; ++index) {
        crc ^= (uint32_t)data[index];
        for (uint32_t bit = 0u; bit < 8u; ++bit) {
            uint32_t mask = 0u - (crc & 1u);
            crc = (crc >> 1u) ^ (0xEDB88320u & mask);
        }
    }

    return ~crc;
}

void li_json_init(li_json_builder* builder)
{
    if (builder == NULL) {
        return;
    }

    builder->data = NULL;
    builder->length = 0u;
    builder->capacity = 0u;
    builder->status = LI_STATUS_OK;
}

void li_json_destroy(li_json_builder* builder)
{
    if (builder == NULL) {
        return;
    }

    free(builder->data);
    builder->data = NULL;
    builder->length = 0u;
    builder->capacity = 0u;
    builder->status = LI_STATUS_OK;
}

int32_t li_json_detach(li_json_builder* builder, li_json_output* output)
{
    if (builder == NULL || output == NULL) {
        return LI_STATUS_INVALID_ARGUMENT;
    }

    output->data = NULL;
    output->length = 0u;
    if (builder->status != LI_STATUS_OK) {
        int32_t status = builder->status;
        li_json_destroy(builder);
        return status;
    }

    if (li_json_append_char(builder, '\0') != LI_STATUS_OK) {
        li_json_destroy(builder);
        return LI_STATUS_OUT_OF_MEMORY;
    }

    output->data = builder->data;
    output->length = builder->length - 1u;
    builder->data = NULL;
    builder->length = 0u;
    builder->capacity = 0u;
    return LI_STATUS_OK;
}

static int32_t li_json_reserve(li_json_builder* builder, uintptr_t additional)
{
    if (builder == NULL) {
        return LI_STATUS_INVALID_ARGUMENT;
    }

    if (builder->status != LI_STATUS_OK) {
        return builder->status;
    }

    if (additional > UINTPTR_MAX - builder->length - 1u) {
        builder->status = LI_STATUS_OUT_OF_MEMORY;
        return builder->status;
    }

    uintptr_t required = builder->length + additional + 1u;
    if (required <= builder->capacity) {
        return LI_STATUS_OK;
    }

    uintptr_t new_capacity = builder->capacity == 0u ? 512u : builder->capacity;
    while (new_capacity < required) {
        if (new_capacity > UINTPTR_MAX / 2u) {
            builder->status = LI_STATUS_OUT_OF_MEMORY;
            return builder->status;
        }

        new_capacity *= 2u;
    }

    char* new_data = (char*)realloc(builder->data, (size_t)new_capacity);
    if (new_data == NULL) {
        builder->status = LI_STATUS_OUT_OF_MEMORY;
        return builder->status;
    }

    builder->data = new_data;
    builder->capacity = new_capacity;
    return LI_STATUS_OK;
}

int32_t li_json_append_char(li_json_builder* builder, char value)
{
    if (li_json_reserve(builder, 1u) != LI_STATUS_OK) {
        return builder == NULL ? LI_STATUS_INVALID_ARGUMENT : builder->status;
    }

    builder->data[builder->length++] = value;
    builder->data[builder->length] = '\0';
    return LI_STATUS_OK;
}

int32_t li_json_append(li_json_builder* builder, const char* text)
{
    if (text == NULL) {
        text = "";
    }

    uintptr_t length = (uintptr_t)strlen(text);
    if (li_json_reserve(builder, length) != LI_STATUS_OK) {
        return builder == NULL ? LI_STATUS_INVALID_ARGUMENT : builder->status;
    }

    memcpy(builder->data + builder->length, text, (size_t)length);
    builder->length += length;
    builder->data[builder->length] = '\0';
    return LI_STATUS_OK;
}

int32_t li_json_append_vformat(li_json_builder* builder, const char* format, va_list args)
{
    if (builder == NULL || format == NULL) {
        return LI_STATUS_INVALID_ARGUMENT;
    }

    va_list measure_args;
    va_copy(measure_args, args);
    int needed = vsnprintf(NULL, 0u, format, measure_args);
    va_end(measure_args);
    if (needed < 0) {
        builder->status = LI_STATUS_PARSE_ERROR;
        return builder->status;
    }

    if (li_json_reserve(builder, (uintptr_t)needed) != LI_STATUS_OK) {
        return builder->status;
    }

    int written = vsnprintf(builder->data + builder->length, (size_t)(builder->capacity - builder->length), format, args);
    if (written < 0 || written > needed) {
        builder->status = LI_STATUS_PARSE_ERROR;
        return builder->status;
    }

    builder->length += (uintptr_t)written;
    return LI_STATUS_OK;
}

int32_t li_json_append_format(li_json_builder* builder, const char* format, ...)
{
    va_list args;
    va_start(args, format);
    int32_t status = li_json_append_vformat(builder, format, args);
    va_end(args);
    return status;
}

int32_t li_json_append_escaped_len(li_json_builder* builder, const char* text, uintptr_t length)
{
    if (builder == NULL) {
        return LI_STATUS_INVALID_ARGUMENT;
    }

    if (li_json_append_char(builder, '"') != LI_STATUS_OK) {
        return builder->status;
    }

    if (text != NULL) {
        for (uintptr_t index = 0u; index < length; ++index) {
            unsigned char value = (unsigned char)text[index];
            switch (value) {
            case '"':
                li_json_append(builder, "\\\"");
                break;
            case '\\':
                li_json_append(builder, "\\\\");
                break;
            case '\b':
                li_json_append(builder, "\\b");
                break;
            case '\f':
                li_json_append(builder, "\\f");
                break;
            case '\n':
                li_json_append(builder, "\\n");
                break;
            case '\r':
                li_json_append(builder, "\\r");
                break;
            case '\t':
                li_json_append(builder, "\\t");
                break;
            default:
                if (value < 0x20u) {
                    li_json_append_format(builder, "\\u%04x", (unsigned int)value);
                }
                else {
                    li_json_append_char(builder, (char)value);
                }
                break;
            }

            if (builder->status != LI_STATUS_OK) {
                return builder->status;
            }
        }
    }

    return li_json_append_char(builder, '"');
}

int32_t li_json_append_escaped(li_json_builder* builder, const char* text)
{
    return li_json_append_escaped_len(builder, text, text == NULL ? 0u : (uintptr_t)strlen(text));
}

int32_t li_json_append_hex(li_json_builder* builder, const uint8_t* data, uintptr_t length)
{
    static const char digits[] = "0123456789abcdef";
    if (builder == NULL || (data == NULL && length != 0u)) {
        return LI_STATUS_INVALID_ARGUMENT;
    }

    if (length > (UINTPTR_MAX / 2u)) {
        builder->status = LI_STATUS_OUT_OF_MEMORY;
        return builder->status;
    }

    if (li_json_append_char(builder, '"') != LI_STATUS_OK) {
        return builder->status;
    }

    if (li_json_reserve(builder, length * 2u) != LI_STATUS_OK) {
        return builder->status;
    }

    for (uintptr_t index = 0u; index < length; ++index) {
        uint8_t value = data[index];
        builder->data[builder->length++] = digits[(value >> 4u) & 0x0Fu];
        builder->data[builder->length++] = digits[value & 0x0Fu];
    }

    builder->data[builder->length] = '\0';
    return li_json_append_char(builder, '"');
}

int32_t li_json_append_bool(li_json_builder* builder, int value)
{
    return li_json_append(builder, value ? "true" : "false");
}

uint16_t li_read_le16(const uint8_t* data)
{
    return (uint16_t)((uint16_t)data[0] | ((uint16_t)data[1] << 8u));
}

uint32_t li_read_le32(const uint8_t* data)
{
    return (uint32_t)data[0] |
        ((uint32_t)data[1] << 8u) |
        ((uint32_t)data[2] << 16u) |
        ((uint32_t)data[3] << 24u);
}

uint64_t li_read_le64(const uint8_t* data)
{
    uint64_t low = (uint64_t)li_read_le32(data);
    uint64_t high = (uint64_t)li_read_le32(data + 4u);
    return low | (high << 32u);
}

uint16_t li_read_be16(const uint8_t* data)
{
    return (uint16_t)(((uint16_t)data[0] << 8u) | (uint16_t)data[1]);
}

uint32_t li_read_be24(const uint8_t* data)
{
    return ((uint32_t)data[0] << 16u) | ((uint32_t)data[1] << 8u) | (uint32_t)data[2];
}

void li_fixed_ascii(char* destination, uintptr_t destination_size, const uint8_t* source, uintptr_t source_size)
{
    if (destination == NULL || destination_size == 0u) {
        return;
    }

    uintptr_t written = 0u;
    if (source != NULL) {
        for (uintptr_t index = 0u; index < source_size && written + 1u < destination_size; ++index) {
            uint8_t value = source[index];
            if (value == 0u) {
                break;
            }

            destination[written++] = isprint(value) ? (char)value : ' ';
        }
    }

    while (written > 0u && destination[written - 1u] == ' ') {
        --written;
    }

    destination[written] = '\0';
}

const char* li_status_to_string(int32_t status)
{
    switch (status) {
    case LI_STATUS_OK:
        return "ok";
    case LI_STATUS_INVALID_ARGUMENT:
        return "invalid_argument";
    case LI_STATUS_OUT_OF_MEMORY:
        return "out_of_memory";
    case LI_STATUS_TRUNCATED:
        return "truncated";
    case LI_STATUS_UNSUPPORTED:
        return "unsupported";
    case LI_STATUS_PARSE_ERROR:
        return "parse_error";
    default:
        return "unknown";
    }
}

uint8_t li_checksum8(const uint8_t* data, uintptr_t length)
{
    uint32_t sum = 0u;
    if (data == NULL && length != 0u) {
        return 0xFFu;
    }

    for (uintptr_t index = 0u; index < length; ++index) {
        sum = (sum + data[index]) & 0xFFu;
    }

    return (uint8_t)sum;
}

int li_checksum8_is_valid(const uint8_t* data, uintptr_t length)
{
    return li_checksum8(data, length) == 0u;
}

void li_uint128_le_to_decimal(const uint8_t* value, char* destination, uintptr_t destination_size)
{
    uint8_t digits[48];
    uintptr_t count = 1u;

    if (destination == NULL || destination_size == 0u) {
        return;
    }

    destination[0] = '\0';
    if (value == NULL) {
        return;
    }

    memset(digits, 0, sizeof(digits));
    for (int byte_index = 15; byte_index >= 0; --byte_index) {
        uint32_t carry = value[byte_index];
        for (uintptr_t digit_index = 0u; digit_index < count; ++digit_index) {
            uint32_t current = (uint32_t)digits[digit_index] * 256u + carry;
            digits[digit_index] = (uint8_t)(current % 10u);
            carry = current / 10u;
        }

        while (carry != 0u && count < LI_ARRAY_LEN(digits)) {
            digits[count++] = (uint8_t)(carry % 10u);
            carry /= 10u;
        }
    }

    uintptr_t output_index = 0u;
    for (uintptr_t digit_index = count; digit_index > 0u && output_index + 1u < destination_size; --digit_index) {
        destination[output_index++] = (char)('0' + digits[digit_index - 1u]);
    }

    if (output_index == 0u && destination_size > 1u) {
        destination[output_index++] = '0';
    }

    destination[output_index] = '\0';
}

double li_uint128_le_to_double(const uint8_t* value)
{
    if (value == NULL) {
        return 0.0;
    }

    double result = 0.0;
    for (int byte_index = 15; byte_index >= 0; --byte_index) {
        result = (result * 256.0) + (double)value[byte_index];
    }

    return result;
}
