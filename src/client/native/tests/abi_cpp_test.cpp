#include "remote_support_native.h"

#include <type_traits>

static_assert(std::is_standard_layout_v<rs_runtime_options_v1>);
static_assert(std::is_standard_layout_v<rs_frame_info_v1>);
static_assert(std::is_standard_layout_v<rs_encoded_frame_v1>);
static_assert(std::is_standard_layout_v<rs_callbacks_v1>);
static_assert(std::is_standard_layout_v<rs_input_capability_v1>);
static_assert(std::is_standard_layout_v<rs_permission_update_v1>);

int main() {
  return RS_NATIVE_ABI_MAJOR == 1u && RS_NATIVE_ABI_MINOR >= 4u ? 0 : 1;
}
