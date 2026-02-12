cmake_minimum_required(VERSION 3.14)
project(TreadmillOpenXRLayer LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Static link the C/C++ runtime so the DLL has zero dependencies
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

add_library(treadmill_layer SHARED treadmill_layer.cpp)

# Export the negotiation entry point
target_compile_definitions(treadmill_layer PRIVATE WIN32_LEAN_AND_MEAN)

# No external libraries needed — only Windows API (kernel32)
target_link_libraries(treadmill_layer PRIVATE kernel32)

# Output name without "lib" prefix
set_target_properties(treadmill_layer PROPERTIES
    PREFIX ""
    OUTPUT_NAME "treadmill_layer"
    RUNTIME_OUTPUT_DIRECTORY "${CMAKE_SOURCE_DIR}/bin"
    LIBRARY_OUTPUT_DIRECTORY "${CMAKE_SOURCE_DIR}/bin"
)

# Generate a .def file to export the negotiation function
set_target_properties(treadmill_layer PROPERTIES
    LINK_FLAGS "/DEF:\"${CMAKE_CURRENT_SOURCE_DIR}/treadmill_layer.def\""
)
