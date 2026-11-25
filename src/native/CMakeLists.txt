cmake_minimum_required(VERSION 3.22)
project(LaptopInspectorNative LANGUAGES C CXX)

set(CMAKE_C_STANDARD 17)
set(CMAKE_C_STANDARD_REQUIRED ON)
set(CMAKE_C_EXTENSIONS OFF)
set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

option(LI_BUILD_NATIVE_TESTS "Собирать C-тесты licore" ON)

add_subdirectory(licore)
add_subdirectory(LaptopInspector.Native)

if (LI_BUILD_NATIVE_TESTS AND EXISTS "${CMAKE_CURRENT_SOURCE_DIR}/../../tests/native_tests/CMakeLists.txt")
    enable_testing()
    add_subdirectory("${CMAKE_CURRENT_SOURCE_DIR}/../../tests/native_tests" "${CMAKE_BINARY_DIR}/native_tests")
endif()
