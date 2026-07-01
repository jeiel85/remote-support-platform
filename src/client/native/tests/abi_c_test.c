#include "remote_support_native.h"

int main(void) {
  rs_runtime_options_v1 runtime = {0};
  rs_capture_options_v1 capture = {0};
  rs_encoder_options_v1 encoder = {0};
  rs_decoder_options_v1 decoder = {0};
  rs_input_capability_v1 input_capability = {0};
  runtime.struct_size = (uint32_t)sizeof(runtime);
  capture.struct_size = (uint32_t)sizeof(capture);
  encoder.struct_size = (uint32_t)sizeof(encoder);
  decoder.struct_size = (uint32_t)sizeof(decoder);
  input_capability.struct_size = (uint32_t)sizeof(input_capability);
  return RS_NATIVE_ABI_MAJOR == 1u && RS_NATIVE_ABI_MINOR >= 3u &&
         runtime.struct_size > 0u && capture.struct_size > 0u &&
         encoder.struct_size > 0u && decoder.struct_size > 0u &&
         input_capability.struct_size > 0u ? 0 : 1;
}
