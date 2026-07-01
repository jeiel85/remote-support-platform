#include "remote_support_native.h"

#include <atomic>
#include <chrono>
#include <cstdio>
#include <string>
#include <thread>

namespace {
std::string display_id;
std::atomic<uint32_t> frame_count{};
std::atomic<rs_status_v1> error_status{RS_STATUS_OK};

void RS_CALL on_display(void*, const rs_display_info_v1* display) {
  if (display_id.empty() || (display->flags & 1u) != 0) display_id.assign(display->display_id_utf8.data, display->display_id_utf8.length);
}
void RS_CALL on_frame(void*, const rs_frame_info_v1* frame) {
  if (frame->width > 0 && frame->height > 0 && frame->d3d11_texture != nullptr) frame_count.fetch_add(1);
}
void RS_CALL on_error(void*, rs_status_v1 status, rs_string_view_v1) { error_status.store(status); }
}

int main() {
  rs_runtime_options_v1 runtime_options{sizeof(runtime_options), 1, 1, 0, nullptr};
  rs_callbacks_v1 callbacks{};
  callbacks.struct_size = sizeof(callbacks);
  callbacks.on_capture_frame = on_frame;
  callbacks.on_error = on_error;
  rs_runtime_handle runtime{};
  if (rs_runtime_create(&runtime_options, &callbacks, &runtime) != RS_STATUS_OK) return 1;
  if (rs_runtime_enumerate_displays(runtime, on_display, nullptr) != RS_STATUS_OK || display_id.empty()) return 2;
  rs_capture_options_v1 options{};
  options.struct_size = sizeof(options);
  options.target_fps = 60;
  options.source = RS_CAPTURE_SOURCE_WGC;
  options.target_kind = RS_CAPTURE_TARGET_DISPLAY;
  options.frame_queue_capacity = 3;
  options.acquire_timeout_ms = 100;
  options.display_id_utf8 = {display_id.data(), static_cast<uint32_t>(display_id.size())};
  rs_capture_handle capture{};
  if (rs_capture_create(runtime, &options, &capture) != RS_STATUS_OK || rs_capture_start(capture) != RS_STATUS_OK) return 3;
  const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(10);
  while (frame_count.load() < 10 && error_status.load() == RS_STATUS_OK && std::chrono::steady_clock::now() < deadline) {
    std::this_thread::sleep_for(std::chrono::milliseconds(20));
  }
  rs_capture_stop(capture);
  rs_capture_destroy(capture);
  rs_runtime_destroy(runtime);
  std::printf("wgc frames=%u status=%d\n", frame_count.load(), static_cast<int>(error_status.load()));
  return frame_count.load() >= 10 && error_status.load() == RS_STATUS_OK ? 0 : 4;
}
