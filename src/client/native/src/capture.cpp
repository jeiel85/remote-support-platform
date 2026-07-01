#include "native_internal.hpp"
#include "test_flags.hpp"

#include <array>

namespace {
using namespace std::chrono_literals;

bool luid_equal(const LUID& left, const LUID& right) {
  return left.LowPart == right.LowPart && left.HighPart == right.HighPart;
}

rs_status_v1 create_duplication(rs_capture_t* capture, const display_record& display, ComPtr<IDXGIOutputDuplication>& duplication) {
  ComPtr<IDXGIDevice> dxgi_device;
  ComPtr<IDXGIAdapter> adapter;
  if (FAILED(capture->runtime->device.As(&dxgi_device)) || FAILED(dxgi_device->GetAdapter(&adapter))) {
    return RS_STATUS_DEVICE_LOST;
  }
  DXGI_ADAPTER_DESC adapter_desc{};
  if (FAILED(adapter->GetDesc(&adapter_desc)) || !luid_equal(adapter_desc.AdapterLuid, display.adapter_luid)) {
    set_last_error(capture->runtime, RS_STATUS_NOT_SUPPORTED, "CAPTURE_UNSUPPORTED", "Selected display is not attached to the runtime D3D11 adapter.");
    return RS_STATUS_NOT_SUPPORTED;
  }
  for (UINT index = 0;; ++index) {
    ComPtr<IDXGIOutput> output;
    if (adapter->EnumOutputs(index, &output) == DXGI_ERROR_NOT_FOUND) break;
    DXGI_OUTPUT_DESC description{};
    if (FAILED(output->GetDesc(&description)) || _wcsicmp(description.DeviceName, display.gdi_name.c_str()) != 0) continue;
    ComPtr<IDXGIOutput1> output1;
    if (FAILED(output.As(&output1))) return RS_STATUS_NOT_SUPPORTED;
    const HRESULT result = output1->DuplicateOutput(capture->runtime->device.Get(), &duplication);
    if (result == E_ACCESSDENIED) {
      set_last_error(capture->runtime, RS_STATUS_ACCESS_DENIED, "CAPTURE_SECURE_DESKTOP_UNAVAILABLE", "Desktop duplication is not available for the current desktop.");
      return RS_STATUS_ACCESS_DENIED;
    }
    if (FAILED(result)) {
      set_last_error(capture->runtime, RS_STATUS_DEVICE_LOST, "CAPTURE_ACCESS_LOST", "DXGI output duplication creation failed.");
      return RS_STATUS_DEVICE_LOST;
    }
    return RS_STATUS_OK;
  }
  set_last_error(capture->runtime, RS_STATUS_NOT_SUPPORTED, "CAPTURE_TARGET_REMOVED", "Selected display output is no longer present.");
  return RS_STATUS_NOT_SUPPORTED;
}

}

void emit_cursor_metadata(rs_capture_t* capture, uint64_t frame_id, uint64_t generation) {
  if (capture->runtime->callbacks.on_cursor == nullptr) return;
  CURSORINFO cursor_info{};
  cursor_info.cbSize = sizeof(cursor_info);
  if (!GetCursorInfo(&cursor_info)) return;

  rs_cursor_info_v1 cursor{};
  cursor.struct_size = sizeof(cursor);
  cursor.frame_id = frame_id;
  cursor.display_generation = generation;
  cursor.visible = (cursor_info.flags & CURSOR_SHOWING) != 0 ? 1u : 0u;
  cursor.desktop_x = cursor_info.ptScreenPos.x;
  cursor.desktop_y = cursor_info.ptScreenPos.y;
  cursor.shape_kind = RS_CURSOR_SHAPE_COLOR;
  cursor.shape_id = static_cast<uint64_t>(reinterpret_cast<uintptr_t>(cursor_info.hCursor));

  std::vector<uint8_t> pixels;
  if (cursor.visible != 0 && cursor.shape_id != capture->cursor_shape_id) {
    ICONINFO icon{};
    if (GetIconInfo(cursor_info.hCursor, &icon)) {
      cursor.hotspot_x = static_cast<int32_t>(icon.xHotspot);
      cursor.hotspot_y = static_cast<int32_t>(icon.yHotspot);
      cursor.width = static_cast<uint32_t>((std::min)(GetSystemMetrics(SM_CXCURSOR), 256));
      cursor.height = static_cast<uint32_t>((std::min)(GetSystemMetrics(SM_CYCURSOR), 256));
      cursor.pitch_bytes = cursor.width * 4u;
      BITMAPINFO bitmap_info{};
      bitmap_info.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
      bitmap_info.bmiHeader.biWidth = static_cast<LONG>(cursor.width);
      bitmap_info.bmiHeader.biHeight = -static_cast<LONG>(cursor.height);
      bitmap_info.bmiHeader.biPlanes = 1;
      bitmap_info.bmiHeader.biBitCount = 32;
      bitmap_info.bmiHeader.biCompression = BI_RGB;
      void* bits = nullptr;
      HDC screen = GetDC(nullptr);
      HDC memory = CreateCompatibleDC(screen);
      HBITMAP bitmap = CreateDIBSection(screen, &bitmap_info, DIB_RGB_COLORS, &bits, nullptr, 0);
      if (memory != nullptr && bitmap != nullptr && bits != nullptr) {
        const HGDIOBJ previous = SelectObject(memory, bitmap);
        pixels.assign(static_cast<size_t>(cursor.pitch_bytes) * cursor.height, 0);
        std::memset(bits, 0, pixels.size());
        if (DrawIconEx(memory, 0, 0, cursor_info.hCursor, static_cast<int>(cursor.width), static_cast<int>(cursor.height), 0, nullptr, DI_NORMAL)) {
          std::memcpy(pixels.data(), bits, pixels.size());
          cursor.bgra8_premultiplied = {pixels.data(), static_cast<uint32_t>(pixels.size())};
          capture->cursor_shape_id = cursor.shape_id;
        }
        SelectObject(memory, previous);
      }
      if (bitmap != nullptr) DeleteObject(bitmap);
      if (memory != nullptr) DeleteDC(memory);
      if (screen != nullptr) ReleaseDC(nullptr, screen);
      if (icon.hbmColor != nullptr) DeleteObject(icon.hbmColor);
      if (icon.hbmMask != nullptr) DeleteObject(icon.hbmMask);
    }
  }
  capture->runtime->callbacks.on_cursor(capture->runtime->callbacks.user_context, &cursor);
}

namespace {

rs_status_v1 run_dxgi(rs_capture_t* capture, std::stop_token stop) {
  const auto display = find_display(capture->runtime, capture->target_id);
  if (!display.has_value()) {
    set_last_error(capture->runtime, RS_STATUS_NOT_SUPPORTED, "CAPTURE_TARGET_REMOVED", "Capture target was not found in the active topology.");
    return RS_STATUS_NOT_SUPPORTED;
  }
  ComPtr<IDXGIOutputDuplication> duplication;
  rs_status_v1 status = create_duplication(capture, *display, duplication);
  if (status != RS_STATUS_OK) return status;

  const uint32_t timeout = (std::min)(capture->options.acquire_timeout_ms, 1000u);
  const uint64_t minimum_interval_ns = 1'000'000'000ULL / (std::clamp)(capture->options.target_fps, 1u, 120u);
  uint64_t last_emitted_ns = 0;
  while (!stop.stop_requested()) {
    DXGI_OUTDUPL_FRAME_INFO acquired{};
    ComPtr<IDXGIResource> resource;
    const HRESULT result = duplication->AcquireNextFrame(timeout, &acquired, &resource);
    if (result == DXGI_ERROR_WAIT_TIMEOUT) continue;
    if (result == DXGI_ERROR_ACCESS_LOST || result == DXGI_ERROR_DEVICE_REMOVED || result == DXGI_ERROR_DEVICE_RESET) {
      return RS_STATUS_DEVICE_LOST;
    }
    if (FAILED(result)) {
      set_last_error(capture->runtime, RS_STATUS_INTERNAL_ERROR, "CAPTURE_ACCESS_LOST", "DXGI frame acquisition failed.");
      return RS_STATUS_INTERNAL_ERROR;
    }
    ComPtr<ID3D11Texture2D> texture;
    if (SUCCEEDED(resource.As(&texture))) {
      const uint64_t timestamp_ns = monotonic_nanoseconds();
      if (last_emitted_ns != 0 && timestamp_ns - last_emitted_ns < minimum_interval_ns) {
        duplication->ReleaseFrame();
        continue;
      }
      last_emitted_ns = timestamp_ns;
      D3D11_TEXTURE2D_DESC texture_desc{};
      texture->GetDesc(&texture_desc);
      const uint64_t frame_id = capture->next_frame_id++;
      const uint64_t generation = capture->runtime->topology_generation.load(std::memory_order_relaxed);
      rs_frame_info_v1 frame{};
      frame.struct_size = sizeof(frame);
      frame.frame_id = frame_id;
      frame.monotonic_timestamp_ns = timestamp_ns;
      frame.width = texture_desc.Width;
      frame.height = texture_desc.Height;
      frame.rotation_degrees = display->rotation_degrees;
      frame.pixel_format = texture_desc.Format == DXGI_FORMAT_NV12 ? RS_PIXEL_FORMAT_NV12 : RS_PIXEL_FORMAT_BGRA8;
      frame.display_generation = generation;
      frame.d3d11_texture = texture.Get();
      frame.desktop_origin_x = display->bounds.left;
      frame.desktop_origin_y = display->bounds.top;
      if (capture->runtime->callbacks.on_capture_frame != nullptr) {
        capture->runtime->callbacks.on_capture_frame(capture->runtime->callbacks.user_context, &frame);
      }
      emit_cursor_metadata(capture, frame_id, generation);
    }
    duplication->ReleaseFrame();
  }
  return RS_STATUS_OK;
}

void run_synthetic(rs_capture_t* capture, std::stop_token stop) {
  const uint32_t width = capture->options.max_width >= 16 ? (std::min)(capture->options.max_width, 8192u) : 1280u;
  const uint32_t height = capture->options.max_height >= 16 ? (std::min)(capture->options.max_height, 8192u) : 720u;
  const uint32_t fps = (std::clamp)(capture->options.target_fps, 1u, 120u);
  D3D11_TEXTURE2D_DESC description{};
  description.Width = width;
  description.Height = height;
  description.MipLevels = 1;
  description.ArraySize = 1;
  description.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
  description.SampleDesc.Count = 1;
  description.Usage = D3D11_USAGE_DYNAMIC;
  description.BindFlags = D3D11_BIND_SHADER_RESOURCE;
  description.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
  ComPtr<ID3D11Texture2D> texture;
  if (FAILED(capture->runtime->device->CreateTexture2D(&description, nullptr, &texture))) {
    set_last_error(capture->runtime, RS_STATUS_RESOURCE_EXHAUSTED, "CAPTURE_UNSUPPORTED", "Synthetic frame texture allocation failed.");
    return;
  }
  const auto interval = std::chrono::nanoseconds(1'000'000'000ULL / fps);
  auto next = std::chrono::steady_clock::now();
  bool topology_injected = false;
  while (!stop.stop_requested()) {
    const uint64_t frame_id = capture->next_frame_id++;
    if (!topology_injected && (capture->options.flags & rs_test_capture_inject_topology_change) != 0 && frame_id >= 15) {
      capture->runtime->topology_generation.fetch_add(1, std::memory_order_relaxed);
      topology_injected = true;
    }
    D3D11_MAPPED_SUBRESOURCE mapped{};
    if (FAILED(capture->runtime->context->Map(texture.Get(), 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped))) {
      set_last_error(capture->runtime, RS_STATUS_DEVICE_LOST, "CAPTURE_ACCESS_LOST", "Synthetic texture map failed.");
      return;
    }
    for (uint32_t y = 0; y < height; ++y) {
      auto* row = reinterpret_cast<uint32_t*>(static_cast<uint8_t*>(mapped.pData) + static_cast<size_t>(mapped.RowPitch) * y);
      for (uint32_t x = 0; x < width; ++x) {
        const uint8_t red = static_cast<uint8_t>((x + frame_id * 3u) & 0xffu);
        const uint8_t green = static_cast<uint8_t>((y + frame_id * 2u) & 0xffu);
        const uint8_t blue = static_cast<uint8_t>(((x / 32u) ^ (y / 32u)) * 48u);
        row[x] = 0xff000000u | static_cast<uint32_t>(red) << 16u | static_cast<uint32_t>(green) << 8u | blue;
      }
    }
    capture->runtime->context->Unmap(texture.Get(), 0);
    rs_frame_info_v1 frame{};
    frame.struct_size = sizeof(frame);
    frame.frame_id = frame_id;
    frame.monotonic_timestamp_ns = monotonic_nanoseconds();
    frame.width = width;
    frame.height = height;
    frame.pixel_format = RS_PIXEL_FORMAT_BGRA8;
    frame.display_generation = capture->runtime->topology_generation.load(std::memory_order_relaxed);
    frame.d3d11_texture = texture.Get();
    if (capture->runtime->callbacks.on_capture_frame != nullptr) {
      capture->runtime->callbacks.on_capture_frame(capture->runtime->callbacks.user_context, &frame);
    }
    next += interval;
    std::this_thread::sleep_until(next);
  }
}

void capture_worker(rs_capture_t* capture, std::stop_token stop) {
  rs_capture_source_v1 source = capture->options.source;
  if (source == RS_CAPTURE_SOURCE_AUTO) {
    source = capture->options.target_kind == RS_CAPTURE_TARGET_SYNTHETIC ? RS_CAPTURE_SOURCE_SYNTHETIC :
             capture->options.target_kind == RS_CAPTURE_TARGET_WINDOW ? RS_CAPTURE_SOURCE_WGC : RS_CAPTURE_SOURCE_DXGI;
  }
  if (source == RS_CAPTURE_SOURCE_SYNTHETIC) {
    run_synthetic(capture, stop);
    return;
  }
  if (source == RS_CAPTURE_SOURCE_WGC) {
    run_wgc_capture(capture, stop);
    return;
  }
  constexpr std::array<std::chrono::milliseconds, 5> delays{100ms, 250ms, 500ms, 1000ms, 2000ms};
  size_t retry = 0;
  const auto started = std::chrono::steady_clock::now();
  while (!stop.stop_requested()) {
    const rs_status_v1 status = run_dxgi(capture, stop);
    if (status == RS_STATUS_OK || status == RS_STATUS_ACCESS_DENIED || status == RS_STATUS_NOT_SUPPORTED) return;
    if (std::chrono::steady_clock::now() - started >= 30s) {
      set_last_error(capture->runtime, RS_STATUS_DEVICE_LOST, "CAPTURE_ACCESS_LOST", "Capture recovery exceeded the 30-second retry budget.");
      return;
    }
    std::this_thread::sleep_for(delays[(std::min)(retry++, delays.size() - 1)]);
  }
}
}

extern "C" {
rs_status_v1 RS_CALL rs_capture_create(rs_runtime_handle runtime, const rs_capture_options_v1* options, rs_capture_handle* out_capture) {
  if (runtime == nullptr || options == nullptr || out_capture == nullptr ||
      !struct_has(options->struct_size, offsetof(rs_capture_options_v1, display_id_utf8), sizeof(options->display_id_utf8))) {
    return RS_STATUS_INVALID_ARGUMENT;
  }
  *out_capture = nullptr;
  auto capture = std::make_unique<rs_capture_t>();
  capture->runtime = runtime;
  capture->options = copy_prefix(options);
  if (capture->options.source == RS_CAPTURE_SOURCE_UNKNOWN) capture->options.source = RS_CAPTURE_SOURCE_AUTO;
  if (capture->options.target_kind == RS_CAPTURE_TARGET_UNKNOWN) capture->options.target_kind = RS_CAPTURE_TARGET_DISPLAY;
  if (capture->options.frame_queue_capacity == 0) capture->options.frame_queue_capacity = 3;
  if (capture->options.acquire_timeout_ms == 0) capture->options.acquire_timeout_ms = 100;
  if (capture->options.frame_queue_capacity < 2 || capture->options.frame_queue_capacity > 6 ||
      capture->options.acquire_timeout_ms > 1000 || capture->options.target_fps > 120) {
    return RS_STATUS_INVALID_ARGUMENT;
  }
  if (options->display_id_utf8.data != nullptr && options->display_id_utf8.length != 0) {
    capture->target_id.assign(options->display_id_utf8.data, options->display_id_utf8.length);
  }
  *out_capture = capture.release();
  return RS_STATUS_OK;
}

rs_status_v1 RS_CALL rs_capture_start(rs_capture_handle capture) {
  if (capture == nullptr) return RS_STATUS_INVALID_ARGUMENT;
  std::scoped_lock lock(capture->state_mutex);
  if (capture->running) return RS_STATUS_ALREADY_INITIALIZED;
  capture->running = true;
  capture->worker = std::jthread([capture](std::stop_token stop) { capture_worker(capture, stop); });
  return RS_STATUS_OK;
}

rs_status_v1 RS_CALL rs_capture_stop(rs_capture_handle capture) {
  if (capture == nullptr) return RS_STATUS_INVALID_ARGUMENT;
  std::jthread worker;
  {
    std::scoped_lock lock(capture->state_mutex);
    if (!capture->running) return RS_STATUS_OK;
    capture->worker.request_stop();
    worker = std::move(capture->worker);
    capture->running = false;
  }
  if (worker.joinable()) worker.join();
  return RS_STATUS_OK;
}

rs_status_v1 RS_CALL rs_capture_set_target(rs_capture_handle capture, rs_string_view_v1 display_id_utf8) {
  if (capture == nullptr || (display_id_utf8.data == nullptr && display_id_utf8.length != 0)) return RS_STATUS_INVALID_ARGUMENT;
  std::scoped_lock lock(capture->state_mutex);
  if (capture->running) return RS_STATUS_INVALID_STATE;
  capture->target_id.assign(display_id_utf8.data == nullptr ? "" : display_id_utf8.data, display_id_utf8.length);
  return RS_STATUS_OK;
}

void RS_CALL rs_capture_destroy(rs_capture_handle capture) {
  if (capture == nullptr) return;
  rs_capture_stop(capture);
  delete capture;
}
}
