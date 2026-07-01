#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#include <psapi.h>
#include "remote_support_native.h"

#include <atomic>
#include <chrono>
#include <fstream>
#include <iomanip>
#include <string>
#include <thread>

namespace {
std::atomic<uint64_t> frames{};
std::atomic<uint64_t> first_timestamp{};
std::atomic<uint64_t> last_timestamp{};
std::atomic<uint64_t> timestamp_regressions{};
std::atomic<rs_status_v1> error_status{RS_STATUS_OK};
std::string display_id;

void RS_CALL on_display(void*, const rs_display_info_v1* display) {
  if (display_id.empty() || (display->flags & 1u) != 0) display_id.assign(display->display_id_utf8.data, display->display_id_utf8.length);
}
void RS_CALL on_frame(void*, const rs_frame_info_v1* frame) {
  uint64_t expected = 0;
  first_timestamp.compare_exchange_strong(expected, frame->monotonic_timestamp_ns);
  const uint64_t previous = last_timestamp.exchange(frame->monotonic_timestamp_ns);
  if (previous != 0 && frame->monotonic_timestamp_ns <= previous) timestamp_regressions.fetch_add(1);
  frames.fetch_add(1);
}
void RS_CALL on_error(void*, rs_status_v1 status, rs_string_view_v1) { error_status.store(status); }

size_t working_set() {
  PROCESS_MEMORY_COUNTERS counters{};
  return GetProcessMemoryInfo(GetCurrentProcess(), &counters, sizeof(counters)) ? counters.WorkingSetSize : 0;
}
}

int main(int argc, char** argv) {
  if (argc != 4) return 64;
  const std::string source_name = argv[1];
  const int seconds = std::stoi(argv[2]);
  if (seconds < 1 || seconds > 3600) return 65;
  rs_capture_source_v1 source = source_name == "dxgi" ? RS_CAPTURE_SOURCE_DXGI :
      source_name == "wgc" ? RS_CAPTURE_SOURCE_WGC : RS_CAPTURE_SOURCE_SYNTHETIC;
  rs_runtime_options_v1 runtime_options{sizeof(runtime_options), 1, 1, 0, nullptr};
  rs_callbacks_v1 callbacks{};
  callbacks.struct_size = sizeof(callbacks);
  callbacks.on_capture_frame = on_frame;
  callbacks.on_error = on_error;
  rs_runtime_handle runtime{};
  if (rs_runtime_create(&runtime_options, &callbacks, &runtime) != RS_STATUS_OK) return 1;
  if (source != RS_CAPTURE_SOURCE_SYNTHETIC) {
    if (rs_runtime_enumerate_displays(runtime, on_display, nullptr) != RS_STATUS_OK || display_id.empty()) return 2;
  }
  rs_capture_options_v1 options{};
  options.struct_size = sizeof(options);
  options.target_fps = 60;
  options.max_width = source_name == "synthetic4k" ? 3840u : source == RS_CAPTURE_SOURCE_SYNTHETIC ? 1920u : 0u;
  options.max_height = source_name == "synthetic4k" ? 2160u : source == RS_CAPTURE_SOURCE_SYNTHETIC ? 1080u : 0u;
  options.source = source;
  options.target_kind = source == RS_CAPTURE_SOURCE_SYNTHETIC ? RS_CAPTURE_TARGET_SYNTHETIC : RS_CAPTURE_TARGET_DISPLAY;
  options.frame_queue_capacity = 3;
  options.acquire_timeout_ms = 100;
  options.display_id_utf8 = {display_id.data(), static_cast<uint32_t>(display_id.size())};
  rs_capture_handle capture{};
  if (rs_capture_create(runtime, &options, &capture) != RS_STATUS_OK) return 3;
  const size_t memory_before = working_set();
  const auto started = std::chrono::steady_clock::now();
  if (rs_capture_start(capture) != RS_STATUS_OK) return 4;
  std::this_thread::sleep_for(std::chrono::seconds(seconds));
  rs_capture_stop(capture);
  const auto elapsed = std::chrono::duration<double>(std::chrono::steady_clock::now() - started).count();
  const size_t memory_after = working_set();
  rs_capture_destroy(capture);
  rs_runtime_destroy(runtime);

  std::ofstream output(argv[3], std::ios::binary | std::ios::trunc);
  output << "{\n"
         << "  \"source\": \"" << source_name << "\",\n"
         << "  \"durationSeconds\": " << std::fixed << std::setprecision(3) << elapsed << ",\n"
         << "  \"frames\": " << frames.load() << ",\n"
         << "  \"framesPerSecond\": " << std::setprecision(3) << static_cast<double>(frames.load()) / elapsed << ",\n"
         << "  \"workingSetBeforeBytes\": " << memory_before << ",\n"
         << "  \"workingSetAfterBytes\": " << memory_after << ",\n"
         << "  \"workingSetDeltaBytes\": " << static_cast<long long>(memory_after) - static_cast<long long>(memory_before) << ",\n"
         << "  \"timestampRegressions\": " << timestamp_regressions.load() << ",\n"
         << "  \"status\": " << static_cast<int>(error_status.load()) << "\n"
         << "}\n";
  return frames.load() > 0 && timestamp_regressions.load() == 0 && error_status.load() == RS_STATUS_OK ? 0 : 5;
}
