#include "native_internal.hpp"

#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Graphics.Capture.h>
#include <winrt/Windows.Graphics.DirectX.Direct3D11.h>

#include <charconv>

namespace capture_api = winrt::Windows::Graphics::Capture;
namespace directx_api = winrt::Windows::Graphics::DirectX;
namespace d3d_api = winrt::Windows::Graphics::DirectX::Direct3D11;

MIDL_INTERFACE("3628e81b-3cac-4c60-b7f4-23ce0e0c3356")
IGraphicsCaptureItemInteropLocal : public IUnknown {
  virtual HRESULT STDMETHODCALLTYPE CreateForWindow(HWND window, REFIID iid, void** result) = 0;
  virtual HRESULT STDMETHODCALLTYPE CreateForMonitor(HMONITOR monitor, REFIID iid, void** result) = 0;
};
__CRT_UUID_DECL(IGraphicsCaptureItemInteropLocal, 0x3628e81b, 0x3cac, 0x4c60, 0xb7, 0xf4, 0x23, 0xce, 0x0e, 0x0c, 0x33, 0x56)

MIDL_INTERFACE("a9b3d012-3df2-4ee3-b8d1-8695f457d3c1")
IDirect3DDxgiInterfaceAccessLocal : public IUnknown {
  virtual HRESULT STDMETHODCALLTYPE GetInterface(REFIID iid, void** object) = 0;
};
__CRT_UUID_DECL(IDirect3DDxgiInterfaceAccessLocal, 0xa9b3d012, 0x3df2, 0x4ee3, 0xb8, 0xd1, 0x86, 0x95, 0xf4, 0x57, 0xd3, 0xc1)

extern "C" HRESULT CreateDirect3D11DeviceFromDXGIDevice(IDXGIDevice* dxgi_device, IInspectable** graphics_device);

namespace {
std::optional<HWND> parse_hwnd(const std::string& target) {
  constexpr std::string_view prefix = "hwnd:";
  if (!target.starts_with(prefix)) return std::nullopt;
  uintptr_t value{};
  const char* first = target.data() + prefix.size();
  const char* last = target.data() + target.size();
  const auto parsed = std::from_chars(first, last, value, 16);
  if (parsed.ec != std::errc{} || parsed.ptr != last || value == 0) return std::nullopt;
  HWND hwnd = reinterpret_cast<HWND>(value);
  return IsWindow(hwnd) ? std::optional<HWND>(hwnd) : std::nullopt;
}

capture_api::GraphicsCaptureItem create_item(rs_capture_t* capture, int32_t& origin_x, int32_t& origin_y) {
  auto interop = winrt::get_activation_factory<capture_api::GraphicsCaptureItem, IGraphicsCaptureItemInteropLocal>();
  capture_api::GraphicsCaptureItem item{nullptr};
  if (capture->options.target_kind == RS_CAPTURE_TARGET_WINDOW) {
    const auto hwnd = parse_hwnd(capture->target_id);
    if (!hwnd.has_value()) throw winrt::hresult_invalid_argument();
    RECT bounds{};
    if (GetWindowRect(*hwnd, &bounds)) {
      origin_x = bounds.left;
      origin_y = bounds.top;
    }
    winrt::check_hresult(interop->CreateForWindow(*hwnd, winrt::guid_of<capture_api::GraphicsCaptureItem>(), winrt::put_abi(item)));
  } else {
    const auto display = find_display(capture->runtime, capture->target_id);
    if (!display.has_value() || display->monitor == nullptr) throw winrt::hresult_invalid_argument();
    origin_x = display->bounds.left;
    origin_y = display->bounds.top;
    winrt::check_hresult(interop->CreateForMonitor(display->monitor, winrt::guid_of<capture_api::GraphicsCaptureItem>(), winrt::put_abi(item)));
  }
  return item;
}

d3d_api::IDirect3DDevice create_winrt_device(ID3D11Device* device) {
  ComPtr<IDXGIDevice> dxgi_device;
  winrt::check_hresult(device->QueryInterface(IID_PPV_ARGS(&dxgi_device)));
  winrt::com_ptr<IInspectable> inspectable;
  winrt::check_hresult(CreateDirect3D11DeviceFromDXGIDevice(dxgi_device.Get(), inspectable.put()));
  return inspectable.as<d3d_api::IDirect3DDevice>();
}
}

rs_status_v1 run_wgc_capture(rs_capture_t* capture, std::stop_token stop) {
  try {
    winrt::init_apartment(winrt::apartment_type::multi_threaded);
    if (!capture_api::GraphicsCaptureSession::IsSupported()) {
      set_last_error(capture->runtime, RS_STATUS_NOT_SUPPORTED, "CAPTURE_UNSUPPORTED", "Windows.Graphics.Capture is not supported by this Windows build.");
      return RS_STATUS_NOT_SUPPORTED;
    }
    int32_t origin_x = 0;
    int32_t origin_y = 0;
    capture_api::GraphicsCaptureItem item = create_item(capture, origin_x, origin_y);
    const d3d_api::IDirect3DDevice device = create_winrt_device(capture->runtime->device.Get());
    auto size = item.Size();
    auto frame_pool = capture_api::Direct3D11CaptureFramePool::CreateFreeThreaded(
        device, directx_api::DirectXPixelFormat::B8G8R8A8UIntNormalized, static_cast<int32_t>(capture->options.frame_queue_capacity), size);
    auto session = frame_pool.CreateCaptureSession(item);
    try {
      session.IsCursorCaptureEnabled(false);
    } catch (const winrt::hresult_error&) {
    }
    std::atomic<bool> callback_failed{};
    std::atomic<bool> target_closed{};
    std::atomic<uint64_t> last_emitted_ns{};
    std::atomic<int32_t> current_width{size.Width};
    std::atomic<int32_t> current_height{size.Height};
    const uint64_t minimum_interval_ns = 1'000'000'000ULL / (std::clamp)(capture->options.target_fps, 1u, 120u);
    const winrt::event_token closed = item.Closed([&target_closed](const capture_api::GraphicsCaptureItem&, const winrt::Windows::Foundation::IInspectable&) {
      target_closed.store(true);
    });
    const winrt::event_token arrived = frame_pool.FrameArrived([capture, origin_x, origin_y, device, &callback_failed, &last_emitted_ns, &current_width, &current_height, minimum_interval_ns](const capture_api::Direct3D11CaptureFramePool& sender, const winrt::Windows::Foundation::IInspectable&) {
      try {
        auto frame_object = sender.TryGetNextFrame();
        if (frame_object == nullptr) return;
        const auto content_size = frame_object.ContentSize();
        if (content_size.Width != current_width.load() || content_size.Height != current_height.load()) {
          current_width.store(content_size.Width);
          current_height.store(content_size.Height);
          capture->runtime->topology_generation.fetch_add(1, std::memory_order_relaxed);
          frame_object.Close();
          sender.Recreate(device, directx_api::DirectXPixelFormat::B8G8R8A8UIntNormalized,
              static_cast<int32_t>(capture->options.frame_queue_capacity), content_size);
          return;
        }
        const uint64_t timestamp_ns = monotonic_nanoseconds();
        const uint64_t previous = last_emitted_ns.load(std::memory_order_relaxed);
        if (previous != 0 && timestamp_ns - previous < minimum_interval_ns) return;
        last_emitted_ns.store(timestamp_ns, std::memory_order_relaxed);
        auto access = frame_object.Surface().as<IDirect3DDxgiInterfaceAccessLocal>();
        winrt::com_ptr<ID3D11Texture2D> texture;
        winrt::check_hresult(access->GetInterface(__uuidof(ID3D11Texture2D), texture.put_void()));
        D3D11_TEXTURE2D_DESC description{};
        texture->GetDesc(&description);
        const uint64_t frame_id = capture->next_frame_id++;
        const uint64_t generation = capture->runtime->topology_generation.load(std::memory_order_relaxed);
        rs_frame_info_v1 frame{};
        frame.struct_size = sizeof(frame);
        frame.frame_id = frame_id;
        frame.monotonic_timestamp_ns = timestamp_ns;
        frame.width = description.Width;
        frame.height = description.Height;
        frame.pixel_format = RS_PIXEL_FORMAT_BGRA8;
        frame.display_generation = generation;
        frame.d3d11_texture = texture.get();
        frame.desktop_origin_x = origin_x;
        frame.desktop_origin_y = origin_y;
        if (capture->runtime->callbacks.on_capture_frame != nullptr) {
          capture->runtime->callbacks.on_capture_frame(capture->runtime->callbacks.user_context, &frame);
        }
        emit_cursor_metadata(capture, frame_id, generation);
      } catch (const winrt::hresult_error&) {
        callback_failed.store(true);
      }
    });
    session.StartCapture();
    while (!stop.stop_requested() && !callback_failed.load() && !target_closed.load()) {
      std::this_thread::sleep_for(std::chrono::milliseconds(10));
    }
    frame_pool.FrameArrived(arrived);
    item.Closed(closed);
    session.Close();
    frame_pool.Close();
    if (callback_failed.load()) {
      set_last_error(capture->runtime, RS_STATUS_DEVICE_LOST, "CAPTURE_ACCESS_LOST", "WGC frame callback failed and the capture was stopped.");
      return RS_STATUS_DEVICE_LOST;
    }
    if (target_closed.load()) {
      set_last_error(capture->runtime, RS_STATUS_CLOSED, "CAPTURE_TARGET_REMOVED", "WGC target closed while capture was active.");
      return RS_STATUS_CLOSED;
    }
    return RS_STATUS_OK;
  } catch (const winrt::hresult_access_denied&) {
    set_last_error(capture->runtime, RS_STATUS_ACCESS_DENIED, "CAPTURE_SECURE_DESKTOP_UNAVAILABLE", "WGC denied access to the selected target.");
    return RS_STATUS_ACCESS_DENIED;
  } catch (const winrt::hresult_error& error) {
    set_last_error(capture->runtime, RS_STATUS_NOT_SUPPORTED, "CAPTURE_UNSUPPORTED", utf8_from_wide(error.message().c_str()));
    return RS_STATUS_NOT_SUPPORTED;
  }
}
