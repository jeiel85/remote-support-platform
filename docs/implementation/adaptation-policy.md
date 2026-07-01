# Video Adaptation Policy

`RemoteSupport.Media` consumes estimated bandwidth, loss, RTT, jitter, encoder queue pressure, capture-to-send latency, CPU/GPU utilization, dirty area, and motion. It emits bitrate, frame rate, even output dimensions, keyframe intent, and emergency state through `IEncoderAdaptationSink`.

| Profile | FPS range | Minimum scale | Maximum bitrate | Bias |
|---|---:|---:|---:|---|
| TEXT | 8–24 | 0.75 | 8 Mbps | Preserve resolution and cap temporal demand |
| BALANCED | 12–45 | 0.50 | 12 Mbps | Default compromise |
| MOTION | 18–60 | 0.50 | 16 Mbps | Preserve temporal smoothness |

Downshift is immediate. Recovery requires three consecutive healthy samples, and a resolution recovery requests a keyframe. Emergency pressure is loss ≥12%, RTT ≥450 ms, queue ≥90%, capture-to-send latency ≥250 ms, or CPU/GPU utilization ≥95%. The frame buffer accepts capacities one through six and removes the oldest superseded frame when full, so latency cannot grow without bound.

Automated sweeps cover 0.5–10 Mbps, loss/RTT/queue/resource pressure, recovery hysteresis, even dimensions, safe profile limits, and oldest-frame eviction. The equal-bandwidth test proves that TEXT, BALANCED, and MOTION choose distinct frame-rate/scale behavior. Perceptual text/chart/video scoring and human reviewer evidence remain a protected-lab release activity.
