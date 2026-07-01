#ifndef REMOTE_SUPPORT_NATIVE_H
#define REMOTE_SUPPORT_NATIVE_H

#include <stddef.h>
#include <stdint.h>

#if defined(_WIN32)
  #if defined(RS_NATIVE_EXPORTS)
    #define RS_API __declspec(dllexport)
  #else
    #define RS_API __declspec(dllimport)
  #endif
  #define RS_CALL __cdecl
#else
  #define RS_API
  #define RS_CALL
#endif

#ifdef __cplusplus
extern "C" {
#endif

#define RS_NATIVE_ABI_MAJOR 1u
#define RS_NATIVE_ABI_MINOR 2u
#define RS_DATA_CHANNEL_ID_INVALID 0xffffffffu

typedef struct rs_runtime_t* rs_runtime_handle;
typedef struct rs_capture_t* rs_capture_handle;
typedef struct rs_encoder_t* rs_encoder_handle;
typedef struct rs_decoder_t* rs_decoder_handle;
typedef struct rs_renderer_t* rs_renderer_handle;
typedef struct rs_transport_t* rs_transport_handle;
typedef struct rs_input_injector_t* rs_input_injector_handle;

typedef enum rs_status_v1 {
  RS_STATUS_OK = 0,
  RS_STATUS_INVALID_ARGUMENT = 1,
  RS_STATUS_NOT_INITIALIZED = 2,
  RS_STATUS_ALREADY_INITIALIZED = 3,
  RS_STATUS_NOT_SUPPORTED = 4,
  RS_STATUS_ACCESS_DENIED = 5,
  RS_STATUS_TIMEOUT = 6,
  RS_STATUS_CANCELLED = 7,
  RS_STATUS_RESOURCE_EXHAUSTED = 8,
  RS_STATUS_DEVICE_LOST = 9,
  RS_STATUS_PROTOCOL_ERROR = 10,
  RS_STATUS_INVALID_STATE = 11,
  RS_STATUS_WOULD_BLOCK = 12,
  RS_STATUS_CLOSED = 13,
  RS_STATUS_BUFFER_TOO_SMALL = 14,
  RS_STATUS_INTERNAL_ERROR = 1000
} rs_status_v1;

typedef enum rs_pixel_format_v1 {
  RS_PIXEL_FORMAT_UNKNOWN = 0,
  RS_PIXEL_FORMAT_BGRA8 = 1,
  RS_PIXEL_FORMAT_NV12 = 2,
  RS_PIXEL_FORMAT_P010 = 3
} rs_pixel_format_v1;

typedef enum rs_frame_kind_v1 {
  RS_FRAME_KIND_UNKNOWN = 0,
  RS_FRAME_KIND_KEY = 1,
  RS_FRAME_KIND_DELTA = 2
} rs_frame_kind_v1;

typedef enum rs_codec_v1 {
  RS_CODEC_UNKNOWN = 0,
  RS_CODEC_H264 = 1
} rs_codec_v1;

typedef enum rs_capture_source_v1 {
  RS_CAPTURE_SOURCE_UNKNOWN = 0,
  RS_CAPTURE_SOURCE_AUTO = 1,
  RS_CAPTURE_SOURCE_DXGI = 2,
  RS_CAPTURE_SOURCE_WGC = 3,
  RS_CAPTURE_SOURCE_SYNTHETIC = 4
} rs_capture_source_v1;

typedef enum rs_capture_target_kind_v1 {
  RS_CAPTURE_TARGET_UNKNOWN = 0,
  RS_CAPTURE_TARGET_DISPLAY = 1,
  RS_CAPTURE_TARGET_WINDOW = 2,
  RS_CAPTURE_TARGET_SYNTHETIC = 3
} rs_capture_target_kind_v1;

typedef enum rs_quality_profile_v1 {
  RS_QUALITY_PROFILE_UNKNOWN = 0,
  RS_QUALITY_PROFILE_TEXT = 1,
  RS_QUALITY_PROFILE_BALANCED = 2,
  RS_QUALITY_PROFILE_MOTION = 3
} rs_quality_profile_v1;

typedef enum rs_encoder_backend_v1 {
  RS_ENCODER_BACKEND_UNKNOWN = 0,
  RS_ENCODER_BACKEND_MEDIA_FOUNDATION_HARDWARE = 1,
  RS_ENCODER_BACKEND_MEDIA_FOUNDATION_SOFTWARE = 2
} rs_encoder_backend_v1;

typedef enum rs_h264_stream_format_v1 {
  RS_H264_STREAM_FORMAT_UNKNOWN = 0,
  RS_H264_STREAM_FORMAT_ANNEX_B = 1
} rs_h264_stream_format_v1;

typedef enum rs_cursor_shape_v1 {
  RS_CURSOR_SHAPE_UNKNOWN = 0,
  RS_CURSOR_SHAPE_MONOCHROME = 1,
  RS_CURSOR_SHAPE_COLOR = 2,
  RS_CURSOR_SHAPE_MASKED_COLOR = 3
} rs_cursor_shape_v1;

typedef enum rs_sdp_type_v1 {
  RS_SDP_TYPE_UNKNOWN = 0,
  RS_SDP_TYPE_OFFER = 1,
  RS_SDP_TYPE_ANSWER = 2,
  RS_SDP_TYPE_ROLLBACK = 3
} rs_sdp_type_v1;

typedef enum rs_transport_state_v1 {
  RS_TRANSPORT_STATE_NEW = 0,
  RS_TRANSPORT_STATE_CONNECTING = 1,
  RS_TRANSPORT_STATE_CONNECTED = 2,
  RS_TRANSPORT_STATE_DISCONNECTED = 3,
  RS_TRANSPORT_STATE_FAILED = 4,
  RS_TRANSPORT_STATE_CLOSED = 5
} rs_transport_state_v1;

typedef enum rs_peer_role_v1 {
  RS_PEER_ROLE_UNKNOWN = 0,
  RS_PEER_ROLE_HOST = 1,
  RS_PEER_ROLE_OPERATOR = 2
} rs_peer_role_v1;

typedef enum rs_transport_binding_state_v1 {
  RS_TRANSPORT_BINDING_NOT_STARTED = 0,
  RS_TRANSPORT_BINDING_LOCAL_SENT = 1,
  RS_TRANSPORT_BINDING_REMOTE_VERIFIED = 2,
  RS_TRANSPORT_BINDING_VERIFIED = 3,
  RS_TRANSPORT_BINDING_FAILED = 4
} rs_transport_binding_state_v1;

typedef enum rs_route_class_v1 {
  RS_ROUTE_CLASS_UNKNOWN = 0,
  RS_ROUTE_CLASS_DIRECT_UDP = 1,
  RS_ROUTE_CLASS_DIRECT_TCP = 2,
  RS_ROUTE_CLASS_TURN_UDP = 3,
  RS_ROUTE_CLASS_TURN_TCP = 4,
  RS_ROUTE_CLASS_TURN_TLS = 5
} rs_route_class_v1;

typedef enum rs_video_input_mode_v1 {
  RS_VIDEO_INPUT_MODE_UNKNOWN = 0,
  RS_VIDEO_INPUT_MODE_ENCODED_H264 = 1,
  RS_VIDEO_INPUT_MODE_D3D11_TEXTURE = 2
} rs_video_input_mode_v1;

typedef enum rs_data_channel_state_v1 {
  RS_DATA_CHANNEL_STATE_CONNECTING = 0,
  RS_DATA_CHANNEL_STATE_OPEN = 1,
  RS_DATA_CHANNEL_STATE_CLOSING = 2,
  RS_DATA_CHANNEL_STATE_CLOSED = 3
} rs_data_channel_state_v1;

typedef enum rs_renderer_view_mode_v1 {
  RS_RENDERER_VIEW_FIT = 0,
  RS_RENDERER_VIEW_ACTUAL_SIZE = 1,
  RS_RENDERER_VIEW_STRETCH = 2
} rs_renderer_view_mode_v1;

typedef enum rs_pointer_kind_v1 {
  RS_POINTER_MOVE = 0,
  RS_POINTER_BUTTON_DOWN = 1,
  RS_POINTER_BUTTON_UP = 2,
  RS_POINTER_WHEEL = 3,
  RS_POINTER_HORIZONTAL_WHEEL = 4
} rs_pointer_kind_v1;

typedef enum rs_pointer_button_v1 {
  RS_POINTER_BUTTON_NONE = 0,
  RS_POINTER_BUTTON_LEFT = 1,
  RS_POINTER_BUTTON_RIGHT = 2,
  RS_POINTER_BUTTON_MIDDLE = 3,
  RS_POINTER_BUTTON_X1 = 4,
  RS_POINTER_BUTTON_X2 = 5
} rs_pointer_button_v1;

typedef enum rs_keyboard_kind_v1 {
  RS_KEYBOARD_KEY_DOWN = 0,
  RS_KEYBOARD_KEY_UP = 1,
  RS_KEYBOARD_UNICODE_TEXT = 2
} rs_keyboard_kind_v1;

typedef struct rs_string_view_v1 {
  const char* data;
  uint32_t length;
} rs_string_view_v1;

typedef struct rs_byte_view_v1 {
  const uint8_t* data;
  uint32_t length;
} rs_byte_view_v1;

typedef struct rs_runtime_options_v1 {
  uint32_t struct_size;
  uint32_t requested_abi_major;
  uint32_t requested_abi_minor;
  uint32_t flags;
  void* user_context;
} rs_runtime_options_v1;

typedef struct rs_capture_options_v1 {
  uint32_t struct_size;
  uint32_t target_fps;
  uint32_t max_width;
  uint32_t max_height;
  uint32_t flags;
  rs_string_view_v1 display_id_utf8;
  rs_capture_source_v1 source;
  rs_capture_target_kind_v1 target_kind;
  uint32_t frame_queue_capacity;
  uint32_t acquire_timeout_ms;
} rs_capture_options_v1;

typedef struct rs_display_info_v1 {
  uint32_t struct_size;
  rs_string_view_v1 display_id_utf8;
  rs_string_view_v1 device_name_utf8;
  int32_t desktop_x;
  int32_t desktop_y;
  uint32_t width;
  uint32_t height;
  uint32_t rotation_degrees;
  uint32_t dpi_x;
  uint32_t dpi_y;
  uint32_t adapter_luid_low;
  int32_t adapter_luid_high;
  uint64_t display_generation;
  uint32_t flags;
} rs_display_info_v1;

typedef struct rs_cursor_info_v1 {
  uint32_t struct_size;
  uint64_t frame_id;
  uint64_t display_generation;
  uint64_t shape_id;
  int32_t desktop_x;
  int32_t desktop_y;
  int32_t hotspot_x;
  int32_t hotspot_y;
  uint32_t visible;
  rs_cursor_shape_v1 shape_kind;
  uint32_t width;
  uint32_t height;
  uint32_t pitch_bytes;
  rs_byte_view_v1 bgra8_premultiplied;
} rs_cursor_info_v1;

typedef struct rs_frame_info_v1 {
  uint32_t struct_size;
  uint64_t frame_id;
  uint64_t monotonic_timestamp_ns;
  uint32_t width;
  uint32_t height;
  uint32_t rotation_degrees;
  rs_pixel_format_v1 pixel_format;
  uint64_t display_generation;
  uint32_t color_primaries;
  uint32_t transfer_characteristics;
  uint32_t matrix_coefficients;
  void* d3d11_texture; /* Borrowed ID3D11Texture2D*. Valid only during callback/call. */
  int32_t desktop_origin_x;
  int32_t desktop_origin_y;
} rs_frame_info_v1;

typedef struct rs_encoder_options_v1 {
  uint32_t struct_size;
  uint32_t width;
  uint32_t height;
  uint32_t target_fps;
  uint32_t target_bitrate_bps;
  uint32_t max_bitrate_bps;
  rs_codec_v1 codec;
  uint32_t flags;
  rs_quality_profile_v1 quality_profile;
  uint32_t frame_queue_capacity;
  uint32_t prefer_hardware;
  uint32_t allow_software_fallback;
  uint32_t max_keyframe_interval_ms;
} rs_encoder_options_v1;

typedef struct rs_encoder_reconfigure_v1 {
  uint32_t struct_size;
  uint32_t width;
  uint32_t height;
  uint32_t target_fps;
  uint32_t target_bitrate_bps;
  uint32_t max_bitrate_bps;
  rs_quality_profile_v1 quality_profile;
  uint32_t flags;
} rs_encoder_reconfigure_v1;

typedef struct rs_encoder_capability_v1 {
  uint32_t struct_size;
  rs_encoder_backend_v1 backend;
  rs_codec_v1 codec;
  rs_string_view_v1 implementation_name_utf8;
  uint32_t min_width;
  uint32_t min_height;
  uint32_t max_width;
  uint32_t max_height;
  uint32_t max_fps;
  uint32_t max_bitrate_bps;
  uint32_t supports_dynamic_rate;
  uint32_t supports_dynamic_resolution;
  uint32_t adapter_luid_low;
  int32_t adapter_luid_high;
  uint32_t flags;
} rs_encoder_capability_v1;

typedef struct rs_encoded_frame_v1 {
  uint32_t struct_size;
  uint64_t frame_id;
  uint64_t rtp_timestamp_90khz;
  uint64_t monotonic_timestamp_ns;
  rs_frame_kind_v1 frame_kind;
  rs_codec_v1 codec;
  rs_byte_view_v1 bytes; /* Borrowed until callback/call returns. */
  int32_t qp;
  uint32_t width;
  uint32_t height;
  rs_h264_stream_format_v1 stream_format;
  uint32_t h264_profile_idc;
  uint32_t h264_level_idc;
} rs_encoded_frame_v1;

typedef struct rs_decoder_options_v1 {
  uint32_t struct_size;
  rs_codec_v1 codec;
  rs_h264_stream_format_v1 stream_format;
  uint32_t max_width;
  uint32_t max_height;
  uint32_t output_queue_capacity;
  uint32_t prefer_hardware;
  uint32_t allow_software_fallback;
  uint32_t flags;
} rs_decoder_options_v1;

typedef struct rs_renderer_options_v1 {
  uint32_t struct_size;
  uintptr_t target_hwnd;
  rs_renderer_view_mode_v1 view_mode;
  uint32_t flags;
} rs_renderer_options_v1;

typedef struct rs_renderer_transform_v1 {
  uint32_t struct_size;
  float zoom;
  float pan_source_x;
  float pan_source_y;
  uint32_t flags;
} rs_renderer_transform_v1;

typedef struct rs_ice_server_v1 {
  uint32_t struct_size;
  const rs_string_view_v1* urls;
  uint32_t url_count;
  rs_string_view_v1 username_utf8;
  rs_string_view_v1 credential_utf8;
} rs_ice_server_v1;

typedef struct rs_peer_key_pair_v1 {
  uint32_t struct_size;
  uint8_t private_key_p256[32];
  uint8_t public_key_uncompressed_p256[65];
} rs_peer_key_pair_v1;

typedef struct rs_transport_binding_options_v1 {
  uint32_t struct_size;
  rs_string_view_v1 remote_peer_id_utf8;
  rs_peer_role_v1 local_role;
  rs_peer_role_v1 remote_role;
  uint64_t permission_revision;
  const rs_string_view_v1* granted_scopes_utf8;
  uint32_t granted_scope_count;
  rs_byte_view_v1 authorization_context_sha256; /* Exactly 32 bytes. */
  rs_byte_view_v1 local_private_key_p256; /* Exactly 32 bytes; copied into locked process memory. */
  rs_byte_view_v1 local_public_key_uncompressed_p256; /* 0x04 || X || Y, exactly 65 bytes. */
  rs_byte_view_v1 remote_public_key_uncompressed_p256; /* 0x04 || X || Y, exactly 65 bytes. */
  rs_string_view_v1 local_key_id_utf8;
  rs_string_view_v1 remote_key_id_utf8;
} rs_transport_binding_options_v1;

typedef struct rs_transport_options_v1 {
  uint32_t struct_size;
  rs_string_view_v1 session_id_utf8;
  rs_string_view_v1 local_peer_id_utf8;
  uint64_t transport_epoch;
  rs_video_input_mode_v1 video_input_mode;
  const rs_ice_server_v1* ice_servers;
  uint32_t ice_server_count;
  uint32_t max_data_message_bytes;
  uint32_t buffered_amount_low_threshold_bytes;
  uint32_t flags;
  const rs_transport_binding_options_v1* binding;
} rs_transport_options_v1;

typedef struct rs_session_description_v1 {
  uint32_t struct_size;
  rs_sdp_type_v1 type;
  rs_string_view_v1 sdp_utf8;
} rs_session_description_v1;

typedef struct rs_ice_candidate_v1 {
  uint32_t struct_size;
  rs_string_view_v1 sdp_mid_utf8;
  int32_t sdp_mline_index;
  rs_string_view_v1 candidate_utf8;
} rs_ice_candidate_v1;

typedef struct rs_data_channel_options_v1 {
  uint32_t struct_size;
  rs_string_view_v1 label_utf8;
  uint32_t ordered;
  int32_t max_retransmits; /* -1 means reliable. */
  int32_t max_packet_lifetime_ms; /* -1 means unset. Do not set both partial-reliability fields. */
  uint32_t negotiated;
  int32_t negotiated_id; /* -1 when not externally negotiated. */
  uint32_t flags;
} rs_data_channel_options_v1;

typedef struct rs_data_message_v1 {
  uint32_t struct_size;
  uint32_t channel_id;
  uint32_t binary;
  rs_byte_view_v1 payload;
} rs_data_message_v1;

typedef struct rs_transport_stats_v1 {
  uint32_t struct_size;
  uint32_t rtt_ms;
  uint32_t jitter_ms;
  uint32_t packet_loss_permyriad;
  uint64_t available_outgoing_bitrate_bps;
  uint64_t actual_video_bitrate_bps;
  uint64_t bytes_sent;
  uint64_t bytes_received;
  uint64_t data_channel_buffered_bytes;
  rs_route_class_v1 route_class;
} rs_transport_stats_v1;

typedef struct rs_input_options_v1 {
  uint32_t struct_size;
  uint64_t expected_display_generation;
  uint32_t flags;
} rs_input_options_v1;

typedef struct rs_pointer_input_v1 {
  uint32_t struct_size;
  rs_pointer_kind_v1 kind;
  int32_t desktop_x;
  int32_t desktop_y;
  rs_pointer_button_v1 button;
  int32_t wheel_delta;
  uint64_t display_generation;
  uint64_t input_sequence;
} rs_pointer_input_v1;

typedef struct rs_keyboard_input_v1 {
  uint32_t struct_size;
  rs_keyboard_kind_v1 kind;
  uint32_t scan_code;
  uint32_t virtual_key;
  uint32_t extended;
  uint32_t repeat;
  rs_string_view_v1 unicode_text_utf8;
  uint64_t input_sequence;
  uint32_t keyboard_layout_id;
} rs_keyboard_input_v1;

typedef void (RS_CALL *rs_log_callback_v1)(void* user_context, uint32_t level, rs_string_view_v1 component, rs_string_view_v1 message);
typedef void (RS_CALL *rs_frame_callback_v1)(void* user_context, const rs_frame_info_v1* frame);
typedef void (RS_CALL *rs_display_info_callback_v1)(void* user_context, const rs_display_info_v1* display);
typedef void (RS_CALL *rs_cursor_callback_v1)(void* user_context, const rs_cursor_info_v1* cursor);
typedef void (RS_CALL *rs_encoded_frame_callback_v1)(void* user_context, const rs_encoded_frame_v1* frame);
typedef void (RS_CALL *rs_encoder_capability_callback_v1)(void* user_context, const rs_encoder_capability_v1* capability);
typedef void (RS_CALL *rs_encoder_fallback_callback_v1)(void* user_context, rs_encoder_backend_v1 failed_backend, rs_encoder_backend_v1 selected_backend, rs_string_view_v1 stable_reason);
typedef void (RS_CALL *rs_error_callback_v1)(void* user_context, rs_status_v1 status, rs_string_view_v1 stable_code);
typedef void (RS_CALL *rs_transport_state_callback_v1)(void* user_context, rs_transport_state_v1 state, rs_string_view_v1 stable_reason);
typedef void (RS_CALL *rs_local_description_callback_v1)(void* user_context, const rs_session_description_v1* description);
typedef void (RS_CALL *rs_local_ice_candidate_callback_v1)(void* user_context, const rs_ice_candidate_v1* candidate);
typedef void (RS_CALL *rs_data_channel_state_callback_v1)(void* user_context, uint32_t channel_id, rs_string_view_v1 label, rs_data_channel_state_v1 state);
typedef void (RS_CALL *rs_data_message_callback_v1)(void* user_context, const rs_data_message_v1* message);
typedef void (RS_CALL *rs_buffered_amount_low_callback_v1)(void* user_context, uint32_t channel_id, uint64_t buffered_amount_bytes);
typedef void (RS_CALL *rs_transport_binding_callback_v1)(void* user_context, rs_transport_binding_state_v1 state, rs_string_view_v1 stable_reason);
typedef void (RS_CALL *rs_transport_video_feedback_callback_v1)(void* user_context, uint32_t target_bitrate_bps, uint32_t target_fps, uint32_t request_keyframe);

typedef struct rs_callbacks_v1 {
  uint32_t struct_size;
  void* user_context;
  rs_log_callback_v1 on_log;
  rs_frame_callback_v1 on_capture_frame;
  rs_encoded_frame_callback_v1 on_encoded_frame;
  rs_frame_callback_v1 on_remote_video_frame;
  rs_error_callback_v1 on_error;
  rs_transport_state_callback_v1 on_transport_state;
  rs_local_description_callback_v1 on_local_description;
  rs_local_ice_candidate_callback_v1 on_local_ice_candidate;
  rs_data_channel_state_callback_v1 on_data_channel_state;
  rs_data_message_callback_v1 on_data_message;
  rs_buffered_amount_low_callback_v1 on_buffered_amount_low;
  rs_cursor_callback_v1 on_cursor;
  rs_frame_callback_v1 on_decoded_frame;
  rs_encoder_fallback_callback_v1 on_encoder_fallback;
  rs_transport_binding_callback_v1 on_transport_binding_state;
  rs_transport_video_feedback_callback_v1 on_transport_video_feedback;
} rs_callbacks_v1;

RS_API uint32_t RS_CALL rs_native_get_abi_major(void);
RS_API uint32_t RS_CALL rs_native_get_abi_minor(void);
RS_API rs_string_view_v1 RS_CALL rs_native_get_build_id(void);

RS_API rs_status_v1 RS_CALL rs_runtime_create(const rs_runtime_options_v1* options, const rs_callbacks_v1* callbacks, rs_runtime_handle* out_runtime);
RS_API void RS_CALL rs_runtime_destroy(rs_runtime_handle runtime);
RS_API rs_status_v1 RS_CALL rs_runtime_get_last_error(rs_runtime_handle runtime, char* utf8_buffer, uint32_t buffer_capacity, uint32_t* out_required_length);
RS_API rs_status_v1 RS_CALL rs_runtime_enumerate_displays(rs_runtime_handle runtime, rs_display_info_callback_v1 callback, void* callback_context);
RS_API rs_status_v1 RS_CALL rs_runtime_enumerate_encoders(rs_runtime_handle runtime, rs_encoder_capability_callback_v1 callback, void* callback_context);
RS_API rs_status_v1 RS_CALL rs_runtime_generate_peer_key_pair(rs_runtime_handle runtime, rs_peer_key_pair_v1* out_key_pair);

RS_API rs_status_v1 RS_CALL rs_capture_create(rs_runtime_handle runtime, const rs_capture_options_v1* options, rs_capture_handle* out_capture);
RS_API rs_status_v1 RS_CALL rs_capture_start(rs_capture_handle capture);
RS_API rs_status_v1 RS_CALL rs_capture_stop(rs_capture_handle capture);
RS_API rs_status_v1 RS_CALL rs_capture_set_target(rs_capture_handle capture, rs_string_view_v1 display_id_utf8);
RS_API void RS_CALL rs_capture_destroy(rs_capture_handle capture);

RS_API rs_status_v1 RS_CALL rs_encoder_create(rs_runtime_handle runtime, const rs_encoder_options_v1* options, rs_encoder_handle* out_encoder);
RS_API rs_status_v1 RS_CALL rs_encoder_submit_d3d11_frame(rs_encoder_handle encoder, const rs_frame_info_v1* frame);
RS_API rs_status_v1 RS_CALL rs_encoder_set_rate(rs_encoder_handle encoder, uint32_t target_bitrate_bps, uint32_t target_fps);
RS_API rs_status_v1 RS_CALL rs_encoder_reconfigure(rs_encoder_handle encoder, const rs_encoder_reconfigure_v1* options);
RS_API rs_status_v1 RS_CALL rs_encoder_request_keyframe(rs_encoder_handle encoder);
RS_API rs_status_v1 RS_CALL rs_encoder_flush(rs_encoder_handle encoder, uint32_t timeout_ms);
RS_API void RS_CALL rs_encoder_destroy(rs_encoder_handle encoder);

RS_API rs_status_v1 RS_CALL rs_decoder_create(rs_runtime_handle runtime, const rs_decoder_options_v1* options, rs_decoder_handle* out_decoder);
RS_API rs_status_v1 RS_CALL rs_decoder_submit_h264(rs_decoder_handle decoder, const rs_encoded_frame_v1* frame);
RS_API rs_status_v1 RS_CALL rs_decoder_reset(rs_decoder_handle decoder);
RS_API rs_status_v1 RS_CALL rs_decoder_flush(rs_decoder_handle decoder, uint32_t timeout_ms);
RS_API void RS_CALL rs_decoder_destroy(rs_decoder_handle decoder);

RS_API rs_status_v1 RS_CALL rs_renderer_create(rs_runtime_handle runtime, const rs_renderer_options_v1* options, rs_renderer_handle* out_renderer);
RS_API rs_status_v1 RS_CALL rs_renderer_set_target_window(rs_renderer_handle renderer, uintptr_t target_hwnd);
RS_API rs_status_v1 RS_CALL rs_renderer_set_view_mode(rs_renderer_handle renderer, rs_renderer_view_mode_v1 view_mode);
RS_API rs_status_v1 RS_CALL rs_renderer_set_transform(rs_renderer_handle renderer, const rs_renderer_transform_v1* transform);
RS_API rs_status_v1 RS_CALL rs_renderer_submit_d3d11_frame(rs_renderer_handle renderer, const rs_frame_info_v1* frame);
RS_API rs_status_v1 RS_CALL rs_renderer_submit_cursor(rs_renderer_handle renderer, const rs_cursor_info_v1* cursor);
RS_API rs_status_v1 RS_CALL rs_renderer_resize(rs_renderer_handle renderer, uint32_t pixel_width, uint32_t pixel_height);
RS_API rs_status_v1 RS_CALL rs_renderer_clear(rs_renderer_handle renderer);
RS_API void RS_CALL rs_renderer_destroy(rs_renderer_handle renderer);

/* create_offer/create_answer create and set the local description atomically, then
   emit on_local_description. set_remote_description must complete before applying
   candidates that depend on it. Callback payloads are borrowed. */
RS_API rs_status_v1 RS_CALL rs_transport_create(rs_runtime_handle runtime, const rs_transport_options_v1* options, rs_transport_handle* out_transport);
RS_API rs_status_v1 RS_CALL rs_transport_create_offer(rs_transport_handle transport, uint32_t ice_restart);
RS_API rs_status_v1 RS_CALL rs_transport_create_answer(rs_transport_handle transport);
RS_API rs_status_v1 RS_CALL rs_transport_set_remote_description(rs_transport_handle transport, const rs_session_description_v1* description);
RS_API rs_status_v1 RS_CALL rs_transport_add_remote_ice_candidate(rs_transport_handle transport, const rs_ice_candidate_v1* candidate);
RS_API rs_status_v1 RS_CALL rs_transport_submit_encoded_video(rs_transport_handle transport, const rs_encoded_frame_v1* frame);
RS_API rs_status_v1 RS_CALL rs_transport_submit_d3d11_video(rs_transport_handle transport, const rs_frame_info_v1* frame);
RS_API rs_status_v1 RS_CALL rs_transport_set_video_rate(rs_transport_handle transport, uint32_t target_bitrate_bps, uint32_t target_fps);
RS_API rs_status_v1 RS_CALL rs_transport_open_data_channel(rs_transport_handle transport, const rs_data_channel_options_v1* options, uint32_t* out_channel_id);
RS_API rs_status_v1 RS_CALL rs_transport_send_data(rs_transport_handle transport, const rs_data_message_v1* message);
RS_API rs_status_v1 RS_CALL rs_transport_close_data_channel(rs_transport_handle transport, uint32_t channel_id);
RS_API rs_status_v1 RS_CALL rs_transport_get_stats(rs_transport_handle transport, rs_transport_stats_v1* out_stats);
RS_API rs_status_v1 RS_CALL rs_transport_close(rs_transport_handle transport);
RS_API void RS_CALL rs_transport_destroy(rs_transport_handle transport);

RS_API rs_status_v1 RS_CALL rs_input_injector_create(rs_runtime_handle runtime, const rs_input_options_v1* options, rs_input_injector_handle* out_injector);
RS_API rs_status_v1 RS_CALL rs_input_injector_set_display_generation(rs_input_injector_handle injector, uint64_t display_generation);
RS_API rs_status_v1 RS_CALL rs_input_inject_pointer(rs_input_injector_handle injector, const rs_pointer_input_v1* input);
RS_API rs_status_v1 RS_CALL rs_input_inject_keyboard(rs_input_injector_handle injector, const rs_keyboard_input_v1* input);
RS_API rs_status_v1 RS_CALL rs_input_release_all(rs_input_injector_handle injector, uint64_t through_input_sequence);
RS_API void RS_CALL rs_input_injector_destroy(rs_input_injector_handle injector);

/* ABI invariants:
   - Callers set struct_size to sizeof(the exact struct version they compiled).
   - The implementation reads only fields covered by struct_size and ignores known trailing fields.
   - A major ABI mismatch fails rs_runtime_create; a newer minor is accepted only when safe.
   - No C++ exception crosses this boundary; status plus rs_runtime_get_last_error is used.
   - All destroy functions are null-safe and must be called from a non-callback thread.
   - Callback/string/byte/frame payloads are borrowed and must be copied before return.
   - Create-call option arrays and strings need remain valid only until the call returns.
   - D3D11 textures are borrowed and must belong to the documented shared device or be
     opened through the implementation's documented shared-handle path.
   - Blocking calls are identified by implementation documentation; callbacks never run
     while an internal global lock is held. Reentrant destruction from callbacks is forbidden.
   - The implementation owns all opaque handles; callers never free them directly. */

#ifdef __cplusplus
}
#endif

#endif
