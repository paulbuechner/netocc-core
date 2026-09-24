cmake_minimum_required(VERSION 3.24)

# NetOccNative: SWIG C# wrappers (build/generated/cxx) linked against OCCT from vcpkg.
# Driven by build.py:  python build.py native --triplet x64-windows
project(netocc-core LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

set(NETOCC_GENERATED_DIR "${CMAKE_SOURCE_DIR}/build/generated/cxx" CACHE PATH "SWIG output (C++ wrappers)")
set(NETOCC_RID "" CACHE STRING ".NET runtime identifier of this build (win-x64, linux-x64, ...)")
set(NETOCC_OCCT_RUNTIME_DIR "" CACHE PATH "Directory with the OCCT + third-party shared libraries to stage")

find_package(OpenCASCADE 8.0.1 EXACT CONFIG REQUIRED)
message(STATUS "OCCT ${OpenCASCADE_VERSION}: ${OpenCASCADE_INSTALL_PREFIX}")

file(GLOB NETOCC_WRAP_SOURCES CONFIGURE_DEPENDS "${NETOCC_GENERATED_DIR}/*_wrap.cxx")
if(NOT NETOCC_WRAP_SOURCES)
  message(FATAL_ERROR "No SWIG output in ${NETOCC_GENERATED_DIR}; run: python build.py generate")
endif()

# No dot in the name: the Windows loader would treat ".Native" as the extension.
add_library(NetOccNative SHARED ${NETOCC_WRAP_SOURCES})

target_include_directories(NetOccNative PRIVATE
  ${OpenCASCADE_INCLUDE_DIR}
  ${CMAKE_SOURCE_DIR}/src/SWIG_files/headers)

target_link_libraries(NetOccNative PRIVATE
  ${OpenCASCADE_FoundationClasses_LIBRARIES}
  ${OpenCASCADE_ModelingData_LIBRARIES}
  ${OpenCASCADE_ModelingAlgorithms_LIBRARIES}
  ${OpenCASCADE_ApplicationFramework_LIBRARIES}
  ${OpenCASCADE_DataExchange_LIBRARIES})

if(MSVC)
  # /bigobj: SWIG wrappers exceed the default section limit
  target_compile_options(NetOccNative PRIVATE /bigobj /EHsc /permissive- /W3)
else()
  target_compile_options(NetOccNative PRIVATE -fvisibility=hidden)
  if(APPLE)
    set_target_properties(NetOccNative PROPERTIES BUILD_RPATH "@loader_path" INSTALL_RPATH "@loader_path")
  else()
    set_target_properties(NetOccNative PROPERTIES BUILD_RPATH "$ORIGIN" INSTALL_RPATH "$ORIGIN")
  endif()
endif()

# Stage layout = runtimes/<rid>/native: the shim plus every OCCT/third-party shared library.
install(TARGETS NetOccNative RUNTIME DESTINATION . LIBRARY DESTINATION .)
if(NETOCC_OCCT_RUNTIME_DIR)
  if(WIN32)
    install(DIRECTORY "${NETOCC_OCCT_RUNTIME_DIR}/" DESTINATION . FILES_MATCHING PATTERN "*.dll")
  elseif(APPLE)
    install(DIRECTORY "${NETOCC_OCCT_RUNTIME_DIR}/" DESTINATION . FILES_MATCHING PATTERN "*.dylib")
  else()
    install(DIRECTORY "${NETOCC_OCCT_RUNTIME_DIR}/" DESTINATION . FILES_MATCHING PATTERN "*.so*")
  endif()
endif()
