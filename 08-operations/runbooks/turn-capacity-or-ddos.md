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

## Restrictions

- Do not enable anonymous TURN.
- Do not allow relay to private networks.
- Do not expose management interface.
- Do not indiscriminately block entire customer regions without incident approval.
