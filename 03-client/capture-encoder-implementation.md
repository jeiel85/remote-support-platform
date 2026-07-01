# Capture and Encoder Implementation

## Native interfaces

```cpp
struct rs_capture_options_v1 {
  uint32_t struct_size;
  uint32_t target_fps;
  uint32_t max_width;
  uint32_t max_height;
  uint32_t flags;
};

struct rs_frame_info_v1 {
  uint32_t struct_size;
  uint64_t frame_id;
  uint64_t timestamp_ns;
  uint32_t width;
  uint32_t height;
  uint32_t rotation;
  uint32_t pixel_format;
};
```

ABI rules:

- all structs contain `struct_size`;
- functions return stable product error codes;
- ownership is documented per handle;
- callbacks never cross into managed code on arbitrary threads without dispatch contract;
- no C++ exceptions cross ABI boundary;
- UTF-8 strings with explicit length.

## Capture lifecycle

1. Create D3D11 device for selected adapter.
2. Open duplication/capture target.
3. Acquire frame with bounded timeout.
4. Process dirty/move rectangles and cursor metadata.
5. Detect access-lost/display-change error.
6. Recreate capture state with exponential bounded retry.
7. Emit capability downgrade if fallback is used.

## Encoder lifecycle

- enumerate hardware transforms;
- test low-latency configuration with a short validation frame path;
- select stable encoder by adapter/vendor/build allow/deny data;
- create color-conversion/scaling pipeline;
- expose dynamic bitrate and resolution controls;
- monitor output timestamps and queue delay;
- fail over to software encoder after bounded recovery.

## Zero-copy goal

Preferred path:

```text
DXGI texture → GPU crop/scale/convert → encoder input texture/sample
```

CPU readback is a fallback, not the default.

## Resource limits

- frame pool size bounded, e.g. 3–6 frames;
- encoder queue depth bounded to low latency;
- no frame retained after newer frame supersedes it unless required as encoder reference;
- GPU allocation recreated on resolution/adapter reset.

## Test hooks

- synthetic frame source;
- forced access-lost event;
- forced hardware encoder failure;
- deterministic color bars and moving grid;
- capture timing and frame-drop counters;
- GPU memory leak detector over repeated monitor switches.
