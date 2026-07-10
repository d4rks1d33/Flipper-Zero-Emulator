cmake_minimum_required(VERSION 3.12)

project (softfloat-3 LANGUAGES C)

set(HOST_ARCH "i386" CACHE STRING "Host architecture")
set(HOST_WORD_SIZE "64" CACHE STRING "Host word size")

if(NOT SOFTFLOAT_SPECIALIZED_ARCHITECTURE)
    message(FATAL_ERROR "Specialized architecture is not set.")
endif()
set(SPECIALIZED_ARCHITECTURE ${SOFTFLOAT_SPECIALIZED_ARCHITECTURE} CACHE STRING "The architecture that Softfloat mimics.")

add_definitions(
    -fPIC
    -Werror-implicit-function-declaration
)

add_compile_definitions(
    SOFTFLOAT_ROUND_ODD
    SOFTFLOAT_FAST_DIV32TO16
    SOFTFLOAT_FAST_DIV64TO32
)

set(FAST_INT64 ON)
set(PRIVATE_INCLUDES source/${SPECIALIZED_ARCHITECTURE})

file(GLOB SOURCES source/*.c)
file(GLOB SPECIALIZED_SOURCES source/${SPECIALIZED_ARCHITECTURE}/*.c)
list(APPEND SOURCES ${SPECIALIZED_SOURCES})

# Exclude unused formats
list(FILTER SOURCES EXCLUDE REGEX ".*\/.*bf16.*\.c")
list(FILTER SOURCES EXCLUDE REGEX ".*\/.*F80.*\.c")
list(FILTER SOURCES EXCLUDE REGEX ".*\/.*F128.*\.c")
list(FILTER SOURCES EXCLUDE REGEX ".*\/.*f128.*\.c")
list(FILTER SOURCES EXCLUDE REGEX ".*\/.*BF16.*\.c")

if(HOST_ARCH STREQUAL "i386" AND HOST_WORD_SIZE STREQUAL "64")
    list(APPEND PRIVATE_INCLUDES build/Linux-x86_64-GCC)
elseif(HOST_ARCH STREQUAL "i386" AND HOST_WORD_SIZE STREQUAL "32")
    list(APPEND PRIVATE_INCLUDES build/Linux-386-GCC)
    set(FAST_INT64 OFF)
elseif(HOST_ARCH STREQUAL "aarch64" AND HOST_WORD_SIZE STREQUAL "64")
    list(APPEND PRIVATE_INCLUDES build/Linux-AARCH64-VFPv2-GCC)
elseif(HOST_ARCH STREQUAL "arm" AND HOST_WORD_SIZE STREQUAL "32")
    list(APPEND PRIVATE_INCLUDES build/Linux-ARM-VFPv2-GCC)
    set(FAST_INT64 OFF)
else()
    message(FATAL_ERROR "The ${HOST_ARCH} ${HOST_WORD_SIZE}-bit host architecture isn't supported.")
endif()

if(FAST_INT64)
    add_compile_definitions(SOFTFLOAT_FAST_INT64)

    list(FILTER SOURCES EXCLUDE REGEX ".*\/s_.*128M.*\.c")
    list(FILTER SOURCES EXCLUDE REGEX ".*\/s_.*128SigM.*\.c")
    list(FILTER SOURCES EXCLUDE REGEX ".*\/s_.*80M.*\.c")
    list(FILTER SOURCES EXCLUDE REGEX ".*\/s_.*M\.c")
else()
    list(FILTER SOURCES EXCLUDE REGEX ".*\/s_.*128[^M]?.*\.c")
    list(FILTER SOURCES EXCLUDE REGEX ".*\/s_.*Extra\.c")
endif()

add_library(
    softfloat-3 OBJECT
    ${SOURCES}
)
target_include_directories(
    softfloat-3 PRIVATE
    ${PRIVATE_INCLUDES}
)
target_include_directories(
    softfloat-3 PUBLIC
    source/include
)
