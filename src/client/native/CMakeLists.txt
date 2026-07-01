cmake_minimum_required(VERSION 4.0)
project(RemoteSupportNative LANGUAGES C CXX)

if(NOT DEFINED RS_CONTRACT_ROOT)
  message(FATAL_ERROR "RS_CONTRACT_ROOT must point to the repository root")
endif()

enable_testing()

add_executable(abi_c_test tests/abi_c_test.c)
target_include_directories(abi_c_test PRIVATE "${RS_CONTRACT_ROOT}/02-protocol/native")
set_property(TARGET abi_c_test PROPERTY C_STANDARD 11)
set_property(TARGET abi_c_test PROPERTY C_STANDARD_REQUIRED ON)

add_executable(abi_cpp_test tests/abi_cpp_test.cpp)
target_include_directories(abi_cpp_test PRIVATE "${RS_CONTRACT_ROOT}/02-protocol/native")
set_property(TARGET abi_cpp_test PROPERTY CXX_STANDARD 20)
set_property(TARGET abi_cpp_test PROPERTY CXX_STANDARD_REQUIRED ON)

if(MSVC)
  target_compile_options(abi_c_test PRIVATE /W4 /WX)
  target_compile_options(abi_cpp_test PRIVATE /W4 /WX /permissive-)
else()
  target_compile_options(abi_c_test PRIVATE -Wall -Wextra -Wpedantic -Werror)
  target_compile_options(abi_cpp_test PRIVATE -Wall -Wextra -Wpedantic -Werror)
endif()

add_test(NAME abi_c_test COMMAND abi_c_test)
add_test(NAME abi_cpp_test COMMAND abi_cpp_test)

