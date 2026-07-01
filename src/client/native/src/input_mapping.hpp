#pragma once

#include <cstdint>
#include <optional>

struct virtual_desktop_rect {
  int32_t left{};
  int32_t top{};
  int32_t width{};
  int32_t height{};
};

inline std::optional<uint16_t> normalize_absolute_axis(int32_t value, int32_t origin, int32_t extent) {
  if (extent <= 1 || value < origin || static_cast<int64_t>(value) >= static_cast<int64_t>(origin) + extent) return std::nullopt;
  const int64_t offset = static_cast<int64_t>(value) - origin;
  return static_cast<uint16_t>((offset * 65'535 + (extent - 2) / 2) / (extent - 1));
}

inline bool normalize_virtual_desktop_point(const virtual_desktop_rect& desktop, int32_t x, int32_t y,
    uint16_t& normalized_x, uint16_t& normalized_y) {
  const auto mapped_x = normalize_absolute_axis(x, desktop.left, desktop.width);
  const auto mapped_y = normalize_absolute_axis(y, desktop.top, desktop.height);
  if (!mapped_x.has_value() || !mapped_y.has_value()) return false;
  normalized_x = *mapped_x;
  normalized_y = *mapped_y;
  return true;
}
