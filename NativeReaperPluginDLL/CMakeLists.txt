cmake_minimum_required(VERSION 3.20)
project(reaper_c24events CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_OSX_ARCHITECTURES "x86_64" CACHE STRING "")

add_library(reaper_c24events SHARED dllmain.cpp)

target_include_directories(reaper_c24events PRIVATE
    ${CMAKE_SOURCE_DIR}
    ${CMAKE_SOURCE_DIR}/../reaper-sdk/sdk
    ${CMAKE_SOURCE_DIR}/../WDL)

target_compile_definitions(reaper_c24events PRIVATE SWELL_PROVIDED_BY_APP)

set_target_properties(reaper_c24events PROPERTIES
    PREFIX ""
    SUFFIX ".dylib"
    CXX_VISIBILITY_PRESET hidden)