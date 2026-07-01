#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#include <psapi.h>

#include "remote_support_native.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cstdint>
#include <fstream>
#include <iomanip>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

namespace {
constexpr uint32_t static_content_flag = 0x40000000u;
rs_encoder_handle encoder{};
rs_decoder_handle decoder{};
std::atomic<int> phase{};
std::atomic<uint64_t> encoded_bytes[2]{};
std::atomic<uint64_t> encoded_frames[2]{};
std::atomic<uint64_t> decoded_frames[2]{};
std::atomic<uint64_t> timestamp_regressions{};
std::atomic<uint64_t> last_encoded_timestamp{};
std::atomic<uint32_t> failures{};
std::mutex latency_mutex;
std::vector<double> encode_latency_ms;
std::vector<double> decode_latency_ms;

uint64_t monotonic_ns() {
  LARGE_INTEGER counter{};
  LARGE_INTEGER frequency{};
  QueryPerformanceCounter(&counter);
  QueryPerformanceFrequency(&frequency);
  const uint64_t seconds = static_cast<uint64_t>(counter.QuadPart / frequency.QuadPart);
  const uint64_t remainder = static_cast<uint64_t>(counter.QuadPart % frequency.QuadPart);
  return seconds * 1'000'000'000ULL + remainder * 1'000'000'000ULL / static_cast<uint64_t>(frequency.QuadPart);
}

size_t working_set() {
  PROCESS_MEMORY_COUNTERS counters{};
  return GetProcessMemoryInfo(GetCurrentProcess(), &counters, sizeof(counters)) ? counters.WorkingSetSize : 0;
}

double percentile(std::vector<double> samples, double quantile) {
  if (samples.empty()) return 0;
  std::sort(samples.begin(), samples.end());
  const size_t index = static_cast<size_t>(quantile * static_cast<double>(samples.size() - 1));
  return samples[index];
}

void RS_CALL on_capture(void*, const rs_frame_info_v1* frame) {
  if (rs_encoder_submit_d3d11_frame(encoder, frame) != RS_STATUS_OK) failures.fetch_add(1);
}

void RS_CALL on_encoded(void*, const rs_encoded_frame_v1* frame) {
  const int current_phase = phase.load();
  encoded_bytes[current_phase].fetch_add(frame->bytes.length);
  encoded_frames[current_phase].fetch_add(1);
  const uint64_t previous = last_encoded_timestamp.exchange(frame->monotonic_timestamp_ns);
  if (previous != 0 && frame->monotonic_timestamp_ns <= previous) timestamp_regressions.fetch_add(1);
  {
    std::scoped_lock lock(latency_mutex);
    encode_latency_ms.push_back(static_cast<double>(monotonic_ns() - frame->monotonic_timestamp_ns) / 1'000'000.0);
  }
  if (rs_decoder_submit_h264(decoder, frame) != RS_STATUS_OK) failures.fetch_add(1);
}

void RS_CALL on_decoded(void*, const rs_frame_info_v1* frame) {
  decoded_frames[phase.load()].fetch_add(1);
  std::scoped_lock lock(latency_mutex);
  decode_latency_ms.push_back(static_cast<double>(monotonic_ns() - frame->monotonic_timestamp_ns) / 1'000'000.0);
}

bool run_phase(rs_runtime_handle runtime, int phase_index, bool static_content, int duration_seconds, double& elapsed_seconds) {
  phase.store(phase_index);
  rs_capture_options_v1 options{};
  options.struct_size = sizeof(options);
  options.target_fps = 30;
  options.max_width = 640;
  options.max_height = 360;
  options.source = RS_CAPTURE_SOURCE_SYNTHETIC;
  options.target_kind = RS_CAPTURE_TARGET_SYNTHETIC;
  options.frame_queue_capacity = 3;
  options.acquire_timeout_ms = 100;
  options.flags = static_content ? static_content_flag : 0;
  rs_capture_handle capture{};
  if (rs_capture_create(runtime, &options, &capture) != RS_STATUS_OK) return false;
  rs_encoder_request_keyframe(encoder);
  const auto started = std::chrono::steady_clock::now();
  if (rs_capture_start(capture) != RS_STATUS_OK) return false;
  std::this_thread::sleep_for(std::chrono::seconds(duration_seconds));
  rs_capture_stop(capture);
  elapsed_seconds = std::chrono::duration<double>(std::chrono::steady_clock::now() - started).count();
  rs_capture_destroy(capture);
  return true;
}
}

int main(int argc, char** argv) {
  if (argc != 2) return 64;
  rs_runtime_options_v1 runtime_options{sizeof(runtime_options), 1, 1, 0, nullptr};
  rs_callbacks_v1 callbacks{};
  callbacks.struct_size = sizeof(callbacks);
  callbacks.on_capture_frame = on_capture;
  callbacks.on_encoded_frame = on_encoded;
  callbacks.on_decoded_frame = on_decoded;
  rs_runtime_handle runtime{};
  if (rs_runtime_create(&runtime_options, &callbacks, &runtime) != RS_STATUS_OK) return 1;

  rs_encoder_options_v1 encoder_options{};
  encoder_options.struct_size = sizeof(encoder_options);
  encoder_options.width = 640;
  encoder_options.height = 360;
  encoder_options.target_fps = 30;
  encoder_options.target_bitrate_bps = 2'000'000;
  encoder_options.max_bitrate_bps = 4'000'000;
  encoder_options.codec = RS_CODEC_H264;
  encoder_options.quality_profile = RS_QUALITY_PROFILE_BALANCED;
  encoder_options.frame_queue_capacity = 3;
  encoder_options.allow_software_fallback = 1;
  encoder_options.max_keyframe_interval_ms = 2'000;
  if (rs_encoder_create(runtime, &encoder_options, &encoder) != RS_STATUS_OK) return 2;

  rs_decoder_options_v1 decoder_options{};
  decoder_options.struct_size = sizeof(decoder_options);
  decoder_options.codec = RS_CODEC_H264;
  decoder_options.stream_format = RS_H264_STREAM_FORMAT_ANNEX_B;
  decoder_options.max_width = 640;
  decoder_options.max_height = 360;
  decoder_options.output_queue_capacity = 3;
  decoder_options.allow_software_fallback = 1;
  if (rs_decoder_create(runtime, &decoder_options, &decoder) != RS_STATUS_OK) return 3;

  double warmup_seconds = 0;
  if (!run_phase(runtime, 0, true, 2, warmup_seconds)) return 4;
  encoded_bytes[0].store(0);
  encoded_frames[0].store(0);
  decoded_frames[0].store(0);
  last_encoded_timestamp.store(0);
  {
    std::scoped_lock lock(latency_mutex);
    encode_latency_ms.clear();
    decode_latency_ms.clear();
  }
  const size_t memory_before = working_set();
  double idle_seconds = 0;
  double motion_seconds = 0;
  if (!run_phase(runtime, 0, true, 5, idle_seconds) || !run_phase(runtime, 1, false, 5, motion_seconds)) return 4;
  rs_encoder_flush(encoder, 2'000);
  rs_decoder_flush(decoder, 2'000);
  const size_t memory_after = working_set();
  rs_encoder_destroy(encoder);
  encoder = nullptr;
  rs_decoder_destroy(decoder);
  decoder = nullptr;
  rs_runtime_destroy(runtime);

  const double idle_bitrate = static_cast<double>(encoded_bytes[0].load()) * 8.0 / idle_seconds;
  const double motion_bitrate = static_cast<double>(encoded_bytes[1].load()) * 8.0 / motion_seconds;
  const double encode_p50 = percentile(encode_latency_ms, 0.50);
  const double encode_p95 = percentile(encode_latency_ms, 0.95);
  const double decode_p50 = percentile(decode_latency_ms, 0.50);
  const double decode_p95 = percentile(decode_latency_ms, 0.95);
  std::ofstream output(argv[1], std::ios::binary | std::ios::trunc);
  output << "{\n"
         << "  \"idleDurationSeconds\": " << std::fixed << std::setprecision(3) << idle_seconds << ",\n"
         << "  \"motionDurationSeconds\": " << motion_seconds << ",\n"
         << "  \"idleFrames\": " << encoded_frames[0].load() << ",\n"
         << "  \"motionFrames\": " << encoded_frames[1].load() << ",\n"
         << "  \"decodedFrames\": " << decoded_frames[0].load() + decoded_frames[1].load() << ",\n"
         << "  \"idleBitrateBps\": " << std::setprecision(1) << idle_bitrate << ",\n"
         << "  \"motionBitrateBps\": " << motion_bitrate << ",\n"
         << "  \"idleToMotionBitrateRatio\": " << idle_bitrate / motion_bitrate << ",\n"
         << "  \"encodeLatencyP50Ms\": " << encode_p50 << ",\n"
         << "  \"encodeLatencyP95Ms\": " << encode_p95 << ",\n"
         << "  \"captureToDecodeLatencyP50Ms\": " << decode_p50 << ",\n"
         << "  \"captureToDecodeLatencyP95Ms\": " << decode_p95 << ",\n"
         << "  \"workingSetBeforeBytes\": " << memory_before << ",\n"
         << "  \"workingSetAfterBytes\": " << memory_after << ",\n"
         << "  \"workingSetDeltaBytes\": " << static_cast<long long>(memory_after) - static_cast<long long>(memory_before) << ",\n"
         << "  \"timestampRegressions\": " << timestamp_regressions.load() << ",\n"
         << "  \"failures\": " << failures.load() << "\n"
         << "}\n";
  const bool material_idle_reduction = idle_bitrate < motion_bitrate * 0.85;
  return encoded_frames[0].load() >= 100 && encoded_frames[1].load() >= 100 &&
      decoded_frames[0].load() + decoded_frames[1].load() >= 150 && material_idle_reduction &&
      timestamp_regressions.load() == 0 && failures.load() == 0 ? 0 : 5;
}
