#pragma once

#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#include <d3d10.h>
#include <d3d11.h>
#include <d2d1_1.h>
#include <dxgi1_2.h>
#include <shellscalingapi.h>
#include <wrl/client.h>

#include "remote_support_native.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <mutex>
#include <optional>
#include <string>
#include <thread>
#include <utility>
#include <vector>

using Microsoft::WRL::ComPtr;

struct display_record {
  std::string id;
  std::string name;
  std::wstring gdi_name;
  HMONITOR monitor{};
  RECT bounds{};
  LUID adapter_luid{};
  uint32_t rotation_degrees{};
  uint32_t dpi_x{96};
  uint32_t dpi_y{96};
  uint32_t flags{};
};

struct rs_runtime_t {
  ComPtr<ID3D11Device> device;
  ComPtr<ID3D11DeviceContext> context;
  rs_callbacks_v1 callbacks{};
  std::mutex error_mutex;
  std::string last_error;
  std::mutex topology_mutex;
  std::string topology_signature;
  std::atomic<uint64_t> topology_generation{1};
};

struct rs_capture_t {
  rs_runtime_t* runtime{};
  rs_capture_options_v1 options{};
  std::string target_id;
  std::mutex state_mutex;
  std::jthread worker;
  bool running{};
  uint64_t next_frame_id{1};
  uint64_t cursor_shape_id{};
};

struct cached_cursor {
  rs_cursor_info_v1 info{};
  std::vector<uint8_t> pixels;
};

struct rs_renderer_t {
  rs_runtime_t* runtime{};
  std::mutex mutex;
  HWND hwnd{};
  rs_renderer_view_mode_v1 view_mode{RS_RENDERER_VIEW_FIT};
  rs_renderer_transform_v1 transform{sizeof(rs_renderer_transform_v1), 1.0f, 0.0f, 0.0f, 0};
  uint32_t pixel_width{};
  uint32_t pixel_height{};
  ComPtr<IDXGISwapChain1> swap_chain;
  ComPtr<ID2D1Factory1> d2d_factory;
  ComPtr<ID2D1Device> d2d_device;
  ComPtr<ID2D1DeviceContext> d2d_context;
  ComPtr<ID2D1Bitmap1> d2d_target;
  cached_cursor cursor;
};

struct rs_encoder_t { rs_runtime_t* runtime{}; };
struct rs_decoder_t { rs_runtime_t* runtime{}; };
struct rs_transport_t { rs_runtime_t* runtime{}; };
struct rs_input_injector_t { rs_runtime_t* runtime{}; };

std::string utf8_from_wide(const wchar_t* value);
std::wstring wide_from_utf8(rs_string_view_v1 value);
uint64_t monotonic_nanoseconds();
rs_string_view_v1 string_view(const std::string& value);
void set_last_error(rs_runtime_t* runtime, rs_status_v1 status, const char* stable_code, const std::string& detail);
void emit_cursor_metadata(rs_capture_t* capture, uint64_t frame_id, uint64_t generation);
std::vector<display_record> enumerate_displays(rs_runtime_t* runtime, bool update_generation);
std::optional<display_record> find_display(rs_runtime_t* runtime, const std::string& id);
rs_status_v1 run_wgc_capture(rs_capture_t* capture, std::stop_token stop);

inline bool struct_has(uint32_t actual_size, size_t field_offset, size_t field_size) {
  return actual_size >= field_offset + field_size;
}

template <typename T>
T copy_prefix(const T* source) {
  T destination{};
  if (source != nullptr) {
    std::memcpy(&destination, source, (std::min)(static_cast<size_t>(source->struct_size), sizeof(T)));
  }
  return destination;
}
