#include "remote_support_native.h"

#include <iostream>
#include <sstream>
#include <string>
#include <vector>

namespace {
struct capability_record {
  rs_encoder_backend_v1 backend{};
  std::string name;
  uint32_t max_width{};
  uint32_t max_height{};
  uint32_t max_fps{};
  uint32_t dynamic_rate{};
  uint32_t dynamic_resolution{};
};

std::vector<capability_record> capabilities;

void RS_CALL on_capability(void*, const rs_encoder_capability_v1* value) {
  capabilities.push_back({value->backend,
      std::string(value->implementation_name_utf8.data, value->implementation_name_utf8.length),
      value->max_width, value->max_height, value->max_fps, value->supports_dynamic_rate,
      value->supports_dynamic_resolution});
}

std::string escape_json(const std::string& value) {
  std::string result;
  for (const char character : value) {
    if (character == '\\' || character == '"') result.push_back('\\');
    result.push_back(character);
  }
  return result;
}
}

int main() {
  rs_runtime_options_v1 options{sizeof(options), 1, 1, 0, nullptr};
  rs_callbacks_v1 callbacks{};
  callbacks.struct_size = sizeof(callbacks);
  rs_runtime_handle runtime{};
  if (rs_runtime_create(&options, &callbacks, &runtime) != RS_STATUS_OK) return 1;
  const rs_status_v1 status = rs_runtime_enumerate_encoders(runtime, on_capability, nullptr);
  rs_runtime_destroy(runtime);
  if (status != RS_STATUS_OK) return 2;

  uint32_t hardware = 0;
  uint32_t software = 0;
  std::ostringstream output;
  output << "{\"encoders\":[";
  for (size_t index = 0; index < capabilities.size(); ++index) {
    const auto& item = capabilities[index];
    if (item.backend == RS_ENCODER_BACKEND_MEDIA_FOUNDATION_HARDWARE) ++hardware;
    if (item.backend == RS_ENCODER_BACKEND_MEDIA_FOUNDATION_SOFTWARE) ++software;
    if (index != 0) output << ',';
    output << "{\"backend\":\"" << (item.backend == RS_ENCODER_BACKEND_MEDIA_FOUNDATION_HARDWARE ? "hardware" : "software")
           << "\",\"name\":\"" << escape_json(item.name) << "\",\"maxWidth\":" << item.max_width
           << ",\"maxHeight\":" << item.max_height << ",\"maxFps\":" << item.max_fps
           << ",\"dynamicRate\":" << item.dynamic_rate
           << ",\"dynamicResolution\":" << item.dynamic_resolution << '}';
  }
  output << "],\"hardwareCount\":" << hardware << ",\"softwareCount\":" << software << "}\n";
  std::cout << output.str();
  return !capabilities.empty() && software > 0 ? 0 : 3;
}
