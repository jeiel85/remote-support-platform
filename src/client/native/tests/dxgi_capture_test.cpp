#include "remote_support_native.h"

#include <atomic>
#include <chrono>
#include <cstdio>
#include <string>
#include <thread>

namespace {
std::string display_id;
std::atomic<uint32_t> frame_count{};
std::atomic<uint32_t> cursor_count{};
std::atomic<rs_status_v1> error_status{RS_STATUS_OK};

void RS_CALL on_display(void*, const rs_display_info_v1* display) {
  if (display_id.empty() || (display->flags & 1u) != 0) {
    display_id.assign(display->display_id_utf8.data, display->display_id_utf8.length);
  }
}

void RS_CALL on_frame(void*, const rs_frame_info_v1* frame) {
  if (frame->width > 0 && frame->height > 0 && frame->d3d11_texture != nullptr) frame_count.fetch_add(1);
}

void RS_CALL on_cursor(void*, const rs_cursor_info_v1*) { cursor_count.fetch_add(1); }
void RS_CALL on_error(void*, rs_status_v1 status, rs_string_view_v1) { error_status.store(status); }
}

int main() {
  rs_runtime_options_v1 runtime_options{};
  runtime_options.struct_size = sizeof(runtime_options);
  runtime_options.requested_abi_major = 1;
  runtime_options.requested_abi_minor = 1;
  rs_callbacks_v1 callbacks{};
  callbacks.struct_size = sizeof(callbacks);
  callbacks.on_capture_frame = on_frame;
  callbacks.on_cursor = on_cursor;
  callbacks.on_error = on_error;
  rs_runtime_handle runtime{};
  if (rs_runtime_create(&runtime_options, &callbacks, &runtime) != RS_STATUS_OK) return 1;
  if (rs_runtime_enumerate_displays(runtime, on_display, nullptr) != RS_STATUS_OK || display_id.empty()) return 2;

  rs_capture_options_v1 capture_options{};
  capture_options.struct_size = sizeof(capture_options);
  capture_options.target_fps = 60;
  capture_options.source = RS_CAPTURE_SOURCE_DXGI;
  capture_options.target_kind = RS_CAPTURE_TARGET_DISPLAY;
  capture_options.frame_queue_capacity = 3;
  capture_options.acquire_timeout_ms = 100;
  capture_options.display_id_utf8 = {display_id.data(), static_cast<uint32_t>(display_id.size())};
  rs_capture_handle capture{};
  if (rs_capture_create(runtime, &capture_options, &capture) != RS_STATUS_OK) return 3;
  if (rs_capture_start(capture) != RS_STATUS_OK) return 4;
  const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(10);
  while (frame_count.load() < 10 && error_status.load() == RS_STATUS_OK && std::chrono::steady_clock::now() < deadline) {
    std::this_thread::sleep_for(std::chrono::milliseconds(20));
  }
  rs_capture_stop(capture);
  rs_capture_destroy(capture);
  rs_runtime_destroy(runtime);
  std::printf("dxgi frames=%u cursors=%u status=%d\n", frame_count.load(), cursor_count.load(), static_cast<int>(error_status.load()));
  return frame_count.load() >= 10 && cursor_count.load() > 0 && error_status.load() == RS_STATUS_OK ? 0 : 5;
}
