cmake_minimum_required(VERSION 3.25)
project(LiveDLSS.Dlss VERSION 0.1.0 LANGUAGES CXX RC)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

set(STREAMLINE_SDK_DIR "$ENV{STREAMLINE_SDK_DIR}" CACHE PATH "NVIDIA Streamline SDK (include/, lib/x64/, bin/x64/)")
if(NOT EXISTS "${STREAMLINE_SDK_DIR}/include/sl.h")
  message(FATAL_ERROR "STREAMLINE_SDK_DIR is missing or invalid: '${STREAMLINE_SDK_DIR}'. "
                      "Download the SDK: https://github.com/NVIDIA-RTX/Streamline/releases")
endif()

add_library(LiveDLSS.Dlss SHARED src/ldls_dlss.cpp)
set(BRIDGE_FILENAME "LiveDLSS.Dlss.dll")
configure_file(version.rc.in "${CMAKE_CURRENT_BINARY_DIR}/version.rc" @ONLY)
target_sources(LiveDLSS.Dlss PRIVATE "${CMAKE_CURRENT_BINARY_DIR}/version.rc")
target_compile_definitions(LiveDLSS.Dlss PRIVATE LIVEDLSS_VERSION="${PROJECT_VERSION}")
target_include_directories(LiveDLSS.Dlss
  PUBLIC include
  PRIVATE "${STREAMLINE_SDK_DIR}/include")
target_link_libraries(LiveDLSS.Dlss PRIVATE "${STREAMLINE_SDK_DIR}/lib/x64/sl.interposer.lib" d3d12)
target_compile_definitions(LiveDLSS.Dlss PRIVATE NOMINMAX WIN32_LEAN_AND_MEAN)
if(MSVC)
  target_compile_options(LiveDLSS.Dlss PRIVATE /W4 /permissive- /utf-8)
endif()

set(LIVEDLSS_RUNTIME_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../../runtimes/dlss" CACHE PATH "Install directory for LiveDLSS.Dlss.dll and Streamline DLLs")
install(TARGETS LiveDLSS.Dlss RUNTIME DESTINATION "${LIVEDLSS_RUNTIME_DIR}")
file(GLOB STREAMLINE_RUNTIME_DLLS "${STREAMLINE_SDK_DIR}/bin/x64/*.dll")
install(FILES ${STREAMLINE_RUNTIME_DLLS} DESTINATION "${LIVEDLSS_RUNTIME_DIR}")
