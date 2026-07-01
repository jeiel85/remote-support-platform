#include "native_internal.hpp"

namespace {
rs_status_v1 unavailable(rs_runtime_t* runtime, const char* code, const char* detail) {
  set_last_error(runtime, RS_STATUS_NOT_SUPPORTED, code, detail);
  return RS_STATUS_NOT_SUPPORTED;
}
}

extern "C" {
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
