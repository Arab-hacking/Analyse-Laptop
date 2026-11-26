add_library(LaptopInspector.Native SHARED
    src/native_common.cpp
    src/li_native.cpp
    src/li_native_device.cpp
    src/li_native_storage.cpp
    src/li_native_battery.cpp
    src/li_native_usb.cpp
    src/li_native_display.cpp
    src/li_native_wlan.cpp
    src/li_native_firmware.cpp
    src/li_native_tpm_sensor.cpp
    src/li_native_gpu_vendor.cpp
)

target_include_directories(LaptopInspector.Native
    PUBLIC
        ${CMAKE_CURRENT_SOURCE_DIR}/include
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/src
)

target_link_libraries(LaptopInspector.Native PRIVATE licore)

if (WIN32)
    target_link_libraries(LaptopInspector.Native PRIVATE
        setupapi
        cfgmgr32
        dxgi
        wlanapi
        user32
        ole32
        advapi32
        uuid
    )
endif()

set_target_properties(LaptopInspector.Native PROPERTIES
    OUTPUT_NAME "LaptopInspector.Native"
    PREFIX ""
)

if (MSVC)
    target_compile_options(LaptopInspector.Native PRIVATE /W4 /WX /permissive- /sdl /guard:cf /Qspectre)
    target_link_options(LaptopInspector.Native PRIVATE /guard:cf)
else()
    target_compile_options(LaptopInspector.Native PRIVATE -Wall -Wextra -Wpedantic -Werror)
endif()
