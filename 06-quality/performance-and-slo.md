# Performance, SLI and SLO

## 1. Client SLIs

- capture FPS and capture delay;
- encoder input/output queue delay;
- encoded bitrate and keyframe count;
- packet loss, RTT and jitter;
- decode/render delay;
- input event acknowledgment delay;
- dropped/coalesced frames;
- CPU/GPU/memory usage;
- crash-free sessions.

## 2. Server SLIs

- API availability and latency;
- WebSocket connect success and active count;
- session creation/authorization success;
- TURN credential issuance latency;
- policy decision latency;
- audit outbox delay;
- database saturation;
- error rate by stable code.

## 3. TURN SLIs

- allocation success;
- relay route establishment;
- packets/bytes;
- packet drop/socket errors;
- CPU/NIC/port-range saturation;
- auth failures and suspicious churn.

## 4. Initial internal objectives

| Objective | Target before attended GA |
|---|---|
| Control API availability | 99.9% monthly internal objective |
| Session authorization p95 | < 1 second excluding human consent |
| Connect success where network route exists | ≥ 98% in defined test population |
| Connection setup p95 | < 10 seconds |
| Crash-free session rate | ≥ 99.9% |
| Update success | ≥ 99.5% on eligible healthy devices |
| Audit outbox p99 delay | < 60 seconds |

These are engineering readiness targets and must be revised from beta measurements before contractual SLA publication.

## 5. Quality scorecard

A release dashboard includes:

- direct/relay ratio;
- connect failure reason distribution;
- median/p95 latency;
- session crash/disconnect rate;
- top Windows/GPU-specific failures;
- update health;
- abuse and security alerts;
- cost per relayed session-hour.
