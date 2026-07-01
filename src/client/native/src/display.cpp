#include "native_internal.hpp"

#include <sstream>

namespace {
HMONITOR monitor_for_device(const wchar_t* device_name) {
  struct context { const wchar_t* name; HMONITOR monitor; } value{device_name, nullptr};
  EnumDisplayMonitors(nullptr, nullptr, [](HMONITOR monitor, HDC, LPRECT, LPARAM parameter) -> BOOL {
    auto* state = reinterpret_cast<context*>(parameter);
    MONITORINFOEXW info{};
    info.cbSize = sizeof(info);
    if (GetMonitorInfoW(monitor, &info) && _wcsicmp(info.szDevice, state->name) == 0) {
      state->monitor = monitor;
      return FALSE;
    }
    return TRUE;
  }, reinterpret_cast<LPARAM>(&value));
  return value.monitor;
}

uint32_t rotation_degrees(DISPLAYCONFIG_ROTATION rotation) {
  switch (rotation) {
    case DISPLAYCONFIG_ROTATION_ROTATE90: return 90;
    case DISPLAYCONFIG_ROTATION_ROTATE180: return 180;
    case DISPLAYCONFIG_ROTATION_ROTATE270: return 270;
    default: return 0;
  }
}
}

std::vector<display_record> enumerate_displays(rs_runtime_t* runtime, bool update_generation) {
  UINT32 path_count = 0;
  UINT32 mode_count = 0;
  if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, &path_count, &mode_count) != ERROR_SUCCESS) return {};
  std::vector<DISPLAYCONFIG_PATH_INFO> paths(path_count);
  std::vector<DISPLAYCONFIG_MODE_INFO> modes(mode_count);
  if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, &path_count, paths.data(), &mode_count, modes.data(), nullptr) != ERROR_SUCCESS) return {};
  paths.resize(path_count);

  std::vector<display_record> result;
  for (const auto& path : paths) {
    DISPLAYCONFIG_SOURCE_DEVICE_NAME source{};
    source.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
    source.header.size = sizeof(source);
    source.header.adapterId = path.sourceInfo.adapterId;
    source.header.id = path.sourceInfo.id;
    if (DisplayConfigGetDeviceInfo(&source.header) != ERROR_SUCCESS) continue;

    DISPLAYCONFIG_TARGET_DEVICE_NAME target{};
    target.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
    target.header.size = sizeof(target);
    target.header.adapterId = path.targetInfo.adapterId;
    target.header.id = path.targetInfo.id;
    if (DisplayConfigGetDeviceInfo(&target.header) != ERROR_SUCCESS) continue;

    display_record record;
    record.id = utf8_from_wide(target.monitorDevicePath);
    record.name = utf8_from_wide(target.monitorFriendlyDeviceName);
    record.gdi_name = source.viewGdiDeviceName;
    record.monitor = monitor_for_device(source.viewGdiDeviceName);
    record.adapter_luid = path.targetInfo.adapterId;
    record.rotation_degrees = rotation_degrees(path.targetInfo.rotation);
    if (record.monitor != nullptr) {
      MONITORINFO info{};
      info.cbSize = sizeof(info);
      if (GetMonitorInfoW(record.monitor, &info)) {
        record.bounds = info.rcMonitor;
        if ((info.dwFlags & MONITORINFOF_PRIMARY) != 0) record.flags |= 1u;
      }
      UINT dpi_x = 96;
      UINT dpi_y = 96;
      if (SUCCEEDED(GetDpiForMonitor(record.monitor, MDT_EFFECTIVE_DPI, &dpi_x, &dpi_y))) {
        record.dpi_x = dpi_x;
        record.dpi_y = dpi_y;
      }
    }
    if (record.id.empty()) record.id = utf8_from_wide(source.viewGdiDeviceName);
    if (record.name.empty()) record.name = record.id;
    result.push_back(std::move(record));
  }
  std::sort(result.begin(), result.end(), [](const display_record& left, const display_record& right) { return left.id < right.id; });

  if (runtime != nullptr && update_generation) {
    std::ostringstream signature;
    for (const auto& display : result) {
      signature << display.id << ':' << display.bounds.left << ':' << display.bounds.top << ':'
                << display.bounds.right << ':' << display.bounds.bottom << ':' << display.rotation_degrees << ':'
                << display.dpi_x << ':' << display.dpi_y << ';';
    }
    std::scoped_lock lock(runtime->topology_mutex);
    if (runtime->topology_signature.empty()) {
      runtime->topology_signature = signature.str();
    } else if (runtime->topology_signature != signature.str()) {
      runtime->topology_signature = signature.str();
      runtime->topology_generation.fetch_add(1, std::memory_order_relaxed);
    }
  }
  return result;
}

std::optional<display_record> find_display(rs_runtime_t* runtime, const std::string& id) {
  auto displays = enumerate_displays(runtime, true);
  if (id.empty()) {
    const auto primary = std::find_if(displays.begin(), displays.end(), [](const display_record& display) { return (display.flags & 1u) != 0; });
    if (primary != displays.end()) return *primary;
    if (!displays.empty()) return displays.front();
    return std::nullopt;
  }
  const auto match = std::find_if(displays.begin(), displays.end(), [&](const display_record& display) { return display.id == id; });
  return match == displays.end() ? std::nullopt : std::optional<display_record>(*match);
}

extern "C" rs_status_v1 RS_CALL rs_runtime_enumerate_displays(rs_runtime_handle runtime, rs_display_info_callback_v1 callback, void* callback_context) {
  if (runtime == nullptr || callback == nullptr) return RS_STATUS_INVALID_ARGUMENT;
  auto displays = enumerate_displays(runtime, true);
  const uint64_t generation = runtime->topology_generation.load(std::memory_order_relaxed);
  for (const auto& display : displays) {
    rs_display_info_v1 info{};
    info.struct_size = sizeof(info);
    info.display_id_utf8 = string_view(display.id);
    info.device_name_utf8 = string_view(display.name);
    info.desktop_x = display.bounds.left;
    info.desktop_y = display.bounds.top;
    info.width = static_cast<uint32_t>((std::max)(0L, display.bounds.right - display.bounds.left));
    info.height = static_cast<uint32_t>((std::max)(0L, display.bounds.bottom - display.bounds.top));
    info.rotation_degrees = display.rotation_degrees;
    info.dpi_x = display.dpi_x;
    info.dpi_y = display.dpi_y;
    info.adapter_luid_low = display.adapter_luid.LowPart;
    info.adapter_luid_high = display.adapter_luid.HighPart;
    info.display_generation = generation;
    info.flags = display.flags;
    callback(callback_context, &info);
  }
  return displays.empty() ? RS_STATUS_NOT_SUPPORTED : RS_STATUS_OK;
}
