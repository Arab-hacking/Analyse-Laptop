add_executable(licore_native_tests
    native_tests.c
)

target_link_libraries(licore_native_tests PRIVATE licore)

target_include_directories(licore_native_tests PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}/../../src/native/licore/include
)

target_compile_definitions(licore_native_tests PRIVATE
    LI_FIXTURE_DIR="${CMAKE_CURRENT_SOURCE_DIR}/../fixtures"
)

if (MSVC)
    target_compile_options(licore_native_tests PRIVATE /W4 /WX /sdl)
else()
    target_compile_options(licore_native_tests PRIVATE -Wall -Wextra -Wpedantic -Werror)
endif()

add_test(NAME licore_native_tests COMMAND licore_native_tests)
