#pragma once

#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#include <d3d10.h>
#include <d3d11.h>
#include <d3d11_1.h>
#include <d2d1_1.h>
#include <dxgi1_2.h>
#include <shellscalingapi.h>
#include <wrl/client.h>
#include <codecapi.h>
#include <icodecapi.h>
#include <mfapi.h>
#include <mferror.h>
#include <mfidl.h>
#include <rtc/rtc.h>

#include "remote_support_native.h"

#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cmath>
#include <condition_variable>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <deque>
#include <functional>
#include <map>
#include <mutex>
#include <optional>
#include <set>
#include <string>
#include <thread>
#include <unordered_map>
#include <utility>
#include <vector>

using Microsoft::WRL::ComPtr;

struct display_record {
  std::string id;
  std::string name;
  std::wstring gdi_name;
  HMONITOR monitor{};
  RECT bounds{};
  LUID adapter_luid{};
  uint32_t rotation_degrees{};
  uint32_t dpi_x{96};
  uint32_t dpi_y{96};
  uint32_t flags{};
};

struct rs_runtime_t {
  ComPtr<ID3D11Device> device;
  ComPtr<ID3D11DeviceContext> context;
  rs_callbacks_v1 callbacks{};
  std::mutex error_mutex;
  std::string last_error;
  std::mutex topology_mutex;
  std::string topology_signature;
  std::atomic<uint64_t> topology_generation{1};
  bool media_foundation_started{};
  bool com_initialized_by_runtime{};
  DWORD owner_thread_id{};
};

struct rs_capture_t {
  rs_runtime_t* runtime{};
  rs_capture_options_v1 options{};
  std::string target_id;
  std::mutex state_mutex;
  std::jthread worker;
  bool running{};
  uint64_t next_frame_id{1};
  uint64_t cursor_shape_id{};
};

struct cached_cursor {
  rs_cursor_info_v1 info{};
  std::vector<uint8_t> pixels;
};

struct rs_renderer_t {
  rs_runtime_t* runtime{};
  std::mutex mutex;
  HWND hwnd{};
  rs_renderer_view_mode_v1 view_mode{RS_RENDERER_VIEW_FIT};
  rs_renderer_transform_v1 transform{sizeof(rs_renderer_transform_v1), 1.0f, 0.0f, 0.0f, 0};
  uint32_t pixel_width{};
  uint32_t pixel_height{};
  ComPtr<IDXGISwapChain1> swap_chain;
  ComPtr<ID2D1Factory1> d2d_factory;
  ComPtr<ID2D1Device> d2d_device;
  ComPtr<ID2D1DeviceContext> d2d_context;
  ComPtr<ID2D1Bitmap1> d2d_target;
  cached_cursor cursor;
};

struct rs_encoder_t {
  rs_runtime_t* runtime{};
  std::mutex mutex;
  rs_encoder_options_v1 options{};
  ComPtr<IMFTransform> transform;
  ComPtr<IMFActivate> activation;
  ComPtr<IMFDXGIDeviceManager> dxgi_device_manager;
  UINT dxgi_reset_token{};
  ComPtr<ID3D11VideoDevice> video_device;
  ComPtr<ID3D11VideoContext> video_context;
  ComPtr<ID3D11VideoProcessorEnumerator> video_enumerator;
  ComPtr<ID3D11VideoProcessor> video_processor;
  ComPtr<ID3D11Texture2D> video_input_surface;
  ComPtr<ID3D11Texture2D> nv12_surface;
  ComPtr<ID3D11Texture2D> readback;
  uint32_t converter_source_width{};
  uint32_t converter_source_height{};
  uint32_t converter_output_width{};
  uint32_t converter_output_height{};
  bool converter_video_encoder_binding{};
  std::vector<uint8_t> nv12;
  std::vector<uint8_t> encoded;
  rs_encoder_backend_v1 backend{RS_ENCODER_BACKEND_UNKNOWN};
  bool asynchronous{};
  bool uses_dxgi_surface{};
  bool force_keyframe{true};
  bool force_keyframe_submitted{};
  bool suppress_output{};
  uint64_t output_count{};
  uint64_t last_timestamp_ns{};
  void (RS_CALL *output_callback)(void*, const rs_encoded_frame_v1*){};
  void* output_callback_context{};
};

struct rs_decoder_t {
  rs_runtime_t* runtime{};
  std::mutex mutex;
  rs_decoder_options_v1 options{};
  ComPtr<IMFTransform> transform;
  ComPtr<ID3D11Texture2D> output_texture;
  std::vector<uint8_t> bgra;
  uint32_t width{};
  uint32_t height{};
  bool awaiting_keyframe{true};
  bool remote_transport_output{};
};

struct transport_channel {
  uint32_t product_id{};
  int rtc_id{-1};
  std::string label;
  bool ordered{true};
  int32_t max_retransmits{-1};
  bool open{};
  bool internal_control{};
  uint64_t last_incoming_sequence{};
  uint64_t last_outgoing_sequence{};
};

struct transport_binding_material {
  std::string remote_peer_id;
  rs_peer_role_v1 local_role{RS_PEER_ROLE_UNKNOWN};
  rs_peer_role_v1 remote_role{RS_PEER_ROLE_UNKNOWN};
  uint64_t permission_revision{};
  std::vector<std::string> scopes;
  std::array<uint8_t, 32> authorization_context{};
  std::array<uint8_t, 32> local_private_key{};
  std::array<uint8_t, 65> local_public_key{};
  std::array<uint8_t, 65> remote_public_key{};
  std::string local_key_id;
  std::string remote_key_id;
  bool private_key_locked{};
};

struct rs_transport_t {
  rs_runtime_t* runtime{};
  std::mutex mutex;
  std::condition_variable state_changed;
  std::string session_id;
  std::string local_peer_id;
  uint64_t transport_epoch{};
  rs_video_input_mode_v1 video_input_mode{RS_VIDEO_INPUT_MODE_UNKNOWN};
  uint32_t max_data_message_bytes{};
  uint32_t buffered_amount_low_threshold_bytes{};
  uint32_t flags{};
  transport_binding_material binding;
  int peer_connection{-1};
  int video_track{-1};
  std::vector<int> additional_track_ids;
  rs_encoder_handle internal_encoder{};
  rs_decoder_handle internal_decoder{};
  std::unordered_map<uint32_t, transport_channel> channels;
  std::unordered_map<int, uint32_t> channel_by_rtc_id;
  uint32_t next_channel_id{1};
  uint32_t control_channel_id{RS_DATA_CHANNEL_ID_INVALID};
  rs_transport_state_v1 state{RS_TRANSPORT_STATE_NEW};
  bool closing{};
  bool local_description_set{};
  bool remote_description_set{};
  std::string local_description;
  std::string remote_description;
  std::array<uint8_t, 32> local_dtls_fingerprint{};
  std::array<uint8_t, 32> remote_dtls_fingerprint{};
  bool have_local_fingerprint{};
  bool have_remote_fingerprint{};
  std::string local_binding_id;
  std::array<uint8_t, 32> local_binding_hash{};
  bool local_binding_sent{};
  bool local_binding_acked{};
  bool remote_binding_verified{};
  bool binding_complete_notified{};
  bool protocol_hello_sent{};
  bool protocol_hello_received{};
  bool protocol_hello_acked{};
  bool content_ready{};
  uint64_t control_outgoing_sequence{};
  uint64_t control_incoming_sequence{};
  std::set<std::string> seen_remote_binding_ids;
  std::jthread heartbeat_worker;
  std::atomic<uint64_t> bytes_sent{};
  std::atomic<uint64_t> bytes_received{};
  std::atomic<uint64_t> video_bytes_sent{};
  std::atomic<uint64_t> video_frames_received{};
  std::atomic<uint32_t> heartbeat_rtt_ms{};
  std::atomic<uint32_t> target_bitrate_bps{4'000'000};
  std::atomic<uint32_t> target_fps{30};
  std::atomic<rs_route_class_v1> route_class{RS_ROUTE_CLASS_UNKNOWN};
  rs_route_class_v1 configured_relay_route{RS_ROUTE_CLASS_TURN_UDP};
  uint64_t created_ns{};
  uint64_t last_heartbeat_nonce{};
  uint64_t last_heartbeat_sent_ns{};
  std::vector<uint8_t> incoming_h264_access_unit;
  uint32_t incoming_h264_timestamp{};
  uint16_t incoming_h264_expected_sequence{};
  bool incoming_h264_have_sequence{};
  bool incoming_h264_key_frame{};
  uint64_t next_remote_frame_id{1};
};
struct rs_input_injector_t {
  rs_runtime_t* runtime{};
  std::mutex mutex;
  uint64_t expected_display_generation{};
  uint32_t permission_flags{};
  bool enabled{true};
  uint64_t last_input_sequence{};
  std::set<uint32_t> pressed_keys;
  std::set<rs_pointer_button_v1> pressed_buttons;
};

std::string utf8_from_wide(const wchar_t* value);
std::wstring wide_from_utf8(rs_string_view_v1 value);
uint64_t monotonic_nanoseconds();
rs_string_view_v1 string_view(const std::string& value);
void set_last_error(rs_runtime_t* runtime, rs_status_v1 status, const char* stable_code, const std::string& detail);
void emit_cursor_metadata(rs_capture_t* capture, uint64_t frame_id, uint64_t generation);
std::vector<display_record> enumerate_displays(rs_runtime_t* runtime, bool update_generation);
std::optional<display_record> find_display(rs_runtime_t* runtime, const std::string& id);
rs_status_v1 run_wgc_capture(rs_capture_t* capture, std::stop_token stop);
HRESULT convert_bgra_to_nv12_gpu(rs_encoder_t* encoder, const rs_frame_info_v1* frame, bool copy_to_system_memory);
bool generate_p256_key_pair(rs_peer_key_pair_v1* output);
bool sha256_bytes(const uint8_t* data, size_t length, std::array<uint8_t, 32>& output);
bool sign_p256_sha256(const transport_binding_material& material, const std::array<uint8_t, 32>& hash,
    std::array<uint8_t, 64>& signature);
bool verify_p256_sha256(const std::array<uint8_t, 65>& public_key, const std::array<uint8_t, 32>& hash,
    const uint8_t* signature, size_t signature_length);

inline bool struct_has(uint32_t actual_size, size_t field_offset, size_t field_size) {
  return actual_size >= field_offset + field_size;
}

template <typename T>
T copy_prefix(const T* source) {
  T destination{};
  if (source != nullptr) {
    std::memcpy(&destination, source, (std::min)(static_cast<size_t>(source->struct_size), sizeof(T)));
  }
  return destination;
}
