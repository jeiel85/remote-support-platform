# Runbook — Control Plane Outage

## Detect

- API availability/latency alerts;
- signaling connection failure spike;
- database or dependency health alert.

## Triage

1. Declare severity and incident channel.
2. Confirm scope by region, endpoint and operation.
3. Check load balancer, deployment, database, cache and certificate state.
4. Determine whether active peer sessions continue.
5. Freeze risky deployments and preserve logs/metrics.

## Mitigate

- rollback recent compatible deployment;
- fail over healthy instances/zone;
- restore database connectivity or activate documented DR;
- disable non-essential background work if it threatens core session paths;
- maintain audit durability.

## Never

- bypass authentication/authorization;
- accept unverified tokens;
- expose database publicly;
- delete evidence to recover disk without approval.

## Recover

- validate session create/resolve/consent/signaling;
- watch error and saturation metrics;
- communicate recovery and residual limitations;
- complete retrospective and tests.
