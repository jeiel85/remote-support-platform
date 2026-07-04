#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>

#include "remote_support_native.h"

#include <atomic>
#include <chrono>
#include <cstdio>
#include <mutex>
#include <thread>
#include <vector>

namespace {
// CTest SKIP_RETURN_CODE sentinel: the environment cannot run the encode pipeline.
constexpr int kEnvironmentUnsupportedSkip = 125;
rs_encoder_handle encoder{};
rs_decoder_handle decoder{};
rs_runtime_handle runtime{};
std::atomic<uint32_t> capabilities{};
std::atomic<uint32_t> encoded_frames{};
std::atomic<uint32_t> decoded_frames{};
std::atomic<uint32_t> resized_frames{};
std::atomic<uint32_t> keyframes{};
std::atomic<uint32_t> failures{};
std::atomic<uint32_t> fallback_events{};
std::mutex encoded_mutex;
std::vector<uint8_t> last_encoded_bytes;
rs_encoded_frame_v1 last_encoded_frame{};

void print_view(rs_string_view_v1 value) {
  if (value.data != nullptr) std::fprintf(stderr, "%.*s", static_cast<int>(value.length), value.data);
}

void RS_CALL on_error(void*, rs_status_v1 status, rs_string_view_v1 stable_code) {
  std::fprintf(stderr, "native error status=%d code=", static_cast<int>(status));
  print_view(stable_code);
  std::fprintf(stderr, "\n");
}

void RS_CALL on_fallback(void*, rs_encoder_backend_v1 failed, rs_encoder_backend_v1 selected, rs_string_view_v1 reason) {
  if (failed != RS_ENCODER_BACKEND_MEDIA_FOUNDATION_HARDWARE ||
      selected != RS_ENCODER_BACKEND_MEDIA_FOUNDATION_SOFTWARE || reason.length == 0) {
    failures.fetch_add(1);
    return;
  }
  fallback_events.fetch_add(1);
}

void RS_CALL on_capability(void*, const rs_encoder_capability_v1* capability) {
  if (capability->codec == RS_CODEC_H264) capabilities.fetch_add(1);
}

void RS_CALL on_capture(void*, const rs_frame_info_v1* frame) {
  if (encoder != nullptr) {
    const rs_status_v1 status = rs_encoder_submit_d3d11_frame(encoder, frame);
    if (status != RS_STATUS_OK) {
      std::fprintf(stderr, "encoder submit status=%d\n", static_cast<int>(status));
      char detail[512]{};
      uint32_t required = 0;
      if (runtime != nullptr && rs_runtime_get_last_error(runtime, detail, sizeof(detail), &required) == RS_STATUS_OK) {
        std::fprintf(stderr, "detail: %s\n", detail);
      }
      failures.fetch_add(1);
    }
  }
}

void RS_CALL on_encoded(void*, const rs_encoded_frame_v1* frame) {
  encoded_frames.fetch_add(1);
  if (frame->frame_kind == RS_FRAME_KIND_KEY) keyframes.fetch_add(1);
  {
    std::scoped_lock lock(encoded_mutex);
    last_encoded_bytes.assign(frame->bytes.data, frame->bytes.data + frame->bytes.length);
    last_encoded_frame = *frame;
    last_encoded_frame.bytes = {last_encoded_bytes.data(), static_cast<uint32_t>(last_encoded_bytes.size())};
  }
  if (decoder != nullptr && rs_decoder_submit_h264(decoder, frame) != RS_STATUS_OK) failures.fetch_add(1);
}

void RS_CALL on_decoded(void*, const rs_frame_info_v1* frame) {
  if (frame->d3d11_texture == nullptr || frame->pixel_format != RS_PIXEL_FORMAT_BGRA8) {
    failures.fetch_add(1);
    return;
  }
  decoded_frames.fetch_add(1);
  if (frame->width == 160 && frame->height == 96) resized_frames.fetch_add(1);
}
}

int main() {
#if defined(RS_ENABLE_TEST_FAULT_INJECTION)
  SetEnvironmentVariableW(L"RS_TEST_FAIL_HARDWARE_ENCODER", L"1");
#endif
  rs_runtime_options_v1 runtime_options{sizeof(runtime_options), 1, 1, 0, nullptr};
  rs_callbacks_v1 callbacks{};
  callbacks.struct_size = sizeof(callbacks);
  callbacks.on_capture_frame = on_capture;
  callbacks.on_encoded_frame = on_encoded;
  callbacks.on_decoded_frame = on_decoded;
  callbacks.on_error = on_error;
  callbacks.on_encoder_fallback = on_fallback;
  if (rs_runtime_create(&runtime_options, &callbacks, &runtime) != RS_STATUS_OK) return 1;
  if (rs_runtime_enumerate_encoders(runtime, on_capability, nullptr) != RS_STATUS_OK || capabilities.load() == 0) return 2;

  rs_encoder_options_v1 encoder_options{};
  encoder_options.struct_size = sizeof(encoder_options);
  encoder_options.width = 320;
  encoder_options.height = 192;
  encoder_options.target_fps = 30;
  encoder_options.target_bitrate_bps = 1'000'000;
  encoder_options.max_bitrate_bps = 2'000'000;
  encoder_options.codec = RS_CODEC_H264;
  encoder_options.quality_profile = RS_QUALITY_PROFILE_BALANCED;
  encoder_options.frame_queue_capacity = 3;
  encoder_options.prefer_hardware = 1;
  encoder_options.allow_software_fallback = 1;
  encoder_options.max_keyframe_interval_ms = 2'000;
  const auto fallback_started = std::chrono::steady_clock::now();
  const rs_status_v1 encoder_status = rs_encoder_create(runtime, &encoder_options, &encoder);
  if (encoder_status != RS_STATUS_OK) {
    char detail[512]{};
    uint32_t required = 0;
    rs_runtime_get_last_error(runtime, detail, sizeof(detail), &required);
    std::fprintf(stderr, "encoder create: %s\n", detail);
    // A headless / GPU-less host (e.g. the CI runner's WARP device) cannot perform the
    // mandatory D3D11 video color conversion, so the encoder truthfully reports
    // RS_STATUS_NOT_SUPPORTED. Skip (not fail) there; this roundtrip runs in full
    // wherever a video-capable device exists. CMake maps this code via SKIP_RETURN_CODE.
    if (encoder_status == RS_STATUS_NOT_SUPPORTED) return kEnvironmentUnsupportedSkip;
    return 3;
  }
  const double fallback_ms = std::chrono::duration<double, std::milli>(
      std::chrono::steady_clock::now() - fallback_started).count();
#if defined(RS_ENABLE_TEST_FAULT_INJECTION)
  SetEnvironmentVariableW(L"RS_TEST_FAIL_HARDWARE_ENCODER", nullptr);
  if (fallback_events.load() != 1) return 10;
  if (std::chrono::steady_clock::now() - fallback_started >= std::chrono::seconds(2)) return 11;
#endif

  rs_decoder_options_v1 decoder_options{};
  decoder_options.struct_size = sizeof(decoder_options);
  decoder_options.codec = RS_CODEC_H264;
  decoder_options.stream_format = RS_H264_STREAM_FORMAT_ANNEX_B;
  decoder_options.max_width = 320;
  decoder_options.max_height = 192;
  decoder_options.output_queue_capacity = 3;
  decoder_options.prefer_hardware = 0;
  decoder_options.allow_software_fallback = 1;
  if (rs_decoder_create(runtime, &decoder_options, &decoder) != RS_STATUS_OK) return 4;

  rs_capture_options_v1 capture_options{};
  capture_options.struct_size = sizeof(capture_options);
  capture_options.target_fps = 30;
  capture_options.max_width = 320;
  capture_options.max_height = 180;
  capture_options.source = RS_CAPTURE_SOURCE_SYNTHETIC;
  capture_options.target_kind = RS_CAPTURE_TARGET_SYNTHETIC;
  capture_options.frame_queue_capacity = 3;
  capture_options.acquire_timeout_ms = 100;
  rs_capture_handle capture{};
  if (rs_capture_create(runtime, &capture_options, &capture) != RS_STATUS_OK || rs_capture_start(capture) != RS_STATUS_OK) return 5;

  auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(10);
  while (decoded_frames.load() < 10 && failures.load() == 0 && std::chrono::steady_clock::now() < deadline) {
    std::this_thread::sleep_for(std::chrono::milliseconds(20));
  }
  if (decoded_frames.load() < 10 || failures.load() != 0) return 6;
  rs_capture_stop(capture);
  if (rs_decoder_reset(decoder) != RS_STATUS_OK) return 12;
  {
    std::scoped_lock lock(encoded_mutex);
    last_encoded_frame.frame_kind = RS_FRAME_KIND_DELTA;
    last_encoded_frame.bytes = {last_encoded_bytes.data(), static_cast<uint32_t>(last_encoded_bytes.size())};
    if (rs_decoder_submit_h264(decoder, &last_encoded_frame) != RS_STATUS_INVALID_STATE) return 13;
  }
  if (rs_encoder_set_rate(encoder, 600'000, 20) != RS_STATUS_OK || rs_encoder_request_keyframe(encoder) != RS_STATUS_OK) return 7;
  rs_encoder_reconfigure_v1 reconfigure{sizeof(reconfigure), 160, 96, 20, 500'000, 1'000'000, RS_QUALITY_PROFILE_TEXT, 0};
  if (rs_encoder_reconfigure(encoder, &reconfigure) != RS_STATUS_OK) return 8;
  if (rs_capture_start(capture) != RS_STATUS_OK) return 14;
  deadline = std::chrono::steady_clock::now() + std::chrono::seconds(10);
  while (resized_frames.load() < 5 && failures.load() == 0 && std::chrono::steady_clock::now() < deadline) {
    std::this_thread::sleep_for(std::chrono::milliseconds(20));
  }
  rs_capture_stop(capture);
  rs_capture_destroy(capture);
  rs_encoder_flush(encoder, 2'000);
  rs_decoder_flush(decoder, 2'000);
  rs_encoder_destroy(encoder);
  encoder = nullptr;
  rs_decoder_destroy(decoder);
  decoder = nullptr;
  rs_runtime_destroy(runtime);
  std::printf("capabilities=%u encoded=%u decoded=%u resized=%u keyframes=%u fallbacks=%u fallback_ms=%.1f\n",
      capabilities.load(), encoded_frames.load(), decoded_frames.load(), resized_frames.load(), keyframes.load(),
      fallback_events.load(), fallback_ms);
  return resized_frames.load() >= 5 && keyframes.load() >= 2 && failures.load() == 0 ? 0 : 9;
}
