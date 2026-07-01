#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#include "remote_support_native.h"

#include <atomic>
#include <chrono>
#include <thread>

namespace {
rs_renderer_handle renderer{};
std::atomic<uint32_t> rendered{};
std::atomic<rs_status_v1> render_error{RS_STATUS_OK};

LRESULT CALLBACK window_proc(HWND hwnd, UINT message, WPARAM wparam, LPARAM lparam) {
  return DefWindowProcW(hwnd, message, wparam, lparam);
}

void RS_CALL on_frame(void*, const rs_frame_info_v1* frame) {
  const rs_status_v1 status = rs_renderer_submit_d3d11_frame(renderer, frame);
  if (status == RS_STATUS_OK) rendered.fetch_add(1); else render_error.store(status);
}

void RS_CALL on_cursor(void*, const rs_cursor_info_v1* cursor) {
  const rs_status_v1 status = rs_renderer_submit_cursor(renderer, cursor);
  if (status != RS_STATUS_OK && status != RS_STATUS_INVALID_STATE) render_error.store(status);
}
}

int main() {
  const wchar_t class_name[] = L"RemoteSupportRendererSmoke";
  WNDCLASSW window_class{};
  window_class.lpfnWndProc = window_proc;
  window_class.hInstance = GetModuleHandleW(nullptr);
  window_class.lpszClassName = class_name;
  if (RegisterClassW(&window_class) == 0) return 1;
  HWND hwnd = CreateWindowExW(0, class_name, L"renderer", WS_OVERLAPPEDWINDOW, CW_USEDEFAULT, CW_USEDEFAULT, 640, 480, nullptr, nullptr, window_class.hInstance, nullptr);
  if (hwnd == nullptr) return 2;

  rs_runtime_options_v1 runtime_options{sizeof(runtime_options), 1, 1, 0, nullptr};
  rs_callbacks_v1 callbacks{};
  callbacks.struct_size = sizeof(callbacks);
  callbacks.on_capture_frame = on_frame;
  callbacks.on_cursor = on_cursor;
  rs_runtime_handle runtime{};
  if (rs_runtime_create(&runtime_options, &callbacks, &runtime) != RS_STATUS_OK) return 3;
  rs_renderer_options_v1 renderer_options{sizeof(renderer_options), reinterpret_cast<uintptr_t>(hwnd), RS_RENDERER_VIEW_FIT, 0};
  if (rs_renderer_create(runtime, &renderer_options, &renderer) != RS_STATUS_OK) return 4;
  if (rs_renderer_resize(renderer, 640, 480) != RS_STATUS_OK) return 5;

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
  if (rs_capture_create(runtime, &capture_options, &capture) != RS_STATUS_OK || rs_capture_start(capture) != RS_STATUS_OK) return 6;
  const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(5);
  while (rendered.load() < 10 && render_error.load() == RS_STATUS_OK && std::chrono::steady_clock::now() < deadline) {
    MSG message{};
    while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) DispatchMessageW(&message);
    std::this_thread::sleep_for(std::chrono::milliseconds(10));
  }
  rs_capture_stop(capture);
  rs_capture_destroy(capture);
  rs_renderer_clear(renderer);
  rs_renderer_destroy(renderer);
  rs_runtime_destroy(runtime);
  DestroyWindow(hwnd);
  UnregisterClassW(class_name, window_class.hInstance);
  return rendered.load() >= 10 && render_error.load() == RS_STATUS_OK ? 0 : 7;
}
