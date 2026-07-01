#include "native_internal.hpp"

namespace {
rs_status_v1 create_target(rs_renderer_t* renderer, uint32_t width, uint32_t height) {
  if (renderer->hwnd == nullptr || !IsWindow(renderer->hwnd) || width == 0 || height == 0) return RS_STATUS_INVALID_STATE;
  if (renderer->swap_chain != nullptr) {
    renderer->d2d_context->SetTarget(nullptr);
    renderer->d2d_target.Reset();
    const HRESULT resized = renderer->swap_chain->ResizeBuffers(0, width, height, DXGI_FORMAT_UNKNOWN, 0);
    if (FAILED(resized)) return RS_STATUS_DEVICE_LOST;
  } else {
    ComPtr<IDXGIDevice> dxgi_device;
    ComPtr<IDXGIAdapter> adapter;
    ComPtr<IDXGIFactory2> factory;
    if (FAILED(renderer->runtime->device.As(&dxgi_device)) || FAILED(dxgi_device->GetAdapter(&adapter)) ||
        FAILED(adapter->GetParent(IID_PPV_ARGS(&factory)))) return RS_STATUS_DEVICE_LOST;
    DXGI_SWAP_CHAIN_DESC1 description{};
    description.Width = width;
    description.Height = height;
    description.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
    description.SampleDesc.Count = 1;
    description.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    description.BufferCount = 2;
    description.Scaling = DXGI_SCALING_STRETCH;
    description.SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD;
    description.AlphaMode = DXGI_ALPHA_MODE_IGNORE;
    if (FAILED(factory->CreateSwapChainForHwnd(renderer->runtime->device.Get(), renderer->hwnd, &description, nullptr, nullptr, &renderer->swap_chain))) {
      return RS_STATUS_NOT_SUPPORTED;
    }

    D2D1_FACTORY_OPTIONS options{};
#if defined(_DEBUG)
    options.debugLevel = D2D1_DEBUG_LEVEL_INFORMATION;
#endif
    if (FAILED(D2D1CreateFactory(D2D1_FACTORY_TYPE_MULTI_THREADED, __uuidof(ID2D1Factory1), &options,
        reinterpret_cast<void**>(renderer->d2d_factory.GetAddressOf())))) return RS_STATUS_NOT_SUPPORTED;
    if (FAILED(renderer->d2d_factory->CreateDevice(dxgi_device.Get(), &renderer->d2d_device)) ||
        FAILED(renderer->d2d_device->CreateDeviceContext(D2D1_DEVICE_CONTEXT_OPTIONS_NONE, &renderer->d2d_context))) {
      return RS_STATUS_NOT_SUPPORTED;
    }
  }

  ComPtr<IDXGISurface> surface;
  if (FAILED(renderer->swap_chain->GetBuffer(0, IID_PPV_ARGS(&surface)))) return RS_STATUS_DEVICE_LOST;
  D2D1_BITMAP_PROPERTIES1 properties{};
  properties.pixelFormat.format = DXGI_FORMAT_B8G8R8A8_UNORM;
  properties.pixelFormat.alphaMode = D2D1_ALPHA_MODE_IGNORE;
  properties.dpiX = 96.0f;
  properties.dpiY = 96.0f;
  properties.bitmapOptions = D2D1_BITMAP_OPTIONS_TARGET | D2D1_BITMAP_OPTIONS_CANNOT_DRAW;
  if (FAILED(renderer->d2d_context->CreateBitmapFromDxgiSurface(surface.Get(), &properties, &renderer->d2d_target))) {
    return RS_STATUS_NOT_SUPPORTED;
  }
  renderer->d2d_context->SetTarget(renderer->d2d_target.Get());
  renderer->pixel_width = width;
  renderer->pixel_height = height;
  return RS_STATUS_OK;
}

std::pair<uint32_t, uint32_t> target_size(rs_renderer_t* renderer, const rs_frame_info_v1* frame) {
  if (renderer->pixel_width != 0 && renderer->pixel_height != 0) return {renderer->pixel_width, renderer->pixel_height};
  RECT client{};
  if (renderer->hwnd != nullptr && GetClientRect(renderer->hwnd, &client)) {
    const uint32_t width = static_cast<uint32_t>((std::max)(1L, client.right - client.left));
    const uint32_t height = static_cast<uint32_t>((std::max)(1L, client.bottom - client.top));
    return {width, height};
  }
  return {frame->width, frame->height};
}

struct render_geometry {
  D2D1_RECT_F destination{};
  float scale_x{};
  float scale_y{};
};

render_geometry geometry_for(const rs_renderer_t* renderer, const rs_frame_info_v1* frame, uint32_t target_width, uint32_t target_height) {
  float scale_x = static_cast<float>(target_width) / static_cast<float>(frame->width);
  float scale_y = static_cast<float>(target_height) / static_cast<float>(frame->height);
  if (renderer->view_mode == RS_RENDERER_VIEW_FIT) {
    scale_x = scale_y = (std::min)(scale_x, scale_y);
  } else if (renderer->view_mode == RS_RENDERER_VIEW_ACTUAL_SIZE) {
    scale_x = scale_y = 1.0f;
  }
  scale_x *= renderer->transform.zoom;
  scale_y *= renderer->transform.zoom;
  const float width = static_cast<float>(frame->width) * scale_x;
  const float height = static_cast<float>(frame->height) * scale_y;
  const float left = (static_cast<float>(target_width) - width) * 0.5f - renderer->transform.pan_source_x * scale_x;
  const float top = (static_cast<float>(target_height) - height) * 0.5f - renderer->transform.pan_source_y * scale_y;
  return {{left, top, left + width, top + height}, scale_x, scale_y};
}

void draw_cursor(rs_renderer_t* renderer, const rs_frame_info_v1* frame, const render_geometry& geometry) {
  const auto& cached = renderer->cursor;
  if (cached.info.visible == 0 || cached.info.display_generation != frame->display_generation || cached.pixels.empty()) return;
  D2D1_BITMAP_PROPERTIES1 properties{};
  properties.pixelFormat.format = DXGI_FORMAT_B8G8R8A8_UNORM;
  properties.pixelFormat.alphaMode = D2D1_ALPHA_MODE_PREMULTIPLIED;
  properties.dpiX = 96.0f;
  properties.dpiY = 96.0f;
  D2D1_SIZE_U size{cached.info.width, cached.info.height};
  ComPtr<ID2D1Bitmap1> bitmap;
  if (FAILED(renderer->d2d_context->CreateBitmap(size, cached.pixels.data(), cached.info.pitch_bytes, &properties, &bitmap))) return;
  const float source_x = static_cast<float>(cached.info.desktop_x - frame->desktop_origin_x - cached.info.hotspot_x);
  const float source_y = static_cast<float>(cached.info.desktop_y - frame->desktop_origin_y - cached.info.hotspot_y);
  const float left = geometry.destination.left + source_x * geometry.scale_x;
  const float top = geometry.destination.top + source_y * geometry.scale_y;
  D2D1_RECT_F destination{left, top, left + cached.info.width * geometry.scale_x, top + cached.info.height * geometry.scale_y};
  renderer->d2d_context->DrawBitmap(bitmap.Get(), destination, 1.0f, D2D1_INTERPOLATION_MODE_LINEAR, nullptr);
}
}

extern "C" {
rs_status_v1 RS_CALL rs_renderer_create(rs_runtime_handle runtime, const rs_renderer_options_v1* options, rs_renderer_handle* out_renderer) {
  if (runtime == nullptr || options == nullptr || out_renderer == nullptr || options->struct_size < sizeof(rs_renderer_options_v1)) {
    return RS_STATUS_INVALID_ARGUMENT;
  }
  if (options->view_mode < RS_RENDERER_VIEW_FIT || options->view_mode > RS_RENDERER_VIEW_STRETCH) return RS_STATUS_INVALID_ARGUMENT;
  auto renderer = std::make_unique<rs_renderer_t>();
  renderer->runtime = runtime;
  renderer->hwnd = reinterpret_cast<HWND>(options->target_hwnd);
  renderer->view_mode = options->view_mode;
  *out_renderer = renderer.release();
  return RS_STATUS_OK;
}

rs_status_v1 RS_CALL rs_renderer_set_target_window(rs_renderer_handle renderer, uintptr_t target_hwnd) {
  if (renderer == nullptr) return RS_STATUS_INVALID_ARGUMENT;
  std::scoped_lock lock(renderer->mutex);
  renderer->hwnd = reinterpret_cast<HWND>(target_hwnd);
  renderer->swap_chain.Reset();
  renderer->d2d_target.Reset();
  renderer->d2d_context.Reset();
  renderer->d2d_device.Reset();
  renderer->d2d_factory.Reset();
  renderer->pixel_width = 0;
  renderer->pixel_height = 0;
  return target_hwnd == 0 || IsWindow(renderer->hwnd) ? RS_STATUS_OK : RS_STATUS_INVALID_ARGUMENT;
}

rs_status_v1 RS_CALL rs_renderer_set_view_mode(rs_renderer_handle renderer, rs_renderer_view_mode_v1 view_mode) {
  if (renderer == nullptr || view_mode < RS_RENDERER_VIEW_FIT || view_mode > RS_RENDERER_VIEW_STRETCH) return RS_STATUS_INVALID_ARGUMENT;
  std::scoped_lock lock(renderer->mutex);
  renderer->view_mode = view_mode;
  return RS_STATUS_OK;
}

rs_status_v1 RS_CALL rs_renderer_set_transform(rs_renderer_handle renderer, const rs_renderer_transform_v1* transform) {
  if (renderer == nullptr || transform == nullptr || transform->struct_size < sizeof(rs_renderer_transform_v1) ||
      !std::isfinite(transform->zoom) || !std::isfinite(transform->pan_source_x) || !std::isfinite(transform->pan_source_y) ||
      transform->zoom < 0.25f || transform->zoom > 8.0f) return RS_STATUS_INVALID_ARGUMENT;
  std::scoped_lock lock(renderer->mutex);
  renderer->transform = *transform;
  return RS_STATUS_OK;
}

rs_status_v1 RS_CALL rs_renderer_submit_cursor(rs_renderer_handle renderer, const rs_cursor_info_v1* cursor) {
  if (renderer == nullptr || cursor == nullptr || cursor->struct_size < sizeof(rs_cursor_info_v1) ||
      cursor->width > 256 || cursor->height > 256 || cursor->bgra8_premultiplied.length > 256u * 1024u ||
      (cursor->bgra8_premultiplied.length != 0 && cursor->bgra8_premultiplied.data == nullptr)) return RS_STATUS_INVALID_ARGUMENT;
  std::scoped_lock lock(renderer->mutex);
  if (cursor->shape_id != renderer->cursor.info.shape_id && cursor->bgra8_premultiplied.length == 0 && cursor->visible != 0) {
    return RS_STATUS_INVALID_STATE;
  }
  if (cursor->bgra8_premultiplied.length != 0) {
    renderer->cursor.pixels.assign(cursor->bgra8_premultiplied.data, cursor->bgra8_premultiplied.data + cursor->bgra8_premultiplied.length);
  }
  renderer->cursor.info = *cursor;
  renderer->cursor.info.bgra8_premultiplied = {nullptr, 0};
  return RS_STATUS_OK;
}

rs_status_v1 RS_CALL rs_renderer_submit_d3d11_frame(rs_renderer_handle renderer, const rs_frame_info_v1* frame) {
  if (renderer == nullptr || frame == nullptr || frame->struct_size < sizeof(rs_frame_info_v1) || frame->d3d11_texture == nullptr ||
      frame->width == 0 || frame->height == 0 || frame->pixel_format != RS_PIXEL_FORMAT_BGRA8) return RS_STATUS_INVALID_ARGUMENT;
  std::scoped_lock lock(renderer->mutex);
  const auto [width, height] = target_size(renderer, frame);
  if (renderer->swap_chain == nullptr || width != renderer->pixel_width || height != renderer->pixel_height) {
    const rs_status_v1 status = create_target(renderer, width, height);
    if (status != RS_STATUS_OK) return status;
  }
  auto* texture = reinterpret_cast<ID3D11Texture2D*>(frame->d3d11_texture);
  ComPtr<IDXGISurface> source_surface;
  if (FAILED(texture->QueryInterface(IID_PPV_ARGS(&source_surface)))) return RS_STATUS_INVALID_ARGUMENT;
  D2D1_BITMAP_PROPERTIES1 source_properties{};
  source_properties.pixelFormat.format = DXGI_FORMAT_B8G8R8A8_UNORM;
  source_properties.pixelFormat.alphaMode = D2D1_ALPHA_MODE_IGNORE;
  source_properties.dpiX = 96.0f;
  source_properties.dpiY = 96.0f;
  ComPtr<ID2D1Bitmap1> source_bitmap;
  if (FAILED(renderer->d2d_context->CreateBitmapFromDxgiSurface(source_surface.Get(), &source_properties, &source_bitmap))) {
    return RS_STATUS_INVALID_ARGUMENT;
  }
  const render_geometry geometry = geometry_for(renderer, frame, width, height);
  renderer->d2d_context->BeginDraw();
  const D2D1_COLOR_F black{0.0f, 0.0f, 0.0f, 1.0f};
  renderer->d2d_context->Clear(&black);
  renderer->d2d_context->DrawBitmap(source_bitmap.Get(), geometry.destination, 1.0f, D2D1_INTERPOLATION_MODE_LINEAR, nullptr);
  draw_cursor(renderer, frame, geometry);
  const HRESULT drawn = renderer->d2d_context->EndDraw();
  if (FAILED(drawn)) return drawn == D2DERR_RECREATE_TARGET ? RS_STATUS_DEVICE_LOST : RS_STATUS_INTERNAL_ERROR;
  const HRESULT presented = renderer->swap_chain->Present(0, 0);
  return SUCCEEDED(presented) ? RS_STATUS_OK : RS_STATUS_DEVICE_LOST;
}

rs_status_v1 RS_CALL rs_renderer_resize(rs_renderer_handle renderer, uint32_t pixel_width, uint32_t pixel_height) {
  if (renderer == nullptr || pixel_width == 0 || pixel_height == 0 || pixel_width > 16384 || pixel_height > 16384) return RS_STATUS_INVALID_ARGUMENT;
  std::scoped_lock lock(renderer->mutex);
  renderer->pixel_width = pixel_width;
  renderer->pixel_height = pixel_height;
  return RS_STATUS_OK;
}

rs_status_v1 RS_CALL rs_renderer_clear(rs_renderer_handle renderer) {
  if (renderer == nullptr) return RS_STATUS_INVALID_ARGUMENT;
  std::scoped_lock lock(renderer->mutex);
  if (renderer->d2d_context == nullptr || renderer->swap_chain == nullptr) return RS_STATUS_OK;
  renderer->d2d_context->BeginDraw();
  const D2D1_COLOR_F black{0.0f, 0.0f, 0.0f, 1.0f};
  renderer->d2d_context->Clear(&black);
  renderer->d2d_context->EndDraw();
  return SUCCEEDED(renderer->swap_chain->Present(0, 0)) ? RS_STATUS_OK : RS_STATUS_DEVICE_LOST;
}

void RS_CALL rs_renderer_destroy(rs_renderer_handle renderer) { delete renderer; }
}

