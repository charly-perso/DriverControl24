cmake_minimum_required(VERSION 3.20)
project(reaper_c24bridge CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_OSX_ARCHITECTURES "arm64" CACHE STRING "")

file(GLOB SOURCES ${CMAKE_SOURCE_DIR}/*.cpp)

add_library(reaper_c24bridge SHARED ${SOURCES})
set_target_properties(reaper_c24bridge PROPERTIES
    PREFIX ""
    SUFFIX ".dylib"
    CXX_VISIBILITY_PRESET hidden
)