#include "remote_support_native.h"

#include <atomic>
#include <chrono>
#include <cstdio>
#include <thread>

namespace {
rs_encoder_handle encoder{};
std::atomic<uint32_t> encoded_frames{};
std::atomic<uint64_t> encoded_bytes{};
std::atomic<uint32_t> failures{};
std::atomic<uint32_t> fallbacks{};

void RS_CALL on_capture(void*, const rs_frame_info_v1* frame) {
  if (rs_encoder_submit_d3d11_frame(encoder, frame) != RS_STATUS_OK) failures.fetch_add(1);
}

void RS_CALL on_encoded(void*, const rs_encoded_frame_v1* frame) {
  encoded_frames.fetch_add(1);
  encoded_bytes.fetch_add(frame->bytes.length);
}

void RS_CALL on_fallback(void*, rs_encoder_backend_v1, rs_encoder_backend_v1, rs_string_view_v1) {
  fallbacks.fetch_add(1);
}
}

int main() {
  rs_runtime_options_v1 runtime_options{sizeof(runtime_options), 1, 1, 0, nullptr};
  rs_callbacks_v1 callbacks{};
  callbacks.struct_size = sizeof(callbacks);
  callbacks.on_capture_frame = on_capture;
  callbacks.on_encoded_frame = on_encoded;
  callbacks.on_encoder_fallback = on_fallback;
  rs_runtime_handle runtime{};
  if (rs_runtime_create(&runtime_options, &callbacks, &runtime) != RS_STATUS_OK) return 1;
  rs_encoder_options_v1 encoder_options{};
  encoder_options.struct_size = sizeof(encoder_options);
  encoder_options.width = 1280;
  encoder_options.height = 720;
  encoder_options.target_fps = 30;
  encoder_options.target_bitrate_bps = 4'000'000;
  encoder_options.max_bitrate_bps = 8'000'000;
  encoder_options.codec = RS_CODEC_H264;
  encoder_options.quality_profile = RS_QUALITY_PROFILE_MOTION;
  encoder_options.frame_queue_capacity = 3;
  encoder_options.prefer_hardware = 1;
  encoder_options.allow_software_fallback = 0;
  encoder_options.max_keyframe_interval_ms = 2'000;
  const auto creation_started = std::chrono::steady_clock::now();
  if (rs_encoder_create(runtime, &encoder_options, &encoder) != RS_STATUS_OK) {
    char detail[512]{};
    uint32_t required = 0;
    rs_runtime_get_last_error(runtime, detail, sizeof(detail), &required);
    std::fprintf(stderr, "hardware encoder create failed: %s\n", detail);
    return 2;
  }
  const double creation_ms = std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - creation_started).count();

  rs_capture_options_v1 capture_options{};
  capture_options.struct_size = sizeof(capture_options);
  capture_options.target_fps = 30;
  capture_options.max_width = 1280;
  capture_options.max_height = 720;
  capture_options.source = RS_CAPTURE_SOURCE_SYNTHETIC;
  capture_options.target_kind = RS_CAPTURE_TARGET_SYNTHETIC;
  capture_options.frame_queue_capacity = 3;
  capture_options.acquire_timeout_ms = 100;
  rs_capture_handle capture{};
  if (rs_capture_create(runtime, &capture_options, &capture) != RS_STATUS_OK || rs_capture_start(capture) != RS_STATUS_OK) return 3;
  std::this_thread::sleep_for(std::chrono::seconds(3));
  rs_capture_stop(capture);
  rs_capture_destroy(capture);
  rs_encoder_flush(encoder, 2'000);
  rs_encoder_destroy(encoder);
  encoder = nullptr;
  rs_runtime_destroy(runtime);
  std::printf("hardware_encoder creation_ms=%.1f frames=%u bytes=%llu fallbacks=%u failures=%u\n",
      creation_ms, encoded_frames.load(), static_cast<unsigned long long>(encoded_bytes.load()), fallbacks.load(), failures.load());
  return encoded_frames.load() >= 30 && encoded_bytes.load() > 0 && fallbacks.load() == 0 && failures.load() == 0 ? 0 : 4;
}
