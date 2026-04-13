cmake_minimum_required(VERSION 3.21)

project(console2lce_lzx_native LANGUAGES C)

add_library(console2lce_lzx_native SHARED
    src/console2lce_lzx_native.c
    third_party/libmspack/lzxd.c
)

target_include_directories(console2lce_lzx_native PRIVATE
    third_party/libmspack
)

target_compile_definitions(console2lce_lzx_native PRIVATE
    HAVE_CONFIG_H=1
    HAVE_INTTYPES_H=1
    SIZEOF_OFF_T=8
)

if (MSVC)
    target_compile_options(console2lce_lzx_native PRIVATE /W4)
endif()
