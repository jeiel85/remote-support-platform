#include "native_internal.hpp"
#include "input_mapping.hpp"

#include <limits>
#include <memory>

namespace {
constexpr uint32_t permission_mask = RS_INPUT_PERMISSION_POINTER | RS_INPUT_PERMISSION_KEYBOARD;
constexpr uint32_t test_noop_flag = 0x80000000u;

void remember_input_error(rs_input_injector_t* injector, rs_status_v1 status, const char* code, const char* detail) {
  set_last_error(injector->runtime, status, code, detail);
}

bool desktop_is_supported() {
  HDESK desktop = OpenInputDesktop(0, FALSE, DESKTOP_READOBJECTS);
  if (desktop == nullptr) return false;
  std::array<wchar_t, 256> name{};
  DWORD required = 0;
  const bool success = GetUserObjectInformationW(desktop, UOI_NAME, name.data(),
      static_cast<DWORD>(name.size() * sizeof(wchar_t)), &required) != FALSE;
  CloseDesktop(desktop);
  return success && _wcsicmp(name.data(), L"Default") == 0;
}

std::optional<DWORD> process_integrity_rid(HANDLE process) {
  HANDLE token = nullptr;
  if (!OpenProcessToken(process, TOKEN_QUERY, &token)) return std::nullopt;
  DWORD size = 0;
  GetTokenInformation(token, TokenIntegrityLevel, nullptr, 0, &size);
  std::vector<uint8_t> buffer(size);
  if (size == 0 || !GetTokenInformation(token, TokenIntegrityLevel, buffer.data(), size, &size)) {
    CloseHandle(token);
    return std::nullopt;
  }
  CloseHandle(token);
  const auto* label = reinterpret_cast<const TOKEN_MANDATORY_LABEL*>(buffer.data());
  const DWORD count = *GetSidSubAuthorityCount(label->Label.Sid);
  if (count == 0) return std::nullopt;
  return *GetSidSubAuthority(label->Label.Sid, count - 1);
}

bool foreground_has_higher_integrity() {
  DWORD target_process_id = 0;
  const HWND foreground = GetForegroundWindow();
  if (foreground == nullptr || GetWindowThreadProcessId(foreground, &target_process_id) == 0 || target_process_id == 0) return false;
  HANDLE target_process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, target_process_id);
  if (target_process == nullptr) return false;
  const auto target = process_integrity_rid(target_process);
  CloseHandle(target_process);
  const auto current = process_integrity_rid(GetCurrentProcess());
  return target.has_value() && current.has_value() && *target > *current;
}

rs_input_capability_state_v1 capability_state(rs_input_injector_t* injector,
    bool& secure_desktop, bool& uipi_blocked, const char*& reason) {
  secure_desktop = !desktop_is_supported();
  uipi_blocked = !secure_desktop && foreground_has_higher_integrity();
  if (!injector->enabled) { reason = "INPUT_DISABLED"; return RS_INPUT_CAPABILITY_DISABLED; }
  if (secure_desktop) { reason = "INPUT_SECURE_DESKTOP_UNSUPPORTED"; return RS_INPUT_CAPABILITY_SECURE_DESKTOP; }
  if (uipi_blocked) { reason = "INPUT_UIPI_BLOCKED"; return RS_INPUT_CAPABILITY_UIPI_BLOCKED; }
  reason = "INPUT_AVAILABLE";
  return RS_INPUT_CAPABILITY_AVAILABLE;
}

rs_status_v1 send_inputs(rs_input_injector_t* injector, std::vector<INPUT>& inputs) {
  if (inputs.empty()) return RS_STATUS_OK;
#if defined(RS_ENABLE_TEST_FAULT_INJECTION)
  if ((injector->permission_flags & test_noop_flag) != 0) return RS_STATUS_OK;
#endif
  SetLastError(ERROR_SUCCESS);
  const UINT sent = SendInput(static_cast<UINT>(inputs.size()), inputs.data(), sizeof(INPUT));
  if (sent == inputs.size()) return RS_STATUS_OK;
  const DWORD error = GetLastError();
  if (error == ERROR_ACCESS_DENIED || foreground_has_higher_integrity()) {
    remember_input_error(injector, RS_STATUS_ACCESS_DENIED, "INPUT_UIPI_BLOCKED", "Windows UIPI blocked remote input injection.");
    return RS_STATUS_ACCESS_DENIED;
  }
  remember_input_error(injector, RS_STATUS_INTERNAL_ERROR, "INPUT_SEND_FAILED", "SendInput did not accept the complete event batch.");
  return RS_STATUS_INTERNAL_ERROR;
}

DWORD button_down_flag(rs_pointer_button_v1 button) {
  switch (button) {
    case RS_POINTER_BUTTON_LEFT: return MOUSEEVENTF_LEFTDOWN;
    case RS_POINTER_BUTTON_RIGHT: return MOUSEEVENTF_RIGHTDOWN;
    case RS_POINTER_BUTTON_MIDDLE: return MOUSEEVENTF_MIDDLEDOWN;
    case RS_POINTER_BUTTON_X1:
    case RS_POINTER_BUTTON_X2: return MOUSEEVENTF_XDOWN;
    default: return 0;
  }
}

DWORD button_up_flag(rs_pointer_button_v1 button) {
  switch (button) {
    case RS_POINTER_BUTTON_LEFT: return MOUSEEVENTF_LEFTUP;
    case RS_POINTER_BUTTON_RIGHT: return MOUSEEVENTF_RIGHTUP;
    case RS_POINTER_BUTTON_MIDDLE: return MOUSEEVENTF_MIDDLEUP;
    case RS_POINTER_BUTTON_X1:
    case RS_POINTER_BUTTON_X2: return MOUSEEVENTF_XUP;
    default: return 0;
  }
}

DWORD x_button_data(rs_pointer_button_v1 button) {
  return button == RS_POINTER_BUTTON_X1 ? XBUTTON1 : button == RS_POINTER_BUTTON_X2 ? XBUTTON2 : 0;
}

void append_key_release(std::vector<INPUT>& inputs, uint32_t encoded) {
  INPUT value{};
  value.type = INPUT_KEYBOARD;
  value.ki.wScan = static_cast<WORD>(encoded & 0xffffu);
  value.ki.dwFlags = KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP;
  if ((encoded & 0x10000u) != 0) value.ki.dwFlags |= KEYEVENTF_EXTENDEDKEY;
  inputs.push_back(value);
}

void append_button_release(std::vector<INPUT>& inputs, rs_pointer_button_v1 button) {
  INPUT value{};
  value.type = INPUT_MOUSE;
  value.mi.dwFlags = button_up_flag(button);
  value.mi.mouseData = x_button_data(button);
  if (value.mi.dwFlags != 0) inputs.push_back(value);
}

rs_status_v1 release_all_locked(rs_input_injector_t* injector, uint64_t through_sequence) {
  std::vector<INPUT> inputs;
  inputs.reserve(injector->pressed_keys.size() + injector->pressed_buttons.size());
  for (const uint32_t key : injector->pressed_keys) append_key_release(inputs, key);
  for (const auto button : injector->pressed_buttons) append_button_release(inputs, button);
  injector->pressed_keys.clear();
  injector->pressed_buttons.clear();
  injector->last_input_sequence = (std::max)(injector->last_input_sequence, through_sequence);
  return send_inputs(injector, inputs);
}

bool valid_sequence(rs_input_injector_t* injector, uint64_t sequence) {
  return sequence != 0 && sequence > injector->last_input_sequence;
}
}

extern "C" {
rs_status_v1 RS_CALL rs_input_injector_create(rs_runtime_handle runtime, const rs_input_options_v1* options,
    rs_input_injector_handle* out_injector) {
  if (runtime == nullptr || options == nullptr || out_injector == nullptr ||
      options->struct_size < sizeof(rs_input_options_v1) || options->expected_display_generation == 0 ||
      (options->flags & ~(permission_mask | test_noop_flag)) != 0) return RS_STATUS_INVALID_ARGUMENT;
#if !defined(RS_ENABLE_TEST_FAULT_INJECTION)
  if ((options->flags & test_noop_flag) != 0) return RS_STATUS_INVALID_ARGUMENT;
#endif
  auto injector = std::make_unique<rs_input_injector_t>();
  injector->runtime = runtime;
  injector->expected_display_generation = options->expected_display_generation;
  injector->permission_flags = options->flags;
  *out_injector = injector.release();
  return RS_STATUS_OK;
}

rs_status_v1 RS_CALL rs_input_injector_set_display_generation(rs_input_injector_handle injector,
    uint64_t display_generation) {
  if (injector == nullptr || display_generation == 0) return RS_STATUS_INVALID_ARGUMENT;
  std::scoped_lock lock(injector->mutex);
  if (display_generation != injector->expected_display_generation) {
    const rs_status_v1 status = release_all_locked(injector, injector->last_input_sequence);
    injector->expected_display_generation = display_generation;
    return status;
  }
  return RS_STATUS_OK;
}

rs_status_v1 RS_CALL rs_input_injector_set_enabled(rs_input_injector_handle injector, uint32_t enabled) {
  if (injector == nullptr || enabled > 1) return RS_STATUS_INVALID_ARGUMENT;
  std::scoped_lock lock(injector->mutex);
  if (enabled == 0) {
    const rs_status_v1 status = release_all_locked(injector, injector->last_input_sequence);
    injector->enabled = false;
    return status;
  }
  injector->enabled = true;
  return RS_STATUS_OK;
}

rs_status_v1 RS_CALL rs_input_injector_get_capability(rs_input_injector_handle injector,
    rs_input_capability_v1* out_capability) {
  if (injector == nullptr || out_capability == nullptr || out_capability->struct_size < sizeof(rs_input_capability_v1)) return RS_STATUS_INVALID_ARGUMENT;
  bool secure_desktop = false;
  bool uipi_blocked = false;
  const char* reason = nullptr;
  rs_input_capability_v1 capability{};
  capability.struct_size = sizeof(capability);
  {
    std::scoped_lock lock(injector->mutex);
    capability.state = capability_state(injector, secure_desktop, uipi_blocked, reason);
    capability.pointer_allowed = (injector->permission_flags & RS_INPUT_PERMISSION_POINTER) != 0 ? 1u : 0u;
    capability.keyboard_allowed = (injector->permission_flags & RS_INPUT_PERMISSION_KEYBOARD) != 0 ? 1u : 0u;
  }
  capability.secure_desktop_active = secure_desktop ? 1u : 0u;
  capability.foreground_has_higher_integrity = uipi_blocked ? 1u : 0u;
  capability.stable_reason = {reason, static_cast<uint32_t>(std::strlen(reason))};
  *out_capability = capability;
  return RS_STATUS_OK;
}

rs_status_v1 RS_CALL rs_input_inject_pointer(rs_input_injector_handle injector, const rs_pointer_input_v1* input) {
  if (injector == nullptr || input == nullptr || input->struct_size < sizeof(rs_pointer_input_v1)) return RS_STATUS_INVALID_ARGUMENT;
  std::scoped_lock lock(injector->mutex);
  if (!injector->enabled || (injector->permission_flags & RS_INPUT_PERMISSION_POINTER) == 0) return RS_STATUS_ACCESS_DENIED;
  if (input->display_generation != injector->expected_display_generation) return RS_STATUS_INVALID_STATE;
  if (!valid_sequence(injector, input->input_sequence)) return RS_STATUS_PROTOCOL_ERROR;
  bool secure_desktop = false;
  bool uipi_blocked = false;
  const char* reason = nullptr;
#if defined(RS_ENABLE_TEST_FAULT_INJECTION)
  if ((injector->permission_flags & test_noop_flag) == 0 &&
      capability_state(injector, secure_desktop, uipi_blocked, reason) != RS_INPUT_CAPABILITY_AVAILABLE) {
    remember_input_error(injector, RS_STATUS_ACCESS_DENIED, reason, "The current Windows desktop or target integrity does not permit input injection.");
    return RS_STATUS_ACCESS_DENIED;
  }
#else
  if (capability_state(injector, secure_desktop, uipi_blocked, reason) != RS_INPUT_CAPABILITY_AVAILABLE) {
    remember_input_error(injector, RS_STATUS_ACCESS_DENIED, reason, "The current Windows desktop or target integrity does not permit input injection.");
    return RS_STATUS_ACCESS_DENIED;
  }
#endif
  const virtual_desktop_rect desktop{GetSystemMetrics(SM_XVIRTUALSCREEN), GetSystemMetrics(SM_YVIRTUALSCREEN),
      GetSystemMetrics(SM_CXVIRTUALSCREEN), GetSystemMetrics(SM_CYVIRTUALSCREEN)};
  uint16_t x = 0;
  uint16_t y = 0;
  if (!normalize_virtual_desktop_point(desktop, input->desktop_x, input->desktop_y, x, y)) return RS_STATUS_INVALID_ARGUMENT;
  INPUT value{};
  value.type = INPUT_MOUSE;
  value.mi.dx = x;
  value.mi.dy = y;
  value.mi.dwFlags = MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK | MOUSEEVENTF_MOVE;
  if (input->kind == RS_POINTER_BUTTON_DOWN) {
    const DWORD flag = button_down_flag(input->button);
    if (flag == 0 || injector->pressed_buttons.contains(input->button)) return RS_STATUS_PROTOCOL_ERROR;
    value.mi.dwFlags |= flag;
    value.mi.mouseData = x_button_data(input->button);
  } else if (input->kind == RS_POINTER_BUTTON_UP) {
    const DWORD flag = button_up_flag(input->button);
    if (flag == 0 || !injector->pressed_buttons.contains(input->button)) return RS_STATUS_PROTOCOL_ERROR;
    value.mi.dwFlags |= flag;
    value.mi.mouseData = x_button_data(input->button);
  } else if (input->kind == RS_POINTER_WHEEL) {
    value.mi.dwFlags |= MOUSEEVENTF_WHEEL;
    value.mi.mouseData = static_cast<DWORD>(input->wheel_delta);
  } else if (input->kind == RS_POINTER_HORIZONTAL_WHEEL) {
    value.mi.dwFlags |= MOUSEEVENTF_HWHEEL;
    value.mi.mouseData = static_cast<DWORD>(input->wheel_delta);
  } else if (input->kind != RS_POINTER_MOVE) {
    return RS_STATUS_INVALID_ARGUMENT;
  }
  std::vector<INPUT> values{value};
  const rs_status_v1 status = send_inputs(injector, values);
  if (status != RS_STATUS_OK) return status;
  if (input->kind == RS_POINTER_BUTTON_DOWN) injector->pressed_buttons.insert(input->button);
  if (input->kind == RS_POINTER_BUTTON_UP) injector->pressed_buttons.erase(input->button);
  injector->last_input_sequence = input->input_sequence;
  return RS_STATUS_OK;
}

rs_status_v1 RS_CALL rs_input_inject_keyboard(rs_input_injector_handle injector, const rs_keyboard_input_v1* input) {
  if (injector == nullptr || input == nullptr || input->struct_size < sizeof(rs_keyboard_input_v1)) return RS_STATUS_INVALID_ARGUMENT;
  std::scoped_lock lock(injector->mutex);
  if (!injector->enabled || (injector->permission_flags & RS_INPUT_PERMISSION_KEYBOARD) == 0) return RS_STATUS_ACCESS_DENIED;
  if (!valid_sequence(injector, input->input_sequence)) return RS_STATUS_PROTOCOL_ERROR;
  bool secure_desktop = false;
  bool uipi_blocked = false;
  const char* reason = nullptr;
#if defined(RS_ENABLE_TEST_FAULT_INJECTION)
  if ((injector->permission_flags & test_noop_flag) == 0 &&
      capability_state(injector, secure_desktop, uipi_blocked, reason) != RS_INPUT_CAPABILITY_AVAILABLE) {
    remember_input_error(injector, RS_STATUS_ACCESS_DENIED, reason, "The current Windows desktop or target integrity does not permit input injection.");
    return RS_STATUS_ACCESS_DENIED;
  }
#else
  if (capability_state(injector, secure_desktop, uipi_blocked, reason) != RS_INPUT_CAPABILITY_AVAILABLE) {
    remember_input_error(injector, RS_STATUS_ACCESS_DENIED, reason, "The current Windows desktop or target integrity does not permit input injection.");
    return RS_STATUS_ACCESS_DENIED;
  }
#endif
  std::vector<INPUT> values;
  uint32_t encoded_key = 0;
  if (input->kind == RS_KEYBOARD_UNICODE_TEXT) {
    const std::wstring text = wide_from_utf8(input->unicode_text_utf8);
    if (text.empty() || text.size() > 256) return RS_STATUS_INVALID_ARGUMENT;
    values.reserve(text.size() * 2);
    for (const wchar_t character : text) {
      INPUT down{};
      down.type = INPUT_KEYBOARD;
      down.ki.wScan = static_cast<WORD>(character);
      down.ki.dwFlags = KEYEVENTF_UNICODE;
      values.push_back(down);
      INPUT up = down;
      up.ki.dwFlags |= KEYEVENTF_KEYUP;
      values.push_back(up);
    }
  } else {
    if (input->scan_code == 0 || input->scan_code > 0xffff || input->virtual_key > 0xff) return RS_STATUS_INVALID_ARGUMENT;
    encoded_key = input->scan_code | (input->extended != 0 ? 0x10000u : 0);
    const bool already_pressed = injector->pressed_keys.contains(encoded_key);
    if (input->kind == RS_KEYBOARD_KEY_DOWN && already_pressed && input->repeat == 0) return RS_STATUS_PROTOCOL_ERROR;
    if (input->kind == RS_KEYBOARD_KEY_UP && !already_pressed) return RS_STATUS_PROTOCOL_ERROR;
    if (input->kind != RS_KEYBOARD_KEY_DOWN && input->kind != RS_KEYBOARD_KEY_UP) return RS_STATUS_INVALID_ARGUMENT;
    INPUT value{};
    value.type = INPUT_KEYBOARD;
    value.ki.wScan = static_cast<WORD>(input->scan_code);
    value.ki.dwFlags = KEYEVENTF_SCANCODE;
    if (input->extended != 0) value.ki.dwFlags |= KEYEVENTF_EXTENDEDKEY;
    if (input->kind == RS_KEYBOARD_KEY_UP) value.ki.dwFlags |= KEYEVENTF_KEYUP;
    values.push_back(value);
  }
  const rs_status_v1 status = send_inputs(injector, values);
  if (status != RS_STATUS_OK) return status;
  if (input->kind == RS_KEYBOARD_KEY_DOWN) injector->pressed_keys.insert(encoded_key);
  if (input->kind == RS_KEYBOARD_KEY_UP) injector->pressed_keys.erase(encoded_key);
  injector->last_input_sequence = input->input_sequence;
  return RS_STATUS_OK;
}

rs_status_v1 RS_CALL rs_input_release_all(rs_input_injector_handle injector, uint64_t through_input_sequence) {
  if (injector == nullptr) return RS_STATUS_INVALID_ARGUMENT;
  std::scoped_lock lock(injector->mutex);
  return release_all_locked(injector, through_input_sequence);
}

void RS_CALL rs_input_injector_destroy(rs_input_injector_handle injector) {
  if (injector == nullptr) return;
  {
    std::scoped_lock lock(injector->mutex);
    release_all_locked(injector, (std::numeric_limits<uint64_t>::max)());
    injector->enabled = false;
  }
  delete injector;
}
}
