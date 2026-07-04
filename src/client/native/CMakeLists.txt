cmake_minimum_required(VERSION 4.0)
project(RemoteSupportNative LANGUAGES C CXX)

set(RS_ICE_BACKEND "libjuice" CACHE STRING "ICE backend: libjuice (UDP TURN) or libnice (TURN UDP/TCP/TLS)")
set_property(CACHE RS_ICE_BACKEND PROPERTY STRINGS libjuice libnice)
if(RS_ICE_BACKEND STREQUAL "libnice")
  set(USE_NICE ON CACHE BOOL "Use libnice for TURN TCP/TLS support" FORCE)
elseif(RS_ICE_BACKEND STREQUAL "libjuice")
  set(USE_NICE OFF CACHE BOOL "Use libjuice UDP ICE backend" FORCE)
else()
  message(FATAL_ERROR "RS_ICE_BACKEND must be libjuice or libnice")
endif()

if(NOT DEFINED RS_CONTRACT_ROOT)
  message(FATAL_ERROR "RS_CONTRACT_ROOT must point to the repository root")
endif()
if(NOT DEFINED RS_CPPWINRT_ROOT)
  message(FATAL_ERROR "RS_CPPWINRT_ROOT must point to generated C++/WinRT headers")
endif()
if(NOT DEFINED RS_LIBDATACHANNEL_ROOT OR NOT EXISTS "${RS_LIBDATACHANNEL_ROOT}/CMakeLists.txt")
  message(FATAL_ERROR "RS_LIBDATACHANNEL_ROOT must point to pinned libdatachannel sources")
endif()
if(NOT DEFINED RS_MBEDTLS_ROOT OR NOT EXISTS "${RS_MBEDTLS_ROOT}/CMakeLists.txt")
  message(FATAL_ERROR "RS_MBEDTLS_ROOT must point to pinned Mbed TLS sources")
endif()

set(ENABLE_PROGRAMS OFF CACHE BOOL "" FORCE)
set(ENABLE_TESTING OFF CACHE BOOL "" FORCE)
set(USE_STATIC_MBEDTLS_LIBRARY ON CACHE BOOL "" FORCE)
set(USE_SHARED_MBEDTLS_LIBRARY OFF CACHE BOOL "" FORCE)
set(MBEDTLS_USER_CONFIG_FILE
  "${CMAKE_CURRENT_SOURCE_DIR}/third_party/mbedtls_user_config.h"
  CACHE FILEPATH "Mbed TLS options required by WebRTC" FORCE)
add_subdirectory("${RS_MBEDTLS_ROOT}" "${CMAKE_BINARY_DIR}/third_party/mbedtls" EXCLUDE_FROM_ALL)
add_library(MbedTLS::MbedTLS INTERFACE IMPORTED GLOBAL)
set_property(TARGET MbedTLS::MbedTLS PROPERTY INTERFACE_LINK_LIBRARIES
  "MbedTLS::mbedtls;MbedTLS::mbedx509;MbedTLS::mbedcrypto")

set(BUILD_SHARED_LIBS OFF CACHE BOOL "" FORCE)
set(USE_MBEDTLS ON CACHE BOOL "" FORCE)
set(ENABLE_MBEDTLS OFF CACHE BOOL "Use libSRTP built-in crypto" FORCE)
set(ENABLE_WARNINGS_AS_ERRORS OFF CACHE BOOL "Third-party warnings are audited separately" FORCE)
set(NO_EXAMPLES ON CACHE BOOL "" FORCE)
set(NO_TESTS ON CACHE BOOL "" FORCE)
set(NO_MEDIA OFF CACHE BOOL "" FORCE)
set(NO_WEBSOCKET OFF CACHE BOOL "" FORCE)
set(PREFER_SYSTEM_LIB OFF CACHE BOOL "" FORCE)
add_subdirectory("${RS_LIBDATACHANNEL_ROOT}" "${CMAKE_BINARY_DIR}/third_party/libdatachannel" EXCLUDE_FROM_ALL)

enable_testing()

add_library(remote_support_native SHARED
  src/runtime.cpp
  src/display.cpp
  src/capture.cpp
  src/wgc_capture.cpp
  src/renderer.cpp
  src/gpu_color_converter.cpp
  src/media_foundation_codec.cpp
  src/transport_crypto.cpp
  src/transport_protocol.cpp
  src/transport.cpp
  src/input.cpp)
target_include_directories(remote_support_native PRIVATE
  "${CMAKE_CURRENT_SOURCE_DIR}/src"
  "${RS_CONTRACT_ROOT}/02-protocol/native")
target_include_directories(remote_support_native SYSTEM PRIVATE "${RS_CPPWINRT_ROOT}")
target_compile_definitions(remote_support_native PRIVATE RS_NATIVE_EXPORTS WIN32_LEAN_AND_MEAN NOMINMAX _WIN32_WINNT=0x0A00 NTDDI_VERSION=0x0A000000)
target_compile_definitions(remote_support_native PRIVATE $<$<CONFIG:Debug>:RS_ENABLE_TEST_FAULT_INJECTION>)
target_link_libraries(remote_support_native PRIVATE datachannel bcrypt d3d11 dxgi d2d1 shcore user32 gdi32 ole32 runtimeobject mf mfplat mfuuid wmcodecdspuuid propsys)
set_property(TARGET remote_support_native PROPERTY CXX_STANDARD 20)
set_property(TARGET remote_support_native PROPERTY CXX_STANDARD_REQUIRED ON)
set_target_properties(remote_support_native PROPERTIES PREFIX "" OUTPUT_NAME "remote_support_native")

add_executable(abi_c_test tests/abi_c_test.c)
target_include_directories(abi_c_test PRIVATE "${RS_CONTRACT_ROOT}/02-protocol/native")
set_property(TARGET abi_c_test PROPERTY C_STANDARD 11)
set_property(TARGET abi_c_test PROPERTY C_STANDARD_REQUIRED ON)

add_executable(abi_cpp_test tests/abi_cpp_test.cpp)
target_include_directories(abi_cpp_test PRIVATE "${RS_CONTRACT_ROOT}/02-protocol/native")
set_property(TARGET abi_cpp_test PROPERTY CXX_STANDARD 20)
set_property(TARGET abi_cpp_test PROPERTY CXX_STANDARD_REQUIRED ON)

add_executable(native_runtime_test tests/native_runtime_test.cpp)
target_include_directories(native_runtime_test PRIVATE "${RS_CONTRACT_ROOT}/02-protocol/native")
target_link_libraries(native_runtime_test PRIVATE remote_support_native)
set_property(TARGET native_runtime_test PROPERTY CXX_STANDARD 20)
set_property(TARGET native_runtime_test PROPERTY CXX_STANDARD_REQUIRED ON)

add_executable(dxgi_capture_test tests/dxgi_capture_test.cpp)
target_include_directories(dxgi_capture_test PRIVATE "${RS_CONTRACT_ROOT}/02-protocol/native")
target_link_libraries(dxgi_capture_test PRIVATE remote_support_native)
set_property(TARGET dxgi_capture_test PROPERTY CXX_STANDARD 20)
set_property(TARGET dxgi_capture_test PROPERTY CXX_STANDARD_REQUIRED ON)

add_executable(wgc_capture_test tests/wgc_capture_test.cpp)
target_include_directories(wgc_capture_test PRIVATE "${RS_CONTRACT_ROOT}/02-protocol/native")
target_link_libraries(wgc_capture_test PRIVATE remote_support_native)
set_property(TARGET wgc_capture_test PROPERTY CXX_STANDARD 20)
set_property(TARGET wgc_capture_test PROPERTY CXX_STANDARD_REQUIRED ON)

add_executable(renderer_smoke_test tests/renderer_smoke_test.cpp)
target_include_directories(renderer_smoke_test PRIVATE "${RS_CONTRACT_ROOT}/02-protocol/native")
target_link_libraries(renderer_smoke_test PRIVATE remote_support_native user32)
set_property(TARGET renderer_smoke_test PROPERTY CXX_STANDARD 20)
set_property(TARGET renderer_smoke_test PROPERTY CXX_STANDARD_REQUIRED ON)

add_executable(capture_benchmark tests/capture_benchmark.cpp)
target_include_directories(capture_benchmark PRIVATE "${RS_CONTRACT_ROOT}/02-protocol/native")
target_link_libraries(capture_benchmark PRIVATE remote_support_native psapi)
set_property(TARGET capture_benchmark PROPERTY CXX_STANDARD 20)
set_property(TARGET capture_benchmark PROPERTY CXX_STANDARD_REQUIRED ON)

add_executable(codec_roundtrip_test tests/codec_roundtrip_test.cpp)
target_include_directories(codec_roundtrip_test PRIVATE "${RS_CONTRACT_ROOT}/02-protocol/native")
target_link_libraries(codec_roundtrip_test PRIVATE remote_support_native)
target_compile_definitions(codec_roundtrip_test PRIVATE $<$<CONFIG:Debug>:RS_ENABLE_TEST_FAULT_INJECTION>)
set_property(TARGET codec_roundtrip_test PROPERTY CXX_STANDARD 20)
set_property(TARGET codec_roundtrip_test PROPERTY CXX_STANDARD_REQUIRED ON)

add_executable(codec_benchmark tests/codec_benchmark.cpp)
target_include_directories(codec_benchmark PRIVATE "${RS_CONTRACT_ROOT}/02-protocol/native")
target_link_libraries(codec_benchmark PRIVATE remote_support_native psapi)
set_property(TARGET codec_benchmark PROPERTY CXX_STANDARD 20)
set_property(TARGET codec_benchmark PROPERTY CXX_STANDARD_REQUIRED ON)

add_executable(codec_capability_probe tests/codec_capability_probe.cpp)
target_include_directories(codec_capability_probe PRIVATE "${RS_CONTRACT_ROOT}/02-protocol/native")
target_link_libraries(codec_capability_probe PRIVATE remote_support_native)
set_property(TARGET codec_capability_probe PROPERTY CXX_STANDARD 20)
set_property(TARGET codec_capability_probe PROPERTY CXX_STANDARD_REQUIRED ON)

add_executable(hardware_encoder_test tests/hardware_encoder_test.cpp)
target_include_directories(hardware_encoder_test PRIVATE "${RS_CONTRACT_ROOT}/02-protocol/native")
target_link_libraries(hardware_encoder_test PRIVATE remote_support_native)
set_property(TARGET hardware_encoder_test PROPERTY CXX_STANDARD 20)
set_property(TARGET hardware_encoder_test PROPERTY CXX_STANDARD_REQUIRED ON)

add_executable(transport_integration_test tests/transport_integration_test.cpp)
target_include_directories(transport_integration_test PRIVATE "${RS_CONTRACT_ROOT}/02-protocol/native")
target_link_libraries(transport_integration_test PRIVATE remote_support_native)
set_property(TARGET transport_integration_test PROPERTY CXX_STANDARD 20)
set_property(TARGET transport_integration_test PROPERTY CXX_STANDARD_REQUIRED ON)

add_executable(input_mapping_test tests/input_mapping_test.cpp)
target_include_directories(input_mapping_test PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}/src")
set_property(TARGET input_mapping_test PROPERTY CXX_STANDARD 20)
set_property(TARGET input_mapping_test PROPERTY CXX_STANDARD_REQUIRED ON)

add_executable(input_integration_test tests/input_integration_test.cpp)
target_include_directories(input_integration_test PRIVATE "${RS_CONTRACT_ROOT}/02-protocol/native")
target_link_libraries(input_integration_test PRIVATE remote_support_native)
set_property(TARGET input_integration_test PROPERTY CXX_STANDARD 20)
set_property(TARGET input_integration_test PROPERTY CXX_STANDARD_REQUIRED ON)

if(MSVC)
  target_compile_options(abi_c_test PRIVATE /W4 /WX)
  target_compile_options(abi_cpp_test PRIVATE /W4 /WX /permissive-)
  target_compile_options(remote_support_native PRIVATE /W4 /WX /permissive- /EHsc)
  target_compile_options(native_runtime_test PRIVATE /W4 /WX /permissive- /EHsc)
  target_compile_options(dxgi_capture_test PRIVATE /W4 /WX /permissive- /EHsc)
  target_compile_options(wgc_capture_test PRIVATE /W4 /WX /permissive- /EHsc)
  target_compile_options(renderer_smoke_test PRIVATE /W4 /WX /permissive- /EHsc)
  target_compile_options(capture_benchmark PRIVATE /W4 /WX /permissive- /EHsc)
  target_compile_options(codec_roundtrip_test PRIVATE /W4 /WX /permissive- /EHsc)
  target_compile_options(codec_benchmark PRIVATE /W4 /WX /permissive- /EHsc)
  target_compile_options(codec_capability_probe PRIVATE /W4 /WX /permissive- /EHsc)
  target_compile_options(hardware_encoder_test PRIVATE /W4 /WX /permissive- /EHsc)
  target_compile_options(transport_integration_test PRIVATE /W4 /WX /permissive- /EHsc)
  target_compile_options(input_mapping_test PRIVATE /W4 /WX /permissive- /EHsc)
  target_compile_options(input_integration_test PRIVATE /W4 /WX /permissive- /EHsc)
else()
  target_compile_options(abi_c_test PRIVATE -Wall -Wextra -Wpedantic -Werror)
  target_compile_options(abi_cpp_test PRIVATE -Wall -Wextra -Wpedantic -Werror)
  target_compile_options(remote_support_native PRIVATE -Wall -Wextra -Wpedantic -Werror)
  target_compile_options(native_runtime_test PRIVATE -Wall -Wextra -Wpedantic -Werror)
  target_compile_options(dxgi_capture_test PRIVATE -Wall -Wextra -Wpedantic -Werror)
  target_compile_options(wgc_capture_test PRIVATE -Wall -Wextra -Wpedantic -Werror)
  target_compile_options(renderer_smoke_test PRIVATE -Wall -Wextra -Wpedantic -Werror)
  target_compile_options(capture_benchmark PRIVATE -Wall -Wextra -Wpedantic -Werror)
  target_compile_options(codec_roundtrip_test PRIVATE -Wall -Wextra -Wpedantic -Werror)
  target_compile_options(codec_benchmark PRIVATE -Wall -Wextra -Wpedantic -Werror)
  target_compile_options(codec_capability_probe PRIVATE -Wall -Wextra -Wpedantic -Werror)
  target_compile_options(hardware_encoder_test PRIVATE -Wall -Wextra -Wpedantic -Werror)
  target_compile_options(transport_integration_test PRIVATE -Wall -Wextra -Wpedantic -Werror)
  target_compile_options(input_mapping_test PRIVATE -Wall -Wextra -Wpedantic -Werror)
  target_compile_options(input_integration_test PRIVATE -Wall -Wextra -Wpedantic -Werror)
endif()

add_test(NAME AT-NFR-MNT-002-abi-c COMMAND abi_c_test)
add_test(NAME AT-NFR-MNT-002-abi-cpp COMMAND abi_cpp_test)
add_test(NAME AT-FR-SCR-002-synthetic-recovery COMMAND native_runtime_test)
add_test(NAME AT-FR-SCR-003-local-render COMMAND renderer_smoke_test)
add_test(NAME AT-FR-SCR-005-codec-roundtrip COMMAND codec_roundtrip_test)
add_test(NAME AT-FR-SCR-005-codec-capabilities COMMAND codec_capability_probe)
add_test(NAME AT-FR-SCR-005-codec-benchmark COMMAND codec_benchmark "${CMAKE_CURRENT_BINARY_DIR}/goal-03-codec-benchmark.json")
add_test(NAME AT-FR-NET-001-lan-webrtc COMMAND transport_integration_test)
add_test(NAME AT-SEC-TRN-001-binding-authorization-negative COMMAND transport_integration_test authorization-mismatch)
add_test(NAME AT-SEC-TRN-001-binding-scope-negative COMMAND transport_integration_test scope-mismatch)
add_test(NAME AT-SEC-TRN-001-binding-epoch-negative COMMAND transport_integration_test epoch-mismatch)
if(CMAKE_BUILD_TYPE STREQUAL "Debug")
  add_test(NAME AT-SEC-TRN-001-binding-fingerprint-negative COMMAND transport_integration_test fingerprint-mismatch)
  add_test(NAME AT-SEC-TRN-001-binding-replay-negative COMMAND transport_integration_test binding-replay)
  add_test(NAME AT-FR-NET-001-protocol-version-negative COMMAND transport_integration_test protocol-version)
  add_test(NAME AT-FR-INP-001-local-scope-and-lifecycle COMMAND input_integration_test)
endif()
add_test(NAME AT-FR-INP-002-coordinate-normalization COMMAND input_mapping_test)

# The codec encode tests use synthetic capture + software fallback so they run in CI,
# but they still require D3D11 video color conversion. Hosts without it (headless,
# GPU-less CI runners on a WARP device) make rs_encoder_create return NOT_SUPPORTED;
# the tests exit 125 there and CTest reports them Skipped instead of Failed. On any
# video-capable machine the encoder is created and the tests run in full.
set_tests_properties(AT-FR-SCR-005-codec-roundtrip AT-FR-SCR-005-codec-benchmark
    PROPERTIES SKIP_RETURN_CODE 125)
