#include "remote_support_native.h"

#include <atomic>
#include <chrono>
#include <thread>

namespace {
std::atomic<uint32_t> frame_count{};
std::atomic<uint64_t> last_timestamp{};
std::atomic<bool> timestamp_regressed{};
std::atomic<uint64_t> first_generation{};
std::atomic<uint64_t> last_generation{};

void RS_CALL on_frame(void*, const rs_frame_info_v1* frame) {
  const uint64_t previous = last_timestamp.exchange(frame->monotonic_timestamp_ns);
  if (previous != 0 && frame->monotonic_timestamp_ns <= previous) timestamp_regressed.store(true);
  uint64_t expected = 0;
  first_generation.compare_exchange_strong(expected, frame->display_generation);
  last_generation.store(frame->display_generation);
  if (frame->width == 64 && frame->height == 64 && frame->d3d11_texture != nullptr) frame_count.fetch_add(1);
}
}

int main() {
  if (rs_native_get_abi_major() != 1u || rs_native_get_abi_minor() < 3u) return 1;
  rs_runtime_options_v1 runtime_options{};
  runtime_options.struct_size = sizeof(runtime_options);
  runtime_options.requested_abi_major = 1;
  runtime_options.requested_abi_minor = 1;
  rs_callbacks_v1 callbacks{};
  callbacks.struct_size = sizeof(callbacks);
  callbacks.on_capture_frame = on_frame;
  rs_runtime_handle runtime{};
  if (rs_runtime_create(&runtime_options, &callbacks, &runtime) != RS_STATUS_OK || runtime == nullptr) return 2;

  rs_capture_options_v1 capture_options{};
  capture_options.struct_size = sizeof(capture_options);
  capture_options.target_fps = 60;
  capture_options.max_width = 64;
  capture_options.max_height = 64;
  capture_options.flags = 0x80000000u;
  capture_options.source = RS_CAPTURE_SOURCE_SYNTHETIC;
  capture_options.target_kind = RS_CAPTURE_TARGET_SYNTHETIC;
  capture_options.frame_queue_capacity = 3;
  capture_options.acquire_timeout_ms = 100;
  rs_capture_handle capture{};
  if (rs_capture_create(runtime, &capture_options, &capture) != RS_STATUS_OK || capture == nullptr) return 3;
  if (rs_capture_start(capture) != RS_STATUS_OK) return 4;
  const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(2);
  while (frame_count.load() < 20 && std::chrono::steady_clock::now() < deadline) {
    std::this_thread::sleep_for(std::chrono::milliseconds(10));
  }
  if (rs_capture_stop(capture) != RS_STATUS_OK) return 5;
  rs_capture_destroy(capture);
  if (frame_count.load() < 15 || timestamp_regressed.load() || last_generation.load() <= first_generation.load()) return 6;

  rs_renderer_options_v1 renderer_options{};
  renderer_options.struct_size = sizeof(renderer_options);
  renderer_options.view_mode = RS_RENDERER_VIEW_FIT;
  rs_renderer_handle renderer{};
  if (rs_renderer_create(runtime, &renderer_options, &renderer) != RS_STATUS_OK) return 7;
  rs_renderer_transform_v1 transform{sizeof(transform), 2.0f, 10.0f, -5.0f, 0};
  if (rs_renderer_set_transform(renderer, &transform) != RS_STATUS_OK) return 8;
  transform.zoom = 9.0f;
  if (rs_renderer_set_transform(renderer, &transform) != RS_STATUS_INVALID_ARGUMENT) return 9;
  rs_renderer_destroy(renderer);
  rs_runtime_destroy(runtime);
  return 0;
}
