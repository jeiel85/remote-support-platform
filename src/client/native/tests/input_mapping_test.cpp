#include "input_mapping.hpp"

#include <array>

int main() {
  const std::array<virtual_desktop_rect, 3> desktops{{
      {-1920, -200, 3840, 1280},
      {0, 0, 7680, 4320},
      {-1080, -1920, 1080, 1920}}};
  for (const auto& desktop : desktops) {
    uint16_t x = 0;
    uint16_t y = 0;
    if (!normalize_virtual_desktop_point(desktop, desktop.left, desktop.top, x, y) || x != 0 || y != 0) return 1;
    if (!normalize_virtual_desktop_point(desktop, desktop.left + desktop.width - 1,
        desktop.top + desktop.height - 1, x, y) || x != 65'535 || y != 65'535) return 2;
    if (!normalize_virtual_desktop_point(desktop, desktop.left + desktop.width / 2,
        desktop.top + desktop.height / 2, x, y) || x < 32'767 || y < 32'767) return 3;
    if (normalize_virtual_desktop_point(desktop, desktop.left - 1, desktop.top, x, y) ||
        normalize_virtual_desktop_point(desktop, desktop.left + desktop.width, desktop.top, x, y)) return 4;
  }
  if (normalize_absolute_axis(0, 0, 1).has_value()) return 5;
  return 0;
}
