#include "remote_support_native.h"

#include <windows.h>

namespace {
rs_runtime_handle create_runtime() {
  rs_runtime_options_v1 options{};
  options.struct_size = sizeof(options);
  options.requested_abi_major = RS_NATIVE_ABI_MAJOR;
  options.requested_abi_minor = RS_NATIVE_ABI_MINOR;
  rs_callbacks_v1 callbacks{};
  callbacks.struct_size = sizeof(callbacks);
  rs_runtime_handle runtime{};
  return rs_runtime_create(&options, &callbacks, &runtime) == RS_STATUS_OK ? runtime : nullptr;
}

rs_input_injector_handle create_injector(rs_runtime_handle runtime, uint32_t permissions) {
  rs_input_options_v1 options{};
  options.struct_size = sizeof(options);
  options.expected_display_generation = 7;
  options.flags = permissions | 0x80000000u;
  rs_input_injector_handle injector{};
  return rs_input_injector_create(runtime, &options, &injector) == RS_STATUS_OK ? injector : nullptr;
}
}

int main() {
  rs_runtime_handle runtime = create_runtime();
  if (runtime == nullptr) return 1;
  rs_input_injector_handle view_only = create_injector(runtime, RS_INPUT_PERMISSION_NONE);
  if (view_only == nullptr) return 2;
  rs_pointer_input_v1 pointer{};
  pointer.struct_size = sizeof(pointer);
  pointer.kind = RS_POINTER_MOVE;
  pointer.desktop_x = GetSystemMetrics(SM_XVIRTUALSCREEN);
  pointer.desktop_y = GetSystemMetrics(SM_YVIRTUALSCREEN);
  pointer.display_generation = 7;
  pointer.input_sequence = 1;
  if (rs_input_inject_pointer(view_only, &pointer) != RS_STATUS_ACCESS_DENIED) return 3;
  rs_input_injector_destroy(view_only);

  rs_input_injector_handle injector = create_injector(runtime,
      RS_INPUT_PERMISSION_POINTER | RS_INPUT_PERMISSION_KEYBOARD);
  if (injector == nullptr) return 4;
  if (rs_input_inject_pointer(injector, &pointer) != RS_STATUS_OK) return 5;
  pointer.kind = RS_POINTER_BUTTON_DOWN;
  pointer.button = RS_POINTER_BUTTON_LEFT;
  pointer.input_sequence = 2;
  if (rs_input_inject_pointer(injector, &pointer) != RS_STATUS_OK) return 6;
  pointer.display_generation = 6;
  pointer.input_sequence = 3;
  if (rs_input_inject_pointer(injector, &pointer) != RS_STATUS_INVALID_STATE) return 7;

  rs_keyboard_input_v1 keyboard{};
  keyboard.struct_size = sizeof(keyboard);
  keyboard.kind = RS_KEYBOARD_KEY_DOWN;
  keyboard.scan_code = 0x1e;
  keyboard.input_sequence = 3;
  if (rs_input_inject_keyboard(injector, &keyboard) != RS_STATUS_OK) return 8;
  if (rs_input_injector_set_enabled(injector, 0) != RS_STATUS_OK) return 9;
  rs_input_capability_v1 capability{};
  capability.struct_size = sizeof(capability);
  if (rs_input_injector_get_capability(injector, &capability) != RS_STATUS_OK ||
      capability.state != RS_INPUT_CAPABILITY_DISABLED) return 10;
  keyboard.kind = RS_KEYBOARD_KEY_UP;
  keyboard.input_sequence = 4;
  if (rs_input_inject_keyboard(injector, &keyboard) != RS_STATUS_ACCESS_DENIED) return 11;
  if (rs_input_injector_set_enabled(injector, 1) != RS_STATUS_OK) return 12;
  if (rs_input_inject_keyboard(injector, &keyboard) != RS_STATUS_PROTOCOL_ERROR) return 13;

  keyboard.kind = RS_KEYBOARD_UNICODE_TEXT;
  keyboard.scan_code = 0;
  keyboard.unicode_text_utf8 = {"한A", 4};
  keyboard.input_sequence = 4;
  if (rs_input_inject_keyboard(injector, &keyboard) != RS_STATUS_OK) return 14;
  if (rs_input_release_all(injector, 10) != RS_STATUS_OK) return 15;
  pointer.kind = RS_POINTER_MOVE;
  pointer.button = RS_POINTER_BUTTON_NONE;
  pointer.display_generation = 7;
  pointer.input_sequence = 10;
  if (rs_input_inject_pointer(injector, &pointer) != RS_STATUS_PROTOCOL_ERROR) return 16;
  pointer.input_sequence = 11;
  if (rs_input_inject_pointer(injector, &pointer) != RS_STATUS_OK) return 17;
  if (rs_input_injector_set_display_generation(injector, 8) != RS_STATUS_OK) return 18;
  pointer.input_sequence = 12;
  if (rs_input_inject_pointer(injector, &pointer) != RS_STATUS_INVALID_STATE) return 19;
  rs_input_injector_destroy(injector);
  rs_runtime_destroy(runtime);
  return 0;
}
