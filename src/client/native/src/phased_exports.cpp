#include "native_internal.hpp"

namespace {
rs_status_v1 unavailable(rs_runtime_t* runtime, const char* code, const char* detail) {
  set_last_error(runtime, RS_STATUS_NOT_SUPPORTED, code, detail);
  return RS_STATUS_NOT_SUPPORTED;
}
}

extern "C" {
rs_status_v1 RS_CALL rs_input_injector_create(rs_runtime_handle runtime, const rs_input_options_v1*, rs_input_injector_handle*) { return runtime == nullptr ? RS_STATUS_INVALID_ARGUMENT : unavailable(runtime, "INPUT_PERMISSION_REVOKED", "Input injection is enabled by Goal 05."); }
rs_status_v1 RS_CALL rs_input_injector_set_display_generation(rs_input_injector_handle injector, uint64_t) { return injector == nullptr ? RS_STATUS_INVALID_ARGUMENT : unavailable(injector->runtime, "INPUT_PERMISSION_REVOKED", "Input injection is enabled by Goal 05."); }
rs_status_v1 RS_CALL rs_input_inject_pointer(rs_input_injector_handle injector, const rs_pointer_input_v1*) { return injector == nullptr ? RS_STATUS_INVALID_ARGUMENT : unavailable(injector->runtime, "INPUT_PERMISSION_REVOKED", "Input injection is enabled by Goal 05."); }
rs_status_v1 RS_CALL rs_input_inject_keyboard(rs_input_injector_handle injector, const rs_keyboard_input_v1*) { return injector == nullptr ? RS_STATUS_INVALID_ARGUMENT : unavailable(injector->runtime, "INPUT_PERMISSION_REVOKED", "Input injection is enabled by Goal 05."); }
rs_status_v1 RS_CALL rs_input_release_all(rs_input_injector_handle injector, uint64_t) { return injector == nullptr ? RS_STATUS_INVALID_ARGUMENT : unavailable(injector->runtime, "INPUT_PERMISSION_REVOKED", "Input injection is enabled by Goal 05."); }
void RS_CALL rs_input_injector_destroy(rs_input_injector_handle injector) { delete injector; }
}
