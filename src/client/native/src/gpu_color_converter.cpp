#include "native_internal.hpp"

namespace {
HRESULT ensure_converter(rs_encoder_t* encoder, const D3D11_TEXTURE2D_DESC& source) {
  const uint32_t output_width = encoder->options.width;
  const uint32_t output_height = encoder->options.height;
  const bool needs_video_encoder_binding = encoder->backend == RS_ENCODER_BACKEND_MEDIA_FOUNDATION_HARDWARE &&
      encoder->uses_dxgi_surface;
  const bool matches = encoder->video_processor != nullptr &&
      encoder->converter_source_width == source.Width && encoder->converter_source_height == source.Height &&
      encoder->converter_output_width == output_width && encoder->converter_output_height == output_height &&
      encoder->converter_video_encoder_binding == needs_video_encoder_binding;
  if (matches) return S_OK;

  encoder->video_enumerator.Reset();
  encoder->video_processor.Reset();
  encoder->video_input_surface.Reset();
  encoder->nv12_surface.Reset();
  encoder->readback.Reset();
  HRESULT result = encoder->runtime->device.As(&encoder->video_device);
  if (SUCCEEDED(result)) result = encoder->runtime->context.As(&encoder->video_context);
  if (FAILED(result)) return result;

  D3D11_VIDEO_PROCESSOR_CONTENT_DESC content{};
  content.InputFrameFormat = D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE;
  content.InputFrameRate = {encoder->options.target_fps, 1};
  content.InputWidth = source.Width;
  content.InputHeight = source.Height;
  content.OutputFrameRate = {encoder->options.target_fps, 1};
  content.OutputWidth = output_width;
  content.OutputHeight = output_height;
  content.Usage = D3D11_VIDEO_USAGE_PLAYBACK_NORMAL;
  result = encoder->video_device->CreateVideoProcessorEnumerator(&content, &encoder->video_enumerator);
  UINT input_support = 0;
  UINT output_support = 0;
  if (SUCCEEDED(result)) result = encoder->video_enumerator->CheckVideoProcessorFormat(source.Format, &input_support);
  if (SUCCEEDED(result)) result = encoder->video_enumerator->CheckVideoProcessorFormat(DXGI_FORMAT_NV12, &output_support);
  if (SUCCEEDED(result) && (input_support & D3D11_VIDEO_PROCESSOR_FORMAT_SUPPORT_INPUT) == 0) result = E_NOTIMPL;
  if (SUCCEEDED(result) && (output_support & D3D11_VIDEO_PROCESSOR_FORMAT_SUPPORT_OUTPUT) == 0) result = E_NOTIMPL;
  if (SUCCEEDED(result)) result = encoder->video_device->CreateVideoProcessor(encoder->video_enumerator.Get(), 0, &encoder->video_processor);

  D3D11_TEXTURE2D_DESC input = source;
  input.MipLevels = 1;
  input.ArraySize = 1;
  input.Usage = D3D11_USAGE_DEFAULT;
  input.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
  input.CPUAccessFlags = 0;
  input.MiscFlags = 0;
  if (SUCCEEDED(result)) result = encoder->runtime->device->CreateTexture2D(&input, nullptr, &encoder->video_input_surface);

  D3D11_TEXTURE2D_DESC output{};
  output.Width = output_width;
  output.Height = output_height;
  output.MipLevels = 1;
  output.ArraySize = 1;
  output.Format = DXGI_FORMAT_NV12;
  output.SampleDesc.Count = 1;
  output.Usage = D3D11_USAGE_DEFAULT;
  output.BindFlags = D3D11_BIND_RENDER_TARGET |
      (needs_video_encoder_binding ? D3D11_BIND_VIDEO_ENCODER : 0u);
  if (SUCCEEDED(result)) result = encoder->runtime->device->CreateTexture2D(&output, nullptr, &encoder->nv12_surface);
  if (FAILED(result)) return result;

  encoder->converter_source_width = source.Width;
  encoder->converter_source_height = source.Height;
  encoder->converter_output_width = output_width;
  encoder->converter_output_height = output_height;
  encoder->converter_video_encoder_binding = needs_video_encoder_binding;
  return S_OK;
}

HRESULT copy_nv12_to_system_memory(rs_encoder_t* encoder) {
  D3D11_TEXTURE2D_DESC description{};
  encoder->nv12_surface->GetDesc(&description);
  description.BindFlags = 0;
  description.MiscFlags = 0;
  description.Usage = D3D11_USAGE_STAGING;
  description.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
  if (encoder->readback == nullptr) {
    const HRESULT created = encoder->runtime->device->CreateTexture2D(&description, nullptr, &encoder->readback);
    if (FAILED(created)) return created;
  }
  encoder->runtime->context->CopyResource(encoder->readback.Get(), encoder->nv12_surface.Get());
  D3D11_MAPPED_SUBRESOURCE mapped{};
  HRESULT result = encoder->runtime->context->Map(encoder->readback.Get(), 0, D3D11_MAP_READ, 0, &mapped);
  if (FAILED(result)) return result;
  const size_t y_size = static_cast<size_t>(description.Width) * description.Height;
  encoder->nv12.resize(y_size + y_size / 2u);
  for (uint32_t row = 0; row < description.Height; ++row) {
    std::memcpy(encoder->nv12.data() + static_cast<size_t>(row) * description.Width,
        static_cast<const uint8_t*>(mapped.pData) + static_cast<size_t>(row) * mapped.RowPitch, description.Width);
  }
  const auto* mapped_uv = static_cast<const uint8_t*>(mapped.pData) + static_cast<size_t>(mapped.RowPitch) * description.Height;
  for (uint32_t row = 0; row < description.Height / 2u; ++row) {
    std::memcpy(encoder->nv12.data() + y_size + static_cast<size_t>(row) * description.Width,
        mapped_uv + static_cast<size_t>(row) * mapped.RowPitch, description.Width);
  }
  encoder->runtime->context->Unmap(encoder->readback.Get(), 0);
  return S_OK;
}
}

HRESULT convert_bgra_to_nv12_gpu(rs_encoder_t* encoder, const rs_frame_info_v1* frame, bool copy_to_system_memory) {
  auto* source_texture = reinterpret_cast<ID3D11Texture2D*>(frame->d3d11_texture);
  D3D11_TEXTURE2D_DESC source{};
  source_texture->GetDesc(&source);
  if (source.Format != DXGI_FORMAT_B8G8R8A8_UNORM && source.Format != DXGI_FORMAT_B8G8R8A8_UNORM_SRGB) {
    return MF_E_INVALIDMEDIATYPE;
  }
  HRESULT result = ensure_converter(encoder, source);
  if (FAILED(result)) return result;
  encoder->runtime->context->CopyResource(encoder->video_input_surface.Get(), source_texture);

  D3D11_VIDEO_PROCESSOR_INPUT_VIEW_DESC input_description{};
  input_description.ViewDimension = D3D11_VPIV_DIMENSION_TEXTURE2D;
  input_description.Texture2D.MipSlice = 0;
  input_description.Texture2D.ArraySlice = 0;
  ComPtr<ID3D11VideoProcessorInputView> input_view;
  result = encoder->video_device->CreateVideoProcessorInputView(
      encoder->video_input_surface.Get(), encoder->video_enumerator.Get(), &input_description, &input_view);
  if (FAILED(result)) return result;

  D3D11_VIDEO_PROCESSOR_OUTPUT_VIEW_DESC output_description{};
  output_description.ViewDimension = D3D11_VPOV_DIMENSION_TEXTURE2D;
  output_description.Texture2D.MipSlice = 0;
  ComPtr<ID3D11VideoProcessorOutputView> output_view;
  result = encoder->video_device->CreateVideoProcessorOutputView(
      encoder->nv12_surface.Get(), encoder->video_enumerator.Get(), &output_description, &output_view);
  if (FAILED(result)) return result;

  const RECT source_rect{0, 0, static_cast<LONG>(source.Width), static_cast<LONG>(source.Height)};
  const RECT output_rect{0, 0, static_cast<LONG>(encoder->options.width), static_cast<LONG>(encoder->options.height)};
  encoder->video_context->VideoProcessorSetStreamFrameFormat(encoder->video_processor.Get(), 0, D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE);
  encoder->video_context->VideoProcessorSetStreamSourceRect(encoder->video_processor.Get(), 0, TRUE, &source_rect);
  encoder->video_context->VideoProcessorSetStreamDestRect(encoder->video_processor.Get(), 0, TRUE, &output_rect);
  encoder->video_context->VideoProcessorSetOutputTargetRect(encoder->video_processor.Get(), TRUE, &output_rect);
  D3D11_VIDEO_PROCESSOR_STREAM stream{};
  stream.Enable = TRUE;
  stream.pInputSurface = input_view.Get();
  result = encoder->video_context->VideoProcessorBlt(encoder->video_processor.Get(), output_view.Get(), 0, 1, &stream);
  if (SUCCEEDED(result) && copy_to_system_memory) result = copy_nv12_to_system_memory(encoder);
  return result;
}
