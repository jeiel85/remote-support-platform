#include "native_internal.hpp"

#include <propvarutil.h>

#include <cstdio>

namespace {
constexpr uint32_t minimum_dimension = 16;
constexpr uint32_t maximum_dimension = 8192;
constexpr uint32_t minimum_bitrate = 64'000;
constexpr uint32_t maximum_bitrate = 100'000'000;
constexpr GUID software_h264_encoder_clsid{0x6ca50344, 0x051a, 0x4ded, {0x97, 0x79, 0xa4, 0x33, 0x05, 0x16, 0x5e, 0x35}};
constexpr GUID software_h264_decoder_clsid{0x62ce7e72, 0x4c71, 0x4d20, {0xb1, 0x5d, 0x45, 0x28, 0x31, 0xa8, 0x7d, 0x9d}};

HRESULT process_encoder_input(rs_encoder_t* encoder, const rs_frame_info_v1* frame);
HRESULT validate_encoder_candidate(rs_encoder_t* encoder);

std::string hresult_detail(const char* message, HRESULT result) {
  char buffer[160]{};
  std::snprintf(buffer, sizeof(buffer), "%s HRESULT=0x%08lx", message, static_cast<unsigned long>(result));
  return buffer;
}

void remember_error(rs_runtime_t* runtime, const std::string& detail) {
  std::scoped_lock lock(runtime->error_mutex);
  runtime->last_error = detail;
}

void clear_error(rs_runtime_t* runtime) {
  std::scoped_lock lock(runtime->error_mutex);
  runtime->last_error.clear();
}

bool valid_dimensions(uint32_t width, uint32_t height) {
  return width >= minimum_dimension && width <= maximum_dimension && height >= minimum_dimension && height <= maximum_dimension &&
         (width & 1u) == 0 && (height & 1u) == 0;
}

HRESULT set_common_video_type(IMFMediaType* type, const GUID& subtype, uint32_t width, uint32_t height, uint32_t fps) {
  HRESULT result = type->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video);
  if (SUCCEEDED(result)) result = type->SetGUID(MF_MT_SUBTYPE, subtype);
  if (SUCCEEDED(result)) result = MFSetAttributeSize(type, MF_MT_FRAME_SIZE, width, height);
  if (SUCCEEDED(result)) result = MFSetAttributeRatio(type, MF_MT_FRAME_RATE, fps, 1);
  if (SUCCEEDED(result)) result = MFSetAttributeRatio(type, MF_MT_PIXEL_ASPECT_RATIO, 1, 1);
  if (SUCCEEDED(result)) result = type->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive);
  return result;
}

HRESULT set_codec_uint(ICodecAPI* codec, const GUID& key, uint32_t value) {
  VARIANT variant{};
  variant.vt = VT_UI4;
  variant.ulVal = value;
  return codec->SetValue(&key, &variant);
}

HRESULT set_codec_bool(ICodecAPI* codec, const GUID& key, bool value) {
  VARIANT variant{};
  variant.vt = VT_BOOL;
  variant.boolVal = value ? VARIANT_TRUE : VARIANT_FALSE;
  return codec->SetValue(&key, &variant);
}

void stop_transform(IMFTransform* transform) {
  if (transform == nullptr) return;
  transform->ProcessMessage(MFT_MESSAGE_NOTIFY_END_OF_STREAM, 0);
  transform->ProcessMessage(MFT_MESSAGE_COMMAND_DRAIN, 0);
  transform->ProcessMessage(MFT_MESSAGE_NOTIFY_END_STREAMING, 0);
  transform->ProcessMessage(MFT_MESSAGE_COMMAND_FLUSH, 0);
}

HRESULT configure_encoder_transform(rs_encoder_t* encoder, IMFTransform* transform) {
  ComPtr<IMFAttributes> attributes;
  UINT32 asynchronous = FALSE;
  if (SUCCEEDED(transform->GetAttributes(&attributes)) && attributes != nullptr) {
    attributes->GetUINT32(MF_TRANSFORM_ASYNC, &asynchronous);
    if (asynchronous != FALSE) attributes->SetUINT32(MF_TRANSFORM_ASYNC_UNLOCK, TRUE);
  }
  encoder->asynchronous = asynchronous != FALSE;

  ComPtr<IMFMediaType> output_type;
  HRESULT result = MFCreateMediaType(&output_type);
  if (SUCCEEDED(result)) result = set_common_video_type(output_type.Get(), MFVideoFormat_H264,
      encoder->options.width, encoder->options.height, encoder->options.target_fps);
  if (SUCCEEDED(result)) result = output_type->SetUINT32(MF_MT_AVG_BITRATE, encoder->options.target_bitrate_bps);
  if (SUCCEEDED(result)) result = output_type->SetUINT32(MF_MT_MPEG2_PROFILE,
      encoder->options.quality_profile == RS_QUALITY_PROFILE_MOTION ? eAVEncH264VProfile_Main : eAVEncH264VProfile_Base);
  if (SUCCEEDED(result)) result = transform->SetOutputType(0, output_type.Get(), 0);
  if (FAILED(result)) {
    remember_error(encoder->runtime, hresult_detail("Media Foundation encoder output type configuration failed.", result));
    return result;
  }

  ComPtr<IMFMediaType> input_type;
  result = MFCreateMediaType(&input_type);
  if (SUCCEEDED(result)) result = set_common_video_type(input_type.Get(), MFVideoFormat_NV12,
      encoder->options.width, encoder->options.height, encoder->options.target_fps);
  if (SUCCEEDED(result)) result = transform->SetInputType(0, input_type.Get(), 0);
  if (FAILED(result)) {
    remember_error(encoder->runtime, hresult_detail("Media Foundation encoder input type configuration failed.", result));
    return result;
  }

  ComPtr<ICodecAPI> codec;
  if (SUCCEEDED(transform->QueryInterface(IID_PPV_ARGS(&codec)))) {
    result = set_codec_bool(codec.Get(), CODECAPI_AVLowLatencyMode, true);
    if (SUCCEEDED(result)) result = set_codec_uint(codec.Get(), CODECAPI_AVEncMPVDefaultBPictureCount, 0);
    if (FAILED(result)) {
      remember_error(encoder->runtime,
          hresult_detail("Encoder did not accept the required low-latency, no-B-frame configuration.", result));
      return result;
    }
    set_codec_uint(codec.Get(), CODECAPI_AVEncCommonRateControlMode, eAVEncCommonRateControlMode_LowDelayVBR);
    set_codec_uint(codec.Get(), CODECAPI_AVEncCommonMeanBitRate, encoder->options.target_bitrate_bps);
    set_codec_uint(codec.Get(), CODECAPI_AVEncCommonMaxBitRate, encoder->options.max_bitrate_bps);
    const uint32_t gop_frames = (std::max)(1u, encoder->options.max_keyframe_interval_ms * encoder->options.target_fps / 1000u);
    set_codec_uint(codec.Get(), CODECAPI_AVEncMPVGOPSize, gop_frames);
  }
  result = transform->ProcessMessage(MFT_MESSAGE_NOTIFY_BEGIN_STREAMING, 0);
  if (SUCCEEDED(result)) result = transform->ProcessMessage(MFT_MESSAGE_NOTIFY_START_OF_STREAM, 0);
  if (FAILED(result)) {
    remember_error(encoder->runtime, hresult_detail("Media Foundation encoder stream start failed.", result));
  }
  return result;
}

HRESULT activate_hardware_encoder(rs_encoder_t* encoder) {
#if defined(RS_ENABLE_TEST_FAULT_INJECTION)
  wchar_t injected[2]{};
  if (GetEnvironmentVariableW(L"RS_TEST_FAIL_HARDWARE_ENCODER", injected, 2) > 0) return E_FAIL;
#endif
  encoder->dxgi_device_manager.Reset();
  HRESULT manager_result = MFCreateDXGIDeviceManager(&encoder->dxgi_reset_token, &encoder->dxgi_device_manager);
  if (SUCCEEDED(manager_result)) {
    manager_result = encoder->dxgi_device_manager->ResetDevice(encoder->runtime->device.Get(), encoder->dxgi_reset_token);
  }
  if (FAILED(manager_result)) return manager_result;
  MFT_REGISTER_TYPE_INFO input{MFMediaType_Video, MFVideoFormat_NV12};
  MFT_REGISTER_TYPE_INFO output{MFMediaType_Video, MFVideoFormat_H264};
  IMFActivate** activations = nullptr;
  UINT32 count = 0;
  const HRESULT enumerated = MFTEnumEx(MFT_CATEGORY_VIDEO_ENCODER,
      MFT_ENUM_FLAG_HARDWARE | MFT_ENUM_FLAG_SORTANDFILTER, &input, &output, &activations, &count);
  if (FAILED(enumerated)) return enumerated;
  HRESULT selected = MF_E_TOPO_CODEC_NOT_FOUND;
  for (UINT32 index = 0; index < count; ++index) {
    ComPtr<IMFActivate> activation;
    activation.Attach(activations[index]);
    ComPtr<IMFTransform> transform;
    HRESULT activated = activation->ActivateObject(IID_PPV_ARGS(&transform));
    if (SUCCEEDED(activated)) {
      ComPtr<IMFAttributes> attributes;
      UINT32 asynchronous = FALSE;
      UINT32 d3d11_aware = FALSE;
      if (SUCCEEDED(transform->GetAttributes(&attributes)) && attributes != nullptr &&
          SUCCEEDED(attributes->GetUINT32(MF_TRANSFORM_ASYNC, &asynchronous)) && asynchronous != FALSE) {
        attributes->SetUINT32(MF_TRANSFORM_ASYNC_UNLOCK, TRUE);
      }
      if (attributes != nullptr) attributes->GetUINT32(MF_SA_D3D11_AWARE, &d3d11_aware);
      encoder->uses_dxgi_surface = d3d11_aware != FALSE;
      if (encoder->uses_dxgi_surface) {
        const HRESULT manager_status = transform->ProcessMessage(MFT_MESSAGE_SET_D3D_MANAGER,
            reinterpret_cast<ULONG_PTR>(encoder->dxgi_device_manager.Get()));
        if (FAILED(manager_status)) encoder->uses_dxgi_surface = false;
      }
    }
    HRESULT configured = activated;
    if (SUCCEEDED(configured)) configured = configure_encoder_transform(encoder, transform.Get());
    if (SUCCEEDED(configured)) {
      encoder->activation = std::move(activation);
      encoder->transform = std::move(transform);
      encoder->backend = RS_ENCODER_BACKEND_MEDIA_FOUNDATION_HARDWARE;
      selected = validate_encoder_candidate(encoder);
      if (SUCCEEDED(selected)) {
        for (++index; index < count; ++index) activations[index]->Release();
        break;
      }
      stop_transform(encoder->transform.Get());
      encoder->activation->ShutdownObject();
      encoder->activation.Reset();
      encoder->transform.Reset();
      encoder->backend = RS_ENCODER_BACKEND_UNKNOWN;
    }
    if (FAILED(activated)) {
      remember_error(encoder->runtime,
          hresult_detail("Hardware encoder could not be activated for the runtime D3D11 device.", activated));
    }
    if (activation != nullptr) activation->ShutdownObject();
  }
  CoTaskMemFree(activations);
  return selected;
}

HRESULT activate_software_encoder(rs_encoder_t* encoder) {
  encoder->dxgi_device_manager.Reset();
  encoder->uses_dxgi_surface = false;
  ComPtr<IMFTransform> transform;
  HRESULT result = CoCreateInstance(software_h264_encoder_clsid, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&transform));
  if (SUCCEEDED(result)) result = configure_encoder_transform(encoder, transform.Get());
  if (SUCCEEDED(result)) {
    encoder->activation.Reset();
    encoder->transform = std::move(transform);
    encoder->backend = RS_ENCODER_BACKEND_MEDIA_FOUNDATION_SOFTWARE;
    result = validate_encoder_candidate(encoder);
  }
  return result;
}

void notify_fallback(rs_encoder_t* encoder, const char* reason) {
  if (encoder->runtime->callbacks.on_encoder_fallback == nullptr) return;
  const std::string stable_reason(reason);
  encoder->runtime->callbacks.on_encoder_fallback(encoder->runtime->callbacks.user_context,
      RS_ENCODER_BACKEND_MEDIA_FOUNDATION_HARDWARE, RS_ENCODER_BACKEND_MEDIA_FOUNDATION_SOFTWARE, string_view(stable_reason));
}

HRESULT select_encoder(rs_encoder_t* encoder, bool allow_hardware) {
  if (encoder->transform != nullptr) stop_transform(encoder->transform.Get());
  if (encoder->activation != nullptr) encoder->activation->ShutdownObject();
  encoder->transform.Reset();
  encoder->activation.Reset();
  encoder->backend = RS_ENCODER_BACKEND_UNKNOWN;
  if (allow_hardware && encoder->options.prefer_hardware != 0) {
    const HRESULT hardware = activate_hardware_encoder(encoder);
    if (SUCCEEDED(hardware)) return S_OK;
    if (encoder->options.allow_software_fallback == 0) return hardware;
    const HRESULT software = activate_software_encoder(encoder);
    if (SUCCEEDED(software)) notify_fallback(encoder, "ENCODER_HARDWARE_FAILED");
    return software;
  }
  return activate_software_encoder(encoder);
}

uint8_t clamp_byte(int value) { return static_cast<uint8_t>((std::clamp)(value, 0, 255)); }

HRESULT make_sample(const uint8_t* bytes, size_t length, int64_t timestamp_100ns, int64_t duration_100ns, ComPtr<IMFSample>& sample) {
  if (length > UINT32_MAX) return E_INVALIDARG;
  ComPtr<IMFMediaBuffer> buffer;
  HRESULT result = MFCreateMemoryBuffer(static_cast<DWORD>(length), &buffer);
  BYTE* destination = nullptr;
  if (SUCCEEDED(result)) result = buffer->Lock(&destination, nullptr, nullptr);
  if (SUCCEEDED(result)) {
    std::memcpy(destination, bytes, length);
    buffer->Unlock();
    result = buffer->SetCurrentLength(static_cast<DWORD>(length));
  }
  if (SUCCEEDED(result)) result = MFCreateSample(&sample);
  if (SUCCEEDED(result)) result = sample->AddBuffer(buffer.Get());
  if (SUCCEEDED(result)) result = sample->SetSampleTime(timestamp_100ns);
  if (SUCCEEDED(result)) result = sample->SetSampleDuration(duration_100ns);
  return result;
}

HRESULT make_surface_sample(ID3D11Texture2D* texture, int64_t timestamp_100ns, int64_t duration_100ns, ComPtr<IMFSample>& sample) {
  ComPtr<IMFMediaBuffer> buffer;
  HRESULT result = MFCreateDXGISurfaceBuffer(__uuidof(ID3D11Texture2D), texture, 0, FALSE, &buffer);
  if (SUCCEEDED(result)) result = MFCreateSample(&sample);
  if (SUCCEEDED(result)) result = sample->AddBuffer(buffer.Get());
  if (SUCCEEDED(result)) result = sample->SetSampleTime(timestamp_100ns);
  if (SUCCEEDED(result)) result = sample->SetSampleDuration(duration_100ns);
  return result;
}

void normalize_annex_b(const uint8_t* bytes, size_t length, std::vector<uint8_t>& output) {
  output.clear();
  if (length >= 4 && bytes[0] == 0 && bytes[1] == 0 && (bytes[2] == 1 || (bytes[2] == 0 && bytes[3] == 1))) {
    output.assign(bytes, bytes + length);
    return;
  }
  size_t offset = 0;
  while (offset + 4 <= length) {
    const uint32_t nal_length = static_cast<uint32_t>(bytes[offset]) << 24u | static_cast<uint32_t>(bytes[offset + 1]) << 16u |
        static_cast<uint32_t>(bytes[offset + 2]) << 8u | bytes[offset + 3];
    offset += 4;
    if (nal_length == 0 || offset + nal_length > length) {
      output.assign(bytes, bytes + length);
      return;
    }
    output.insert(output.end(), {0, 0, 0, 1});
    output.insert(output.end(), bytes + offset, bytes + offset + nal_length);
    offset += nal_length;
  }
  if (offset != length) output.assign(bytes, bytes + length);
}

HRESULT drain_encoder(rs_encoder_t* encoder, const rs_frame_info_v1* source_frame, bool& emitted) {
  MFT_OUTPUT_STREAM_INFO stream_info{};
  HRESULT result = encoder->transform->GetOutputStreamInfo(0, &stream_info);
  if (FAILED(result)) return result;
  ComPtr<IMFSample> output_sample;
  MFT_OUTPUT_DATA_BUFFER output{};
  if ((stream_info.dwFlags & MFT_OUTPUT_STREAM_PROVIDES_SAMPLES) == 0) {
    const DWORD capacity = (std::max)(stream_info.cbSize, static_cast<DWORD>((std::max)(1'048'576u, encoder->options.max_bitrate_bps / 8u)));
    ComPtr<IMFMediaBuffer> buffer;
    result = MFCreateSample(&output_sample);
    if (SUCCEEDED(result)) result = MFCreateMemoryBuffer(capacity, &buffer);
    if (SUCCEEDED(result)) result = output_sample->AddBuffer(buffer.Get());
    if (FAILED(result)) return result;
    output.pSample = output_sample.Get();
  }
  DWORD status = 0;
  result = encoder->transform->ProcessOutput(0, 1, &output, &status);
  if (output.pEvents != nullptr) output.pEvents->Release();
  if (result == MF_E_TRANSFORM_NEED_MORE_INPUT) return result;
  if (FAILED(result)) return result;
  ComPtr<IMFSample> transform_owned_sample;
  if (output.pSample != nullptr && output_sample == nullptr) transform_owned_sample.Attach(output.pSample);
  IMFSample* produced = transform_owned_sample != nullptr ? transform_owned_sample.Get() : output_sample.Get();
  if (produced == nullptr) return E_UNEXPECTED;
  ComPtr<IMFMediaBuffer> contiguous;
  result = produced->ConvertToContiguousBuffer(&contiguous);
  BYTE* data = nullptr;
  DWORD length = 0;
  if (SUCCEEDED(result)) result = contiguous->Lock(&data, nullptr, &length);
  if (FAILED(result)) return result;
  normalize_annex_b(data, length, encoder->encoded);
  contiguous->Unlock();
  UINT32 clean_point = FALSE;
  produced->GetUINT32(MFSampleExtension_CleanPoint, &clean_point);
  rs_encoded_frame_v1 frame{};
  frame.struct_size = sizeof(frame);
  frame.frame_id = source_frame->frame_id;
  frame.rtp_timestamp_90khz = source_frame->monotonic_timestamp_ns * 90'000ULL / 1'000'000'000ULL;
  frame.monotonic_timestamp_ns = source_frame->monotonic_timestamp_ns;
  frame.frame_kind = clean_point != FALSE ? RS_FRAME_KIND_KEY : RS_FRAME_KIND_DELTA;
  frame.codec = RS_CODEC_H264;
  frame.bytes = {encoder->encoded.data(), static_cast<uint32_t>(encoder->encoded.size())};
  frame.qp = -1;
  frame.width = encoder->options.width;
  frame.height = encoder->options.height;
  frame.stream_format = RS_H264_STREAM_FORMAT_ANNEX_B;
  frame.h264_profile_idc = encoder->options.quality_profile == RS_QUALITY_PROFILE_MOTION ? 77u : 66u;
  frame.h264_level_idc = encoder->options.width * encoder->options.height > 1920u * 1080u ? 51u : 42u;
  if (encoder->output_callback != nullptr) {
    if (!encoder->suppress_output) encoder->output_callback(encoder->output_callback_context, &frame);
  } else if (encoder->runtime->callbacks.on_encoded_frame != nullptr) {
    if (!encoder->suppress_output) {
      encoder->runtime->callbacks.on_encoded_frame(encoder->runtime->callbacks.user_context, &frame);
    }
  }
  encoder->output_count++;
  encoder->force_keyframe = false;
  encoder->force_keyframe_submitted = false;
  emitted = true;
  return S_OK;
}

HRESULT wait_for_event(IMFTransform* transform, MediaEventType desired, uint32_t timeout_ms) {
  ComPtr<IMFMediaEventGenerator> generator;
  HRESULT result = transform->QueryInterface(IID_PPV_ARGS(&generator));
  if (FAILED(result)) return result;
  const auto deadline = std::chrono::steady_clock::now() + std::chrono::milliseconds(timeout_ms);
  while (std::chrono::steady_clock::now() < deadline) {
    ComPtr<IMFMediaEvent> event;
    result = generator->GetEvent(MF_EVENT_FLAG_NO_WAIT, &event);
    if (result == MF_E_NO_EVENTS_AVAILABLE) {
      std::this_thread::sleep_for(std::chrono::milliseconds(1));
      continue;
    }
    if (FAILED(result)) return result;
    MediaEventType type{};
    event->GetType(&type);
    HRESULT event_status = S_OK;
    event->GetStatus(&event_status);
    if (FAILED(event_status)) return event_status;
    if (type == desired) return S_OK;
  }
  return HRESULT_FROM_WIN32(ERROR_TIMEOUT);
}

HRESULT process_encoder_input(rs_encoder_t* encoder, const rs_frame_info_v1* frame) {
  if (encoder->force_keyframe && !encoder->force_keyframe_submitted) {
    ComPtr<ICodecAPI> codec;
    HRESULT forced = encoder->transform->QueryInterface(IID_PPV_ARGS(&codec));
    if (SUCCEEDED(forced)) forced = set_codec_uint(codec.Get(), CODECAPI_AVEncVideoForceKeyFrame, 1);
    if (FAILED(forced)) return forced;
    encoder->force_keyframe_submitted = true;
  }
  ComPtr<IMFSample> sample;
  const int64_t timestamp = static_cast<int64_t>(frame->monotonic_timestamp_ns / 100ULL);
  const int64_t duration = 10'000'000LL / encoder->options.target_fps;
  HRESULT result = encoder->backend == RS_ENCODER_BACKEND_MEDIA_FOUNDATION_HARDWARE && encoder->uses_dxgi_surface
      ? make_surface_sample(encoder->nv12_surface.Get(), timestamp, duration, sample)
      : make_sample(encoder->nv12.data(), encoder->nv12.size(), timestamp, duration, sample);
  if (FAILED(result)) return result;
  if (encoder->asynchronous) {
    result = wait_for_event(encoder->transform.Get(), METransformNeedInput, 2000);
    if (FAILED(result)) return result;
  }
  result = encoder->transform->ProcessInput(0, sample.Get(), 0);
  if (result == MF_E_NOTACCEPTING) {
    bool ignored = false;
    drain_encoder(encoder, frame, ignored);
    result = encoder->transform->ProcessInput(0, sample.Get(), 0);
  }
  if (FAILED(result)) return result;
  if (encoder->asynchronous) {
    result = wait_for_event(encoder->transform.Get(), METransformHaveOutput, 2000);
    if (FAILED(result)) return result;
  }
  bool emitted = false;
  if (encoder->asynchronous) {
    return drain_encoder(encoder, frame, emitted);
  }
  do {
    result = drain_encoder(encoder, frame, emitted);
  } while (SUCCEEDED(result));
  return result == MF_E_TRANSFORM_NEED_MORE_INPUT ? S_OK : result;
}

HRESULT validate_encoder_candidate(rs_encoder_t* encoder) {
  const char* failed_stage = "Color-bar texture allocation failed.";
  D3D11_TEXTURE2D_DESC description{};
  description.Width = encoder->options.width;
  description.Height = encoder->options.height;
  description.MipLevels = 1;
  description.ArraySize = 1;
  description.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
  description.SampleDesc.Count = 1;
  description.Usage = D3D11_USAGE_DYNAMIC;
  description.BindFlags = D3D11_BIND_SHADER_RESOURCE;
  description.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
  ComPtr<ID3D11Texture2D> texture;
  HRESULT result = encoder->runtime->device->CreateTexture2D(&description, nullptr, &texture);
  D3D11_MAPPED_SUBRESOURCE mapped{};
  if (SUCCEEDED(result)) {
    failed_stage = "Color-bar texture mapping failed.";
    result = encoder->runtime->context->Map(texture.Get(), 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped);
  }
  if (SUCCEEDED(result)) {
    constexpr uint32_t colors[]{0xfff8f8f8u, 0xffffff00u, 0xff00ffffu, 0xff00ff00u,
        0xffff00ffu, 0xffff0000u, 0xff0000ffu, 0xff101010u};
    for (uint32_t y = 0; y < description.Height; ++y) {
      auto* row = reinterpret_cast<uint32_t*>(static_cast<uint8_t*>(mapped.pData) + static_cast<size_t>(mapped.RowPitch) * y);
      for (uint32_t x = 0; x < description.Width; ++x) {
        row[x] = colors[(static_cast<size_t>(x) * std::size(colors)) / description.Width];
      }
    }
    encoder->runtime->context->Unmap(texture.Get(), 0);
  }
  rs_frame_info_v1 frame{};
  frame.struct_size = sizeof(frame);
  frame.frame_id = 0;
  frame.monotonic_timestamp_ns = monotonic_nanoseconds();
  frame.width = description.Width;
  frame.height = description.Height;
  frame.pixel_format = RS_PIXEL_FORMAT_BGRA8;
  frame.d3d11_texture = texture.Get();
  const uint64_t output_before = encoder->output_count;
  encoder->suppress_output = true;
  if (SUCCEEDED(result)) {
    failed_stage = "Color-bar GPU conversion failed.";
    result = convert_bgra_to_nv12_gpu(encoder, &frame,
        encoder->backend == RS_ENCODER_BACKEND_MEDIA_FOUNDATION_SOFTWARE || !encoder->uses_dxgi_surface);
  }
  if (SUCCEEDED(result)) {
    failed_stage = "Color-bar encode failed.";
    for (uint32_t attempt = 0; SUCCEEDED(result) && encoder->output_count == output_before && attempt < 60; ++attempt) {
      frame.frame_id = attempt;
      frame.monotonic_timestamp_ns += 1'000'000'000ULL / encoder->options.target_fps;
      result = process_encoder_input(encoder, &frame);
    }
  }
  if (SUCCEEDED(result) && encoder->output_count == output_before) {
    failed_stage = "Color-bar validation produced no output.";
    result = MF_E_TRANSFORM_NEED_MORE_INPUT;
  }
  encoder->suppress_output = false;
  encoder->transform->ProcessMessage(MFT_MESSAGE_COMMAND_FLUSH, 0);
  encoder->transform->ProcessMessage(MFT_MESSAGE_NOTIFY_START_OF_STREAM, 0);
  encoder->force_keyframe = true;
  encoder->force_keyframe_submitted = false;
  encoder->last_timestamp_ns = 0;
  if (FAILED(result)) {
    remember_error(encoder->runtime, hresult_detail(failed_stage, result));
  }
  return result;
}

HRESULT configure_decoder(rs_decoder_t* decoder) {
  HRESULT result = CoCreateInstance(software_h264_decoder_clsid, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&decoder->transform));
  ComPtr<IMFMediaType> input_type;
  if (SUCCEEDED(result)) result = MFCreateMediaType(&input_type);
  if (SUCCEEDED(result)) result = set_common_video_type(input_type.Get(), MFVideoFormat_H264,
      decoder->options.max_width, decoder->options.max_height, 60);
  if (SUCCEEDED(result)) result = decoder->transform->SetInputType(0, input_type.Get(), 0);
  if (FAILED(result)) return result;
  for (DWORD index = 0;; ++index) {
    ComPtr<IMFMediaType> candidate;
    if (FAILED(decoder->transform->GetOutputAvailableType(0, index, &candidate))) break;
    GUID subtype{};
    if (SUCCEEDED(candidate->GetGUID(MF_MT_SUBTYPE, &subtype)) && subtype == MFVideoFormat_NV12 &&
        SUCCEEDED(decoder->transform->SetOutputType(0, candidate.Get(), 0))) break;
  }
  result = decoder->transform->ProcessMessage(MFT_MESSAGE_NOTIFY_BEGIN_STREAMING, 0);
  if (SUCCEEDED(result)) result = decoder->transform->ProcessMessage(MFT_MESSAGE_NOTIFY_START_OF_STREAM, 0);
  return result;
}

HRESULT select_decoder_output(rs_decoder_t* decoder) {
  for (DWORD index = 0;; ++index) {
    ComPtr<IMFMediaType> candidate;
    HRESULT result = decoder->transform->GetOutputAvailableType(0, index, &candidate);
    if (FAILED(result)) return result;
    GUID subtype{};
    if (SUCCEEDED(candidate->GetGUID(MF_MT_SUBTYPE, &subtype)) && subtype == MFVideoFormat_NV12) {
      result = decoder->transform->SetOutputType(0, candidate.Get(), 0);
      if (SUCCEEDED(result)) return S_OK;
    }
  }
}

HRESULT emit_decoded_frame(rs_decoder_t* decoder, const rs_encoded_frame_v1* source, IMFSample* sample) {
  ComPtr<IMFMediaType> output_type;
  HRESULT result = decoder->transform->GetOutputCurrentType(0, &output_type);
  UINT32 width = 0, height = 0;
  if (SUCCEEDED(result)) result = MFGetAttributeSize(output_type.Get(), MF_MT_FRAME_SIZE, &width, &height);
  if (FAILED(result) || !valid_dimensions(width, height)) return MF_E_INVALIDMEDIATYPE;
  ComPtr<IMFMediaBuffer> buffer;
  result = sample->ConvertToContiguousBuffer(&buffer);
  BYTE* nv12 = nullptr;
  DWORD length = 0;
  if (SUCCEEDED(result)) result = buffer->Lock(&nv12, nullptr, &length);
  const size_t required = static_cast<size_t>(width) * height * 3u / 2u;
  if (FAILED(result) || length < required) {
    if (nv12 != nullptr) buffer->Unlock();
    return MF_E_BUFFERTOOSMALL;
  }
  decoder->bgra.resize(static_cast<size_t>(width) * height * 4u);
  const uint8_t* y_plane = nv12;
  const uint8_t* uv_plane = nv12 + static_cast<size_t>(width) * height;
  for (uint32_t y = 0; y < height; ++y) {
    for (uint32_t x = 0; x < width; ++x) {
      const int luminance = (std::max)(0, static_cast<int>(y_plane[static_cast<size_t>(y) * width + x]) - 16);
      const size_t uv_offset = static_cast<size_t>(y / 2) * width + (x & ~1u);
      const int u = static_cast<int>(uv_plane[uv_offset]) - 128;
      const int v = static_cast<int>(uv_plane[uv_offset + 1]) - 128;
      const int c = 298 * luminance;
      uint8_t* pixel = decoder->bgra.data() + (static_cast<size_t>(y) * width + x) * 4u;
      pixel[2] = clamp_byte((c + 459 * v + 128) >> 8);
      pixel[1] = clamp_byte((c - 55 * u - 136 * v + 128) >> 8);
      pixel[0] = clamp_byte((c + 541 * u + 128) >> 8);
      pixel[3] = 255;
    }
  }
  buffer->Unlock();

  bool recreate = decoder->output_texture == nullptr || decoder->width != width || decoder->height != height;
  if (recreate) {
    D3D11_TEXTURE2D_DESC description{};
    description.Width = width;
    description.Height = height;
    description.MipLevels = 1;
    description.ArraySize = 1;
    description.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
    description.SampleDesc.Count = 1;
    description.Usage = D3D11_USAGE_DYNAMIC;
    description.BindFlags = D3D11_BIND_SHADER_RESOURCE;
    description.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
    decoder->output_texture.Reset();
    result = decoder->runtime->device->CreateTexture2D(&description, nullptr, &decoder->output_texture);
    if (FAILED(result)) return result;
    decoder->width = width;
    decoder->height = height;
  }
  D3D11_MAPPED_SUBRESOURCE mapped{};
  result = decoder->runtime->context->Map(decoder->output_texture.Get(), 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped);
  if (FAILED(result)) return result;
  for (uint32_t y = 0; y < height; ++y) {
    std::memcpy(static_cast<uint8_t*>(mapped.pData) + static_cast<size_t>(mapped.RowPitch) * y,
        decoder->bgra.data() + static_cast<size_t>(width) * 4u * y, static_cast<size_t>(width) * 4u);
  }
  decoder->runtime->context->Unmap(decoder->output_texture.Get(), 0);
  rs_frame_info_v1 frame{};
  frame.struct_size = sizeof(frame);
  frame.frame_id = source->frame_id;
  frame.monotonic_timestamp_ns = source->monotonic_timestamp_ns;
  frame.width = width;
  frame.height = height;
  frame.pixel_format = RS_PIXEL_FORMAT_BGRA8;
  frame.d3d11_texture = decoder->output_texture.Get();
  if (decoder->remote_transport_output && decoder->runtime->callbacks.on_remote_video_frame != nullptr) {
    decoder->runtime->callbacks.on_remote_video_frame(decoder->runtime->callbacks.user_context, &frame);
  } else if (decoder->runtime->callbacks.on_decoded_frame != nullptr) {
    decoder->runtime->callbacks.on_decoded_frame(decoder->runtime->callbacks.user_context, &frame);
  }
  return S_OK;
}

HRESULT drain_decoder(rs_decoder_t* decoder, const rs_encoded_frame_v1* source, bool& emitted) {
  MFT_OUTPUT_STREAM_INFO info{};
  HRESULT result = decoder->transform->GetOutputStreamInfo(0, &info);
  if (result == MF_E_TRANSFORM_TYPE_NOT_SET) {
    result = select_decoder_output(decoder);
    if (SUCCEEDED(result)) result = decoder->transform->GetOutputStreamInfo(0, &info);
  }
  if (FAILED(result)) return result;
  ComPtr<IMFSample> sample;
  ComPtr<IMFMediaBuffer> buffer;
  result = MFCreateSample(&sample);
  const DWORD capacity = (std::max)(info.cbSize, static_cast<DWORD>(decoder->options.max_width * decoder->options.max_height * 3u / 2u));
  if (SUCCEEDED(result)) result = MFCreateMemoryBuffer(capacity, &buffer);
  if (SUCCEEDED(result)) result = sample->AddBuffer(buffer.Get());
  if (FAILED(result)) return result;
  MFT_OUTPUT_DATA_BUFFER output{0, sample.Get(), 0, nullptr};
  DWORD status = 0;
  result = decoder->transform->ProcessOutput(0, 1, &output, &status);
  if (output.pEvents != nullptr) output.pEvents->Release();
  if (result == MF_E_TRANSFORM_STREAM_CHANGE) {
    result = select_decoder_output(decoder);
    if (SUCCEEDED(result)) return drain_decoder(decoder, source, emitted);
  }
  if (FAILED(result)) return result;
  result = emit_decoded_frame(decoder, source, sample.Get());
  if (SUCCEEDED(result)) emitted = true;
  return result;
}

HRESULT process_decoder_input(rs_decoder_t* decoder, const rs_encoded_frame_v1* frame) {
  ComPtr<IMFSample> sample;
  HRESULT result = make_sample(frame->bytes.data, frame->bytes.length,
      static_cast<int64_t>(frame->monotonic_timestamp_ns / 100ULL), 0, sample);
  if (FAILED(result)) return result;
  result = decoder->transform->ProcessInput(0, sample.Get(), 0);
  if (FAILED(result)) return result;
  bool emitted = false;
  do {
    result = drain_decoder(decoder, frame, emitted);
  } while (SUCCEEDED(result));
  return result == MF_E_TRANSFORM_NEED_MORE_INPUT ? S_OK : result;
}

void enumerate_encoder_class(UINT32 flags, rs_encoder_backend_v1 backend,
    rs_encoder_capability_callback_v1 callback, void* context) {
  MFT_REGISTER_TYPE_INFO input{MFMediaType_Video, MFVideoFormat_NV12};
  MFT_REGISTER_TYPE_INFO output{MFMediaType_Video, MFVideoFormat_H264};
  IMFActivate** activations = nullptr;
  UINT32 count = 0;
  if (FAILED(MFTEnumEx(MFT_CATEGORY_VIDEO_ENCODER, flags | MFT_ENUM_FLAG_SORTANDFILTER, &input, &output, &activations, &count))) return;
  for (UINT32 index = 0; index < count; ++index) {
    WCHAR* friendly = nullptr;
    UINT32 length = 0;
    activations[index]->GetAllocatedString(MFT_FRIENDLY_NAME_Attribute, &friendly, &length);
    const std::string name = utf8_from_wide(friendly == nullptr ? L"Media Foundation H.264" : friendly);
    CoTaskMemFree(friendly);
    rs_encoder_capability_v1 capability{};
    capability.struct_size = sizeof(capability);
    capability.backend = backend;
    capability.codec = RS_CODEC_H264;
    capability.implementation_name_utf8 = string_view(name);
    capability.min_width = minimum_dimension;
    capability.min_height = minimum_dimension;
    capability.max_width = maximum_dimension;
    capability.max_height = maximum_dimension;
    capability.max_fps = 120;
    capability.max_bitrate_bps = maximum_bitrate;
    ComPtr<IMFTransform> transform;
    if (SUCCEEDED(activations[index]->ActivateObject(IID_PPV_ARGS(&transform)))) {
      ComPtr<ICodecAPI> codec;
      if (SUCCEEDED(transform.As(&codec)) &&
          SUCCEEDED(codec->IsSupported(&CODECAPI_AVEncCommonMeanBitRate))) {
        capability.supports_dynamic_rate = 1;
      }
      ComPtr<IMFAttributes> transform_attributes;
      UINT32 dynamic_format = FALSE;
      if (SUCCEEDED(transform->GetAttributes(&transform_attributes)) && transform_attributes != nullptr &&
          SUCCEEDED(transform_attributes->GetUINT32(MFT_SUPPORT_DYNAMIC_FORMAT_CHANGE, &dynamic_format))) {
        capability.supports_dynamic_resolution = dynamic_format != FALSE ? 1u : 0u;
      }
      activations[index]->ShutdownObject();
    }
    callback(context, &capability);
    activations[index]->Release();
  }
  CoTaskMemFree(activations);
}
}

extern "C" {
rs_status_v1 RS_CALL rs_runtime_enumerate_encoders(rs_runtime_handle runtime, rs_encoder_capability_callback_v1 callback, void* callback_context) {
  if (runtime == nullptr || callback == nullptr) return RS_STATUS_INVALID_ARGUMENT;
  enumerate_encoder_class(MFT_ENUM_FLAG_HARDWARE, RS_ENCODER_BACKEND_MEDIA_FOUNDATION_HARDWARE, callback, callback_context);
  enumerate_encoder_class(MFT_ENUM_FLAG_SYNCMFT | MFT_ENUM_FLAG_LOCALMFT,
      RS_ENCODER_BACKEND_MEDIA_FOUNDATION_SOFTWARE, callback, callback_context);
  return RS_STATUS_OK;
}

rs_status_v1 RS_CALL rs_encoder_create(rs_runtime_handle runtime, const rs_encoder_options_v1* options, rs_encoder_handle* out_encoder) {
  if (runtime == nullptr || options == nullptr || out_encoder == nullptr || options->struct_size < sizeof(rs_encoder_options_v1) ||
      !valid_dimensions(options->width, options->height) || options->target_fps == 0 || options->target_fps > 120 ||
      options->target_bitrate_bps < minimum_bitrate || options->target_bitrate_bps > options->max_bitrate_bps ||
      options->max_bitrate_bps > maximum_bitrate || options->codec != RS_CODEC_H264 ||
      options->frame_queue_capacity < 2 || options->frame_queue_capacity > 6 ||
      options->max_keyframe_interval_ms < 250 || options->max_keyframe_interval_ms > 10'000) return RS_STATUS_INVALID_ARGUMENT;
  *out_encoder = nullptr;
  auto encoder = std::make_unique<rs_encoder_t>();
  encoder->runtime = runtime;
  encoder->options = *options;
  const HRESULT result = select_encoder(encoder.get(), true);
  if (FAILED(result)) {
    std::string detail;
    {
      std::scoped_lock lock(runtime->error_mutex);
      detail = runtime->last_error;
    }
    if (detail.empty()) detail = hresult_detail("No Media Foundation H.264 encoder accepted the requested configuration.", result);
    set_last_error(runtime, RS_STATUS_NOT_SUPPORTED, "ENCODER_CONFIGURATION_UNSUPPORTED", detail);
    return RS_STATUS_NOT_SUPPORTED;
  }
  clear_error(runtime);
  *out_encoder = encoder.release();
  return RS_STATUS_OK;
}

rs_status_v1 RS_CALL rs_encoder_submit_d3d11_frame(rs_encoder_handle encoder, const rs_frame_info_v1* frame) {
  if (encoder == nullptr || frame == nullptr || frame->struct_size < sizeof(rs_frame_info_v1) || frame->d3d11_texture == nullptr) return RS_STATUS_INVALID_ARGUMENT;
  std::scoped_lock lock(encoder->mutex);
  if (frame->monotonic_timestamp_ns <= encoder->last_timestamp_ns) return RS_STATUS_INVALID_ARGUMENT;
  const char* failed_stage = "GPU color conversion";
  HRESULT result = convert_bgra_to_nv12_gpu(encoder, frame,
      encoder->backend == RS_ENCODER_BACKEND_MEDIA_FOUNDATION_SOFTWARE || !encoder->uses_dxgi_surface);
  if (SUCCEEDED(result)) {
    failed_stage = "Media Foundation encode";
    result = process_encoder_input(encoder, frame);
  }
  if (FAILED(result) && encoder->backend == RS_ENCODER_BACKEND_MEDIA_FOUNDATION_HARDWARE && encoder->options.allow_software_fallback != 0) {
    if (SUCCEEDED(select_encoder(encoder, false))) {
      notify_fallback(encoder, "ENCODER_HARDWARE_FAILED");
      failed_stage = "GPU color conversion after fallback";
      result = convert_bgra_to_nv12_gpu(encoder, frame, true);
      if (SUCCEEDED(result)) {
        failed_stage = "Media Foundation encode after fallback";
        result = process_encoder_input(encoder, frame);
      }
    }
  }
  if (FAILED(result)) {
    set_last_error(encoder->runtime, RS_STATUS_INTERNAL_ERROR, "ENCODER_HARDWARE_FAILED",
        hresult_detail(failed_stage, result));
    return RS_STATUS_INTERNAL_ERROR;
  }
  encoder->last_timestamp_ns = frame->monotonic_timestamp_ns;
  return RS_STATUS_OK;
}

rs_status_v1 RS_CALL rs_encoder_set_rate(rs_encoder_handle encoder, uint32_t target_bitrate_bps, uint32_t target_fps) {
  if (encoder == nullptr || target_bitrate_bps < minimum_bitrate || target_bitrate_bps > encoder->options.max_bitrate_bps || target_fps == 0 || target_fps > 120) return RS_STATUS_INVALID_ARGUMENT;
  std::scoped_lock lock(encoder->mutex);
  encoder->options.target_bitrate_bps = target_bitrate_bps;
  encoder->options.target_fps = target_fps;
  ComPtr<ICodecAPI> codec;
  if (FAILED(encoder->transform->QueryInterface(IID_PPV_ARGS(&codec))) ||
      FAILED(set_codec_uint(codec.Get(), CODECAPI_AVEncCommonMeanBitRate, target_bitrate_bps))) return RS_STATUS_NOT_SUPPORTED;
  return RS_STATUS_OK;
}

rs_status_v1 RS_CALL rs_encoder_reconfigure(rs_encoder_handle encoder, const rs_encoder_reconfigure_v1* options) {
  if (encoder == nullptr || options == nullptr || options->struct_size < sizeof(rs_encoder_reconfigure_v1) ||
      !valid_dimensions(options->width, options->height) || options->target_fps == 0 || options->target_fps > 120 ||
      options->target_bitrate_bps < minimum_bitrate || options->target_bitrate_bps > options->max_bitrate_bps ||
      options->max_bitrate_bps > maximum_bitrate) return RS_STATUS_INVALID_ARGUMENT;
  std::scoped_lock lock(encoder->mutex);
  encoder->options.width = options->width;
  encoder->options.height = options->height;
  encoder->options.target_fps = options->target_fps;
  encoder->options.target_bitrate_bps = options->target_bitrate_bps;
  encoder->options.max_bitrate_bps = options->max_bitrate_bps;
  encoder->options.quality_profile = options->quality_profile;
  encoder->readback.Reset();
  encoder->nv12_surface.Reset();
  encoder->video_input_surface.Reset();
  encoder->video_processor.Reset();
  encoder->video_enumerator.Reset();
  encoder->force_keyframe = true;
  return SUCCEEDED(select_encoder(encoder, true)) ? RS_STATUS_OK : RS_STATUS_NOT_SUPPORTED;
}

rs_status_v1 RS_CALL rs_encoder_request_keyframe(rs_encoder_handle encoder) {
  if (encoder == nullptr) return RS_STATUS_INVALID_ARGUMENT;
  std::scoped_lock lock(encoder->mutex);
  encoder->force_keyframe = true;
  return RS_STATUS_OK;
}

rs_status_v1 RS_CALL rs_encoder_flush(rs_encoder_handle encoder, uint32_t timeout_ms) {
  if (encoder == nullptr || timeout_ms > 30'000) return RS_STATUS_INVALID_ARGUMENT;
  std::scoped_lock lock(encoder->mutex);
  encoder->transform->ProcessMessage(MFT_MESSAGE_COMMAND_DRAIN, 0);
  encoder->transform->ProcessMessage(MFT_MESSAGE_COMMAND_FLUSH, 0);
  encoder->transform->ProcessMessage(MFT_MESSAGE_NOTIFY_START_OF_STREAM, 0);
  return RS_STATUS_OK;
}

void RS_CALL rs_encoder_destroy(rs_encoder_handle encoder) {
  if (encoder == nullptr) return;
  stop_transform(encoder->transform.Get());
  if (encoder->activation != nullptr) encoder->activation->ShutdownObject();
  delete encoder;
}

rs_status_v1 RS_CALL rs_decoder_create(rs_runtime_handle runtime, const rs_decoder_options_v1* options, rs_decoder_handle* out_decoder) {
  if (runtime == nullptr || options == nullptr || out_decoder == nullptr || options->struct_size < sizeof(rs_decoder_options_v1) ||
      options->codec != RS_CODEC_H264 || options->stream_format != RS_H264_STREAM_FORMAT_ANNEX_B ||
      !valid_dimensions(options->max_width, options->max_height) || options->output_queue_capacity < 2 || options->output_queue_capacity > 6) return RS_STATUS_INVALID_ARGUMENT;
  *out_decoder = nullptr;
  auto decoder = std::make_unique<rs_decoder_t>();
  decoder->runtime = runtime;
  decoder->options = *options;
  if (FAILED(configure_decoder(decoder.get()))) return RS_STATUS_NOT_SUPPORTED;
  *out_decoder = decoder.release();
  return RS_STATUS_OK;
}

rs_status_v1 RS_CALL rs_decoder_submit_h264(rs_decoder_handle decoder, const rs_encoded_frame_v1* frame) {
  if (decoder == nullptr || frame == nullptr || frame->struct_size < sizeof(rs_encoded_frame_v1) ||
      frame->codec != RS_CODEC_H264 || frame->stream_format != RS_H264_STREAM_FORMAT_ANNEX_B ||
      frame->bytes.data == nullptr || frame->bytes.length == 0 || frame->bytes.length > 16u * 1024u * 1024u) return RS_STATUS_INVALID_ARGUMENT;
  std::scoped_lock lock(decoder->mutex);
  if (decoder->awaiting_keyframe && frame->frame_kind != RS_FRAME_KIND_KEY) return RS_STATUS_INVALID_STATE;
  const HRESULT result = process_decoder_input(decoder, frame);
  if (FAILED(result)) return RS_STATUS_PROTOCOL_ERROR;
  decoder->awaiting_keyframe = false;
  return RS_STATUS_OK;
}

rs_status_v1 RS_CALL rs_decoder_reset(rs_decoder_handle decoder) {
  if (decoder == nullptr) return RS_STATUS_INVALID_ARGUMENT;
  std::scoped_lock lock(decoder->mutex);
  decoder->transform->ProcessMessage(MFT_MESSAGE_COMMAND_FLUSH, 0);
  decoder->transform->ProcessMessage(MFT_MESSAGE_NOTIFY_START_OF_STREAM, 0);
  decoder->awaiting_keyframe = true;
  return RS_STATUS_OK;
}

rs_status_v1 RS_CALL rs_decoder_flush(rs_decoder_handle decoder, uint32_t timeout_ms) {
  if (decoder == nullptr || timeout_ms > 30'000) return RS_STATUS_INVALID_ARGUMENT;
  std::scoped_lock lock(decoder->mutex);
  decoder->transform->ProcessMessage(MFT_MESSAGE_COMMAND_DRAIN, 0);
  return RS_STATUS_OK;
}

void RS_CALL rs_decoder_destroy(rs_decoder_handle decoder) {
  if (decoder == nullptr) return;
  stop_transform(decoder->transform.Get());
  delete decoder;
}
}
