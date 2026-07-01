# Transport Metrics

The ABI returns a bounded snapshot through `rs_transport_get_stats`:

| Field | Meaning |
|---|---|
| `rtt_ms` | Reliable-control heartbeat RTT when available |
| `available_outgoing_bitrate_bps` | Current encoder/REMB target |
| `actual_video_bitrate_bps` | Session-average encoded video payload rate |
| `bytes_sent`, `bytes_received` | Process-side WebRTC media and data payload bytes |
| `data_channel_buffered_bytes` | Sum of current SCTP channel buffers |
| `route_class` | Direct UDP/TCP or TURN UDP/TCP/TLS classification |

Session IDs, peer IDs, SDP, ICE candidates, TURN usernames and credentials are
not metric dimensions. Production aggregation uses bounded tenant, region,
route and transport labels supplied by the managed layer.
