add_library(licore STATIC
    src/li_core.c
    src/li_hash.c
    src/li_smbios.c
    src/li_edid.c
    src/li_acpi.c
    src/li_cpuid.c
    src/li_storage.c
)

target_include_directories(licore
    PUBLIC
        ${CMAKE_CURRENT_SOURCE_DIR}/include
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/src
)

if (MSVC)
    target_compile_options(licore PRIVATE /W4 /WX /sdl)
else()
    target_compile_options(licore PRIVATE -Wall -Wextra -Wpedantic -Werror)
endif()
