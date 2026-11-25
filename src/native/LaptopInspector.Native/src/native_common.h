#pragma once

#include <cstdint>
#include <cstdlib>
#include <string>
#include <string_view>
#include <vector>

namespace li_native {

class JsonWriter final {
public:
    JsonWriter();

    const std::string& str() const noexcept;
    void raw(std::string_view value);
    void comma(bool& first);
    void begin_object();
    void end_object();
    void begin_array();
    void end_array();
    void key(std::string_view name);
    void string(std::string_view value);
    void boolean(bool value);
    void null_value();
    void signed_number(long long value);
    void unsigned_number(unsigned long long value);
    void double_number(double value);
    void hex_bytes(const void* data, std::size_t length);

private:
    std::string text_;
};

void set_last_error(std::string_view message) noexcept;
const char* get_last_error() noexcept;
char* duplicate_json(std::string_view text) noexcept;
int32_t return_json(char** output, std::string_view text) noexcept;
int32_t return_json_string(char** output, const std::string& text) noexcept;
int32_t return_error_json(char** output, std::string_view collector, std::string_view message) noexcept;
std::string unsupported_json(std::string_view collector, std::string_view reason);
std::string escape_json(std::string_view value);
std::string bytes_to_hex(const void* data, std::size_t length);

#ifdef _WIN32
std::string wide_to_utf8(std::wstring_view value);
std::string win32_error_message(unsigned long error_code);
std::string hresult_message(long result);
std::string last_win32_error_message();
void append_win32_error(JsonWriter& writer, unsigned long error_code);
#endif

}  // namespace li_native
