#include "native_common.h"

#include <algorithm>
#include <cstdio>
#include <cstring>
#include <new>

#ifdef _WIN32
#define NOMINMAX
#include <windows.h>
#endif

namespace li_native {
namespace {
thread_local std::string g_last_error;

void append_escaped_into(std::string& target, std::string_view value)
{
    target.push_back('"');
    for (unsigned char character : value) {
        switch (character) {
        case '"':
            target += "\\\"";
            break;
        case '\\':
            target += "\\\\";
            break;
        case '\b':
            target += "\\b";
            break;
        case '\f':
            target += "\\f";
            break;
        case '\n':
            target += "\\n";
            break;
        case '\r':
            target += "\\r";
            break;
        case '\t':
            target += "\\t";
            break;
        default:
            if (character < 0x20u) {
                char buffer[7];
                std::snprintf(buffer, sizeof(buffer), "\\u%04x", static_cast<unsigned int>(character));
                target += buffer;
            }
            else {
                target.push_back(static_cast<char>(character));
            }
            break;
        }
    }

    target.push_back('"');
}
}  // namespace

JsonWriter::JsonWriter()
{
    text_.reserve(4096u);
}

const std::string& JsonWriter::str() const noexcept
{
    return text_;
}

void JsonWriter::raw(std::string_view value)
{
    text_.append(value.data(), value.size());
}

void JsonWriter::comma(bool& first)
{
    if (!first) {
        text_.push_back(',');
    }

    first = false;
}

void JsonWriter::begin_object()
{
    text_.push_back('{');
}

void JsonWriter::end_object()
{
    text_.push_back('}');
}

void JsonWriter::begin_array()
{
    text_.push_back('[');
}

void JsonWriter::end_array()
{
    text_.push_back(']');
}

void JsonWriter::key(std::string_view name)
{
    string(name);
    text_.push_back(':');
}

void JsonWriter::string(std::string_view value)
{
    append_escaped_into(text_, value);
}

void JsonWriter::boolean(bool value)
{
    raw(value ? "true" : "false");
}

void JsonWriter::null_value()
{
    raw("null");
}

void JsonWriter::signed_number(long long value)
{
    text_ += std::to_string(value);
}

void JsonWriter::unsigned_number(unsigned long long value)
{
    text_ += std::to_string(value);
}

void JsonWriter::double_number(double value)
{
    char buffer[64];
    std::snprintf(buffer, sizeof(buffer), "%.3f", value);
    text_ += buffer;
}

void JsonWriter::hex_bytes(const void* data, std::size_t length)
{
    string(bytes_to_hex(data, length));
}

void set_last_error(std::string_view message) noexcept
{
    try {
        g_last_error.assign(message.data(), message.size());
    }
    catch (...) {
    }
}

const char* get_last_error() noexcept
{
    return g_last_error.c_str();
}

char* duplicate_json(std::string_view text) noexcept
{
    const std::size_t size = text.size() + 1u;
    void* memory = std::malloc(size);
    if (memory == nullptr) {
        set_last_error("Недостаточно памяти для JSON-буфера.");
        return nullptr;
    }

    std::memcpy(memory, text.data(), text.size());
    static_cast<char*>(memory)[text.size()] = '\0';
    return static_cast<char*>(memory);
}

int32_t return_json(char** output, std::string_view text) noexcept
{
    if (output == nullptr) {
        set_last_error("Аргумент output равен null.");
        return -1;
    }

    *output = duplicate_json(text);
    return *output == nullptr ? -2 : 0;
}

int32_t return_json_string(char** output, const std::string& text) noexcept
{
    return return_json(output, std::string_view(text.data(), text.size()));
}

int32_t return_error_json(char** output, std::string_view collector, std::string_view message) noexcept
{
    try {
        set_last_error(message);
        JsonWriter writer;
        writer.begin_object();
        writer.key("collector");
        writer.string(collector);
        writer.raw(",\"status\":\"failed\",\"error\":");
        writer.string(message);
        writer.end_object();
        return return_json_string(output, writer.str());
    }
    catch (...) {
        set_last_error("Не удалось сформировать JSON ошибки.");
        return -3;
    }
}

std::string unsupported_json(std::string_view collector, std::string_view reason)
{
    JsonWriter writer;
    writer.begin_object();
    writer.key("collector");
    writer.string(collector);
    writer.raw(",\"status\":\"unsupported\",\"reason\":");
    writer.string(reason);
    writer.end_object();
    return writer.str();
}

std::string escape_json(std::string_view value)
{
    std::string result;
    result.reserve(value.size() + 2u);
    append_escaped_into(result, value);
    return result;
}

std::string bytes_to_hex(const void* data, std::size_t length)
{
    static constexpr char digits[] = "0123456789abcdef";
    std::string result;
    result.resize(length * 2u);
    const auto* bytes = static_cast<const unsigned char*>(data);
    if (bytes == nullptr && length != 0u) {
        return {};
    }

    for (std::size_t index = 0u; index < length; ++index) {
        result[index * 2u] = digits[(bytes[index] >> 4u) & 0x0Fu];
        result[(index * 2u) + 1u] = digits[bytes[index] & 0x0Fu];
    }

    return result;
}

#ifdef _WIN32
std::string wide_to_utf8(std::wstring_view value)
{
    if (value.empty()) {
        return {};
    }

    int required = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, value.data(), static_cast<int>(value.size()), nullptr, 0, nullptr, nullptr);
    if (required <= 0) {
        required = WideCharToMultiByte(CP_UTF8, 0, value.data(), static_cast<int>(value.size()), nullptr, 0, nullptr, nullptr);
    }

    if (required <= 0) {
        return {};
    }

    std::string result(static_cast<std::size_t>(required), '\0');
    WideCharToMultiByte(CP_UTF8, 0, value.data(), static_cast<int>(value.size()), result.data(), required, nullptr, nullptr);
    return result;
}

std::string win32_error_message(unsigned long error_code)
{
    wchar_t* buffer = nullptr;
    DWORD length = FormatMessageW(
        FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS,
        nullptr,
        error_code,
        0,
        reinterpret_cast<LPWSTR>(&buffer),
        0,
        nullptr);
    if (length == 0 || buffer == nullptr) {
        return "Win32 error " + std::to_string(error_code);
    }

    std::wstring message(buffer, buffer + length);
    LocalFree(buffer);
    while (!message.empty() && (message.back() == L'\r' || message.back() == L'\n' || message.back() == L' ' || message.back() == L'.')) {
        message.pop_back();
    }

    return wide_to_utf8(message);
}

std::string hresult_message(long result)
{
    return win32_error_message(static_cast<unsigned long>(result));
}

std::string last_win32_error_message()
{
    return win32_error_message(GetLastError());
}

void append_win32_error(JsonWriter& writer, unsigned long error_code)
{
    writer.begin_object();
    writer.key("code");
    writer.unsigned_number(error_code);
    writer.raw(",\"message\":");
    writer.string(win32_error_message(error_code));
    writer.end_object();
}
#endif

}  // namespace li_native
