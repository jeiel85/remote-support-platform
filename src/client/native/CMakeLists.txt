cmake_minimum_required(VERSION 4.0)
project(RemoteSupportNative LANGUAGES C CXX)

if(NOT DEFINED RS_CONTRACT_ROOT)
  message(FATAL_ERROR "RS_CONTRACT_ROOT must point to the repository root")
endif()
if(NOT DEFINED RS_CPPWINRT_ROOT)
  message(FATAL_ERROR "RS_CPPWINRT_ROOT must point to generated C++/WinRT headers")
endif()

enable_testing()

add_library(remote_support_native SHARED
  src/runtime.cpp
  src/display.cpp
  src/capture.cpp
  src/wgc_capture.cpp
  src/renderer.cpp
  src/phased_exports.cpp)
target_include_directories(remote_support_native PRIVATE
  "${CMAKE_CURRENT_SOURCE_DIR}/src"
  "${RS_CONTRACT_ROOT}/02-protocol/native")
target_include_directories(remote_support_native SYSTEM PRIVATE "${RS_CPPWINRT_ROOT}")
target_compile_definitions(remote_support_native PRIVATE RS_NATIVE_EXPORTS WIN32_LEAN_AND_MEAN NOMINMAX _WIN32_WINNT=0x0A00 NTDDI_VERSION=0x0A000000)
target_link_libraries(remote_support_native PRIVATE d3d11 dxgi d2d1 shcore user32 gdi32 ole32 runtimeobject)
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

if(MSVC)
  target_compile_options(abi_c_test PRIVATE /W4 /WX)
  target_compile_options(abi_cpp_test PRIVATE /W4 /WX /permissive-)
  target_compile_options(remote_support_native PRIVATE /W4 /WX /permissive- /EHsc)
  target_compile_options(native_runtime_test PRIVATE /W4 /WX /permissive- /EHsc)
  target_compile_options(dxgi_capture_test PRIVATE /W4 /WX /permissive- /EHsc)
  target_compile_options(wgc_capture_test PRIVATE /W4 /WX /permissive- /EHsc)
  target_compile_options(renderer_smoke_test PRIVATE /W4 /WX /permissive- /EHsc)
  target_compile_options(capture_benchmark PRIVATE /W4 /WX /permissive- /EHsc)
else()
  target_compile_options(abi_c_test PRIVATE -Wall -Wextra -Wpedantic -Werror)
  target_compile_options(abi_cpp_test PRIVATE -Wall -Wextra -Wpedantic -Werror)
  target_compile_options(remote_support_native PRIVATE -Wall -Wextra -Wpedantic -Werror)
  target_compile_options(native_runtime_test PRIVATE -Wall -Wextra -Wpedantic -Werror)
  target_compile_options(dxgi_capture_test PRIVATE -Wall -Wextra -Wpedantic -Werror)
  target_compile_options(wgc_capture_test PRIVATE -Wall -Wextra -Wpedantic -Werror)
  target_compile_options(renderer_smoke_test PRIVATE -Wall -Wextra -Wpedantic -Werror)
  target_compile_options(capture_benchmark PRIVATE -Wall -Wextra -Wpedantic -Werror)
endif()

add_test(NAME AT-NFR-MNT-002-abi-c COMMAND abi_c_test)
add_test(NAME AT-NFR-MNT-002-abi-cpp COMMAND abi_cpp_test)
add_test(NAME AT-FR-SCR-002-synthetic-recovery COMMAND native_runtime_test)
add_test(NAME AT-FR-SCR-003-local-render COMMAND renderer_smoke_test)
