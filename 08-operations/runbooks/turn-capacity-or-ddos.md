# Runbook — TURN Capacity or DDoS

## Signals

- allocation failures;
- NIC/packet drop/CPU pressure;
- abnormal auth failures or allocation churn;
- egress cost spike;
- relay port exhaustion.

## Actions

1. Identify region/node/source/tenant pattern.
2. Confirm whether traffic is legitimate load or abuse.
3. Add capacity from immutable image if within validated model.
4. Drain unhealthy node from credential issuance.
5. Apply source/tenant rate controls and revoke suspect credentials.
6. Coordinate provider DDoS/network controls.
7. Preserve metrics and minimal logs.
8. Verify direct sessions and unaffected regions remain healthy.

## Node replacement drill

1. Confirm at least two independently restartable nodes are advertised in the region.
2. Mark one node draining and verify new credential responses omit it before stopping the process.
3. Preserve allocation-success, p95 allocation latency, CPU, NIC, packet-drop and free-port metrics.
4. Replace it from the pinned immutable image/configuration and run authenticated UDP, TCP and TLS allocations.
5. Re-advertise only after health and capacity checks pass; record drain and restoration timestamps.

The drill record includes image digest, host/kernel/NIC profile, existing
allocation impact, achieved regional restoration time and the alert delivery
timestamp. A single-node compose smoke test does not satisfy the regional drill.

## Restrictions

- Do not enable anonymous TURN.
- Do not allow relay to private networks.
- Do not expose management interface.
- Do not indiscriminately block entire customer regions without incident approval.
