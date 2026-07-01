#include "native_internal.hpp"

#include <array>

namespace {
constexpr char build_id[] = "remote-support-native/0.2.0+abi1.1";

rs_status_v1 create_device(rs_runtime_t* runtime) {
  constexpr std::array<D3D_FEATURE_LEVEL, 2> levels{D3D_FEATURE_LEVEL_11_1, D3D_FEATURE_LEVEL_11_0};
  D3D_FEATURE_LEVEL selected{};
  UINT flags = D3D11_CREATE_DEVICE_BGRA_SUPPORT | D3D11_CREATE_DEVICE_VIDEO_SUPPORT;
#if defined(_DEBUG)
  flags |= D3D11_CREATE_DEVICE_DEBUG;
#endif
  HRESULT result = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, flags,
      levels.data(), static_cast<UINT>(levels.size()), D3D11_SDK_VERSION,
      &runtime->device, &selected, &runtime->context);
#if defined(_DEBUG)
  if (result == DXGI_ERROR_SDK_COMPONENT_MISSING) {
    flags &= ~D3D11_CREATE_DEVICE_DEBUG;
    result = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, flags,
        levels.data(), static_cast<UINT>(levels.size()), D3D11_SDK_VERSION,
        &runtime->device, &selected, &runtime->context);
  }
#endif
  if (FAILED(result)) {
    result = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_WARP, nullptr, flags,
        levels.data(), static_cast<UINT>(levels.size()), D3D11_SDK_VERSION,
        &runtime->device, &selected, &runtime->context);
  }
  if (FAILED(result)) {
    return RS_STATUS_DEVICE_LOST;
  }
  ComPtr<ID3D10Multithread> multithread;
  if (SUCCEEDED(runtime->device.As(&multithread))) {
    multithread->SetMultithreadProtected(TRUE);
  }
  return RS_STATUS_OK;
}
}

std::string utf8_from_wide(const wchar_t* value) {
  if (value == nullptr || *value == L'\0') return {};
  const int required = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, value, -1, nullptr, 0, nullptr, nullptr);
  if (required <= 1) return {};
  std::string result(static_cast<size_t>(required), '\0');
  WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, value, -1, result.data(), required, nullptr, nullptr);
  result.pop_back();
  return result;
}

std::wstring wide_from_utf8(rs_string_view_v1 value) {
  if (value.data == nullptr || value.length == 0) return {};
  const int required = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, value.data, static_cast<int>(value.length), nullptr, 0);
  if (required <= 0) return {};
  std::wstring result(static_cast<size_t>(required), L'\0');
  MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, value.data, static_cast<int>(value.length), result.data(), required);
  return result;
}

uint64_t monotonic_nanoseconds() {
  LARGE_INTEGER counter{};
  LARGE_INTEGER frequency{};
  QueryPerformanceCounter(&counter);
  QueryPerformanceFrequency(&frequency);
  const uint64_t seconds = static_cast<uint64_t>(counter.QuadPart / frequency.QuadPart);
  const uint64_t remainder = static_cast<uint64_t>(counter.QuadPart % frequency.QuadPart);
  return seconds * 1'000'000'000ULL + remainder * 1'000'000'000ULL / static_cast<uint64_t>(frequency.QuadPart);
}

rs_string_view_v1 string_view(const std::string& value) {
  return {value.data(), static_cast<uint32_t>(value.size())};
}

void set_last_error(rs_runtime_t* runtime, rs_status_v1 status, const char* stable_code, const std::string& detail) {
  if (runtime == nullptr) return;
  {
    std::scoped_lock lock(runtime->error_mutex);
    runtime->last_error = detail;
  }
  if (runtime->callbacks.on_error != nullptr) {
    const std::string code(stable_code);
    runtime->callbacks.on_error(runtime->callbacks.user_context, status, string_view(code));
  }
}

extern "C" {
uint32_t RS_CALL rs_native_get_abi_major(void) { return RS_NATIVE_ABI_MAJOR; }
uint32_t RS_CALL rs_native_get_abi_minor(void) { return RS_NATIVE_ABI_MINOR; }
rs_string_view_v1 RS_CALL rs_native_get_build_id(void) { return {build_id, static_cast<uint32_t>(sizeof(build_id) - 1)}; }

rs_status_v1 RS_CALL rs_runtime_create(const rs_runtime_options_v1* options, const rs_callbacks_v1* callbacks, rs_runtime_handle* out_runtime) {
  if (options == nullptr || out_runtime == nullptr ||
      !struct_has(options->struct_size, offsetof(rs_runtime_options_v1, flags), sizeof(options->flags))) {
    return RS_STATUS_INVALID_ARGUMENT;
  }
  *out_runtime = nullptr;
  if (options->requested_abi_major != RS_NATIVE_ABI_MAJOR || options->requested_abi_minor > RS_NATIVE_ABI_MINOR) {
    return RS_STATUS_NOT_SUPPORTED;
  }
  auto runtime = std::make_unique<rs_runtime_t>();
  runtime->owner_thread_id = GetCurrentThreadId();
  const HRESULT com_result = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
  runtime->com_initialized_by_runtime = SUCCEEDED(com_result);
  if (callbacks != nullptr) {
    if (!struct_has(callbacks->struct_size, offsetof(rs_callbacks_v1, on_error), sizeof(callbacks->on_error))) {
      if (runtime->com_initialized_by_runtime) CoUninitialize();
      return RS_STATUS_INVALID_ARGUMENT;
    }
    runtime->callbacks = copy_prefix(callbacks);
  }
  const rs_status_v1 status = create_device(runtime.get());
  if (status != RS_STATUS_OK) {
    if (runtime->com_initialized_by_runtime) CoUninitialize();
    return status;
  }
  if (FAILED(MFStartup(MF_VERSION, MFSTARTUP_FULL))) {
    if (runtime->com_initialized_by_runtime) CoUninitialize();
    return RS_STATUS_NOT_SUPPORTED;
  }
  runtime->media_foundation_started = true;
  *out_runtime = runtime.release();
  return RS_STATUS_OK;
}

void RS_CALL rs_runtime_destroy(rs_runtime_handle runtime) {
  if (runtime == nullptr) return;
  if (runtime->media_foundation_started) MFShutdown();
  if (runtime->com_initialized_by_runtime && runtime->owner_thread_id == GetCurrentThreadId()) CoUninitialize();
  delete runtime;
}

rs_status_v1 RS_CALL rs_runtime_get_last_error(rs_runtime_handle runtime, char* utf8_buffer, uint32_t buffer_capacity, uint32_t* out_required_length) {
  if (runtime == nullptr || out_required_length == nullptr) return RS_STATUS_INVALID_ARGUMENT;
  std::scoped_lock lock(runtime->error_mutex);
  const size_t required = runtime->last_error.size() + 1;
  if (required > UINT32_MAX) return RS_STATUS_INTERNAL_ERROR;
  *out_required_length = static_cast<uint32_t>(required);
  if (utf8_buffer == nullptr || buffer_capacity < required) return RS_STATUS_BUFFER_TOO_SMALL;
  std::memcpy(utf8_buffer, runtime->last_error.c_str(), required);
  return RS_STATUS_OK;
}
}
