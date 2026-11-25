#pragma once

#include "licore/li_core.h"

#include <stdarg.h>
#include <stddef.h>
#include <stdint.h>

#define LI_ARRAY_LEN(value) (sizeof(value) / sizeof((value)[0]))

typedef struct li_json_builder {
    char* data;
    uintptr_t length;
    uintptr_t capacity;
    int32_t status;
} li_json_builder;

void li_json_init(li_json_builder* builder);
void li_json_destroy(li_json_builder* builder);
int32_t li_json_detach(li_json_builder* builder, li_json_output* output);
int32_t li_json_append_char(li_json_builder* builder, char value);
int32_t li_json_append(li_json_builder* builder, const char* text);
int32_t li_json_append_format(li_json_builder* builder, const char* format, ...);
int32_t li_json_append_vformat(li_json_builder* builder, const char* format, va_list args);
int32_t li_json_append_escaped(li_json_builder* builder, const char* text);
int32_t li_json_append_escaped_len(li_json_builder* builder, const char* text, uintptr_t length);
int32_t li_json_append_hex(li_json_builder* builder, const uint8_t* data, uintptr_t length);
int32_t li_json_append_bool(li_json_builder* builder, int value);

uint16_t li_read_le16(const uint8_t* data);
uint32_t li_read_le32(const uint8_t* data);
uint64_t li_read_le64(const uint8_t* data);
uint16_t li_read_be16(const uint8_t* data);
uint32_t li_read_be24(const uint8_t* data);
void li_fixed_ascii(char* destination, uintptr_t destination_size, const uint8_t* source, uintptr_t source_size);
const char* li_status_to_string(int32_t status);

int li_checksum8_is_valid(const uint8_t* data, uintptr_t length);
uint8_t li_checksum8(const uint8_t* data, uintptr_t length);

void li_uint128_le_to_decimal(const uint8_t* value, char* destination, uintptr_t destination_size);
double li_uint128_le_to_double(const uint8_t* value);
