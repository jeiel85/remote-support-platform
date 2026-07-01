#include "native_internal.hpp"

namespace {
rs_status_v1 unavailable(rs_runtime_t* runtime, const char* code, const char* detail) {
  set_last_error(runtime, RS_STATUS_NOT_SUPPORTED, code, detail);
  return RS_STATUS_NOT_SUPPORTED;
}
}

extern "C" {
rs_status_v1 RS_CALL rs_runtime_enumerate_encoders(rs_runtime_handle runtime, rs_encoder_capability_callback_v1, void*) {
  return runtime == nullptr ? RS_STATUS_INVALID_ARGUMENT : unavailable(runtime, "ENCODER_CONFIGURATION_UNSUPPORTED", "Encoder capability discovery is enabled by Goal 03.");
}
rs_status_v1 RS_CALL rs_encoder_create(rs_runtime_handle runtime, const rs_encoder_options_v1*, rs_encoder_handle*) { return runtime == nullptr ? RS_STATUS_INVALID_ARGUMENT : unavailable(runtime, "ENCODER_CONFIGURATION_UNSUPPORTED", "Encoder creation is enabled by Goal 03."); }
rs_status_v1 RS_CALL rs_encoder_submit_d3d11_frame(rs_encoder_handle encoder, const rs_frame_info_v1*) { return encoder == nullptr ? RS_STATUS_INVALID_ARGUMENT : unavailable(encoder->runtime, "ENCODER_CONFIGURATION_UNSUPPORTED", "Encoder submission is enabled by Goal 03."); }
rs_status_v1 RS_CALL rs_encoder_set_rate(rs_encoder_handle encoder, uint32_t, uint32_t) { return encoder == nullptr ? RS_STATUS_INVALID_ARGUMENT : unavailable(encoder->runtime, "ENCODER_CONFIGURATION_UNSUPPORTED", "Encoder rate control is enabled by Goal 03."); }
rs_status_v1 RS_CALL rs_encoder_reconfigure(rs_encoder_handle encoder, const rs_encoder_reconfigure_v1*) { return encoder == nullptr ? RS_STATUS_INVALID_ARGUMENT : unavailable(encoder->runtime, "ENCODER_CONFIGURATION_UNSUPPORTED", "Encoder reconfiguration is enabled by Goal 03."); }
rs_status_v1 RS_CALL rs_encoder_request_keyframe(rs_encoder_handle encoder) { return encoder == nullptr ? RS_STATUS_INVALID_ARGUMENT : unavailable(encoder->runtime, "ENCODER_CONFIGURATION_UNSUPPORTED", "Keyframe requests are enabled by Goal 03."); }
rs_status_v1 RS_CALL rs_encoder_flush(rs_encoder_handle encoder, uint32_t) { return encoder == nullptr ? RS_STATUS_INVALID_ARGUMENT : unavailable(encoder->runtime, "ENCODER_CONFIGURATION_UNSUPPORTED", "Encoder flush is enabled by Goal 03."); }
void RS_CALL rs_encoder_destroy(rs_encoder_handle encoder) { delete encoder; }
rs_status_v1 RS_CALL rs_decoder_create(rs_runtime_handle runtime, const rs_decoder_options_v1*, rs_decoder_handle*) { return runtime == nullptr ? RS_STATUS_INVALID_ARGUMENT : unavailable(runtime, "ENCODER_CONFIGURATION_UNSUPPORTED", "Decoder creation is enabled by Goal 03."); }
rs_status_v1 RS_CALL rs_decoder_submit_h264(rs_decoder_handle decoder, const rs_encoded_frame_v1*) { return decoder == nullptr ? RS_STATUS_INVALID_ARGUMENT : unavailable(decoder->runtime, "ENCODER_CONFIGURATION_UNSUPPORTED", "Decoder submission is enabled by Goal 03."); }
rs_status_v1 RS_CALL rs_decoder_reset(rs_decoder_handle decoder) { return decoder == nullptr ? RS_STATUS_INVALID_ARGUMENT : unavailable(decoder->runtime, "ENCODER_CONFIGURATION_UNSUPPORTED", "Decoder reset is enabled by Goal 03."); }
rs_status_v1 RS_CALL rs_decoder_flush(rs_decoder_handle decoder, uint32_t) { return decoder == nullptr ? RS_STATUS_INVALID_ARGUMENT : unavailable(decoder->runtime, "ENCODER_CONFIGURATION_UNSUPPORTED", "Decoder flush is enabled by Goal 03."); }
void RS_CALL rs_decoder_destroy(rs_decoder_handle decoder) { delete decoder; }

rs_status_v1 RS_CALL rs_transport_create(rs_runtime_handle runtime, const rs_transport_options_v1*, rs_transport_handle*) { return runtime == nullptr ? RS_STATUS_INVALID_ARGUMENT : unavailable(runtime, "TRANSPORT_CHANNEL_NEGOTIATION_FAILED", "Transport is enabled by Goal 04."); }
rs_status_v1 RS_CALL rs_transport_create_offer(rs_transport_handle transport, uint32_t) { return transport == nullptr ? RS_STATUS_INVALID_ARGUMENT : unavailable(transport->runtime, "TRANSPORT_CHANNEL_NEGOTIATION_FAILED", "Transport is enabled by Goal 04."); }
rs_status_v1 RS_CALL rs_transport_create_answer(rs_transport_handle transport) { return transport == nullptr ? RS_STATUS_INVALID_ARGUMENT : unavailable(transport->runtime, "TRANSPORT_CHANNEL_NEGOTIATION_FAILED", "Transport is enabled by Goal 04."); }
rs_status_v1 RS_CALL rs_transport_set_remote_description(rs_transport_handle transport, const rs_session_description_v1*) { return transport == nullptr ? RS_STATUS_INVALID_ARGUMENT : unavailable(transport->runtime, "TRANSPORT_CHANNEL_NEGOTIATION_FAILED", "Transport is enabled by Goal 04."); }
rs_status_v1 RS_CALL rs_transport_add_remote_ice_candidate(rs_transport_handle transport, const rs_ice_candidate_v1*) { return transport == nullptr ? RS_STATUS_INVALID_ARGUMENT : unavailable(transport->runtime, "TRANSPORT_CHANNEL_NEGOTIATION_FAILED", "Transport is enabled by Goal 04."); }
rs_status_v1 RS_CALL rs_transport_submit_encoded_video(rs_transport_handle transport, const rs_encoded_frame_v1*) { return transport == nullptr ? RS_STATUS_INVALID_ARGUMENT : unavailable(transport->runtime, "TRANSPORT_CHANNEL_NEGOTIATION_FAILED", "Transport is enabled by Goal 04."); }
rs_status_v1 RS_CALL rs_transport_submit_d3d11_video(rs_transport_handle transport, const rs_frame_info_v1*) { return transport == nullptr ? RS_STATUS_INVALID_ARGUMENT : unavailable(transport->runtime, "TRANSPORT_CHANNEL_NEGOTIATION_FAILED", "Transport is enabled by Goal 04."); }
rs_status_v1 RS_CALL rs_transport_set_video_rate(rs_transport_handle transport, uint32_t, uint32_t) { return transport == nullptr ? RS_STATUS_INVALID_ARGUMENT : unavailable(transport->runtime, "TRANSPORT_CHANNEL_NEGOTIATION_FAILED", "Transport is enabled by Goal 04."); }
rs_status_v1 RS_CALL rs_transport_open_data_channel(rs_transport_handle transport, const rs_data_channel_options_v1*, uint32_t*) { return transport == nullptr ? RS_STATUS_INVALID_ARGUMENT : unavailable(transport->runtime, "TRANSPORT_CHANNEL_NEGOTIATION_FAILED", "Transport is enabled by Goal 04."); }
rs_status_v1 RS_CALL rs_transport_send_data(rs_transport_handle transport, const rs_data_message_v1*) { return transport == nullptr ? RS_STATUS_INVALID_ARGUMENT : unavailable(transport->runtime, "TRANSPORT_CHANNEL_NEGOTIATION_FAILED", "Transport is enabled by Goal 04."); }
rs_status_v1 RS_CALL rs_transport_close_data_channel(rs_transport_handle transport, uint32_t) { return transport == nullptr ? RS_STATUS_INVALID_ARGUMENT : unavailable(transport->runtime, "TRANSPORT_CHANNEL_NEGOTIATION_FAILED", "Transport is enabled by Goal 04."); }
rs_status_v1 RS_CALL rs_transport_get_stats(rs_transport_handle transport, rs_transport_stats_v1*) { return transport == nullptr ? RS_STATUS_INVALID_ARGUMENT : unavailable(transport->runtime, "TRANSPORT_CHANNEL_NEGOTIATION_FAILED", "Transport is enabled by Goal 04."); }
rs_status_v1 RS_CALL rs_transport_close(rs_transport_handle transport) { return transport == nullptr ? RS_STATUS_INVALID_ARGUMENT : RS_STATUS_OK; }
void RS_CALL rs_transport_destroy(rs_transport_handle transport) { delete transport; }

rs_status_v1 RS_CALL rs_input_injector_create(rs_runtime_handle runtime, const rs_input_options_v1*, rs_input_injector_handle*) { return runtime == nullptr ? RS_STATUS_INVALID_ARGUMENT : unavailable(runtime, "INPUT_PERMISSION_REVOKED", "Input injection is enabled by Goal 05."); }
rs_status_v1 RS_CALL rs_input_injector_set_display_generation(rs_input_injector_handle injector, uint64_t) { return injector == nullptr ? RS_STATUS_INVALID_ARGUMENT : unavailable(injector->runtime, "INPUT_PERMISSION_REVOKED", "Input injection is enabled by Goal 05."); }
rs_status_v1 RS_CALL rs_input_inject_pointer(rs_input_injector_handle injector, const rs_pointer_input_v1*) { return injector == nullptr ? RS_STATUS_INVALID_ARGUMENT : unavailable(injector->runtime, "INPUT_PERMISSION_REVOKED", "Input injection is enabled by Goal 05."); }
rs_status_v1 RS_CALL rs_input_inject_keyboard(rs_input_injector_handle injector, const rs_keyboard_input_v1*) { return injector == nullptr ? RS_STATUS_INVALID_ARGUMENT : unavailable(injector->runtime, "INPUT_PERMISSION_REVOKED", "Input injection is enabled by Goal 05."); }
rs_status_v1 RS_CALL rs_input_release_all(rs_input_injector_handle injector, uint64_t) { return injector == nullptr ? RS_STATUS_INVALID_ARGUMENT : unavailable(injector->runtime, "INPUT_PERMISSION_REVOKED", "Input injection is enabled by Goal 05."); }
void RS_CALL rs_input_injector_destroy(rs_input_injector_handle injector) { delete injector; }
}
