# Capacity and Cost Model

## 1. Primary cost drivers

1. TURN egress and public IP/network processing.
2. Control-plane database and observability volume.
3. Artifact distribution.
4. Support/abuse operations.

Screen data is not stored, so object storage should not dominate unless recording is later enabled.

## 2. Session bandwidth estimate

For each session:

```text
avg_video_mbps = weighted average by quality profile
relay_probability = fraction requiring TURN
monthly_relay_GB ≈ sessions × avg_duration_seconds × avg_video_mbps / 8 / 1000 × relay_probability
```

Add both provider-specific ingress/egress accounting and protocol overhead. Use measured traffic by screen type; synthetic video tests alone overestimate office-work traffic.

## 3. Metering dimensions

- tenant;
- session type;
- direct/relay;
- relay region and protocol;
- duration;
- bytes in/out;
- peak and average bitrate;
- file transfer bytes;
- failed negotiation reason.

## 4. Cost controls

- plan quotas and alerts;
- idle bitrate convergence;
- max resolution/frame rate by plan;
- geographic relay selection;
- abuse rate limits;
- relay-only mode reserved for environments that need it;
- automatic termination of abandoned sessions after policy timeout.

## 5. Load-test milestones

- 1,000 concurrent signaling sockets.
- 10,000 concurrent signaling sockets.
- TURN tests at 25%, 50%, 75% of intended node capacity.
- allocation churn and reconnect storm.
- packet loss and MTU fragmentation scenarios.
- database audit/metering ingestion under burst.

Scale targets must be updated with test evidence, not extrapolation alone.
