# SLO and Alert Catalog

## 1. Principles

- Alerts are based on user-visible symptoms or imminent capacity exhaustion.
- Every page links to a runbook and owning service.
- Metrics exclude high-cardinality raw tenant/session IDs from primary labels.
- Security alerts remain separate from availability paging when response ownership differs.

## 2. Initial service objectives

| Service | SLI | Internal objective | Measurement window |
|---|---|---:|---|
| Control API | successful eligible requests / eligible requests | 99.9% | rolling 30 days |
| Session authorization | p95 server latency excluding human consent | < 1 s | rolling 1 h and 24 h |
| Signaling | authenticated socket establishment success | ≥ 99.5% | rolling 1 h |
| Defined-network connection | sessions connected when a tested route exists | ≥ 98% | release test population |
| TURN | allocation success for valid credentials | ≥ 99.5% | rolling 1 h |
| Audit pipeline | event durable/exported p99 delay | < 60 s | rolling 1 h |
| Client update | successful eligible installations | ≥ 99.5% | release cohort |

These are internal launch objectives, not customer SLA commitments.

## 3. Paging alerts

| Alert | Condition | Minimum duration | Severity | Runbook |
|---|---|---:|---|---|
| ControlApiFastBurn | 1 h error-budget burn > 14.4x | 5 min | SEV-2 | control-plane outage |
| ControlApiSlowBurn | 6 h burn > 6x | 30 min | SEV-2/3 | control-plane outage |
| SessionAuthorizationFailure | valid authorization failures > 5% and volume threshold met | 10 min | SEV-2 | control-plane outage |
| SignalingConnectFailure | connection failures > 5% by region/version | 10 min | SEV-2 | control-plane outage |
| TurnAllocationFailure | valid allocation failures > 3% by region | 5 min | SEV-2 | TURN capacity/DDoS |
| TurnPortExhaustion | available relay ports < 20% | 5 min | SEV-2 | TURN capacity/DDoS |
| TurnNicSaturation | sustained NIC > 75% or packet drops rising | 10 min | SEV-2 | TURN capacity/DDoS |
| AuditPipelineStalled | oldest unprocessed security event > 5 min | 5 min | SEV-2 | security incident |
| UpdateFailureSpike | cohort failure > 2% or boot-loop signal | 10 min | SEV-1/2 | signing/update incident |
| SigningAnomaly | unexpected signer/key/release sequence | immediate | SEV-1 | signing-key compromise |
| CrossTenantAccessSignal | any confirmed tenant-isolation violation | immediate | SEV-1 | security incident |

Low-volume alerts use absolute-count guards to avoid noisy percentages.

## 4. Ticket alerts

- database storage or connection pool trend;
- certificate/metadata expiry at 30/14/7/3/1 days;
- dependency vulnerability requiring action;
- client version below support threshold;
- repeated GPU/driver-specific crash clusters;
- abuse-report backlog and SLA breach;
- backup/restore drill overdue;
- TURN cost anomaly by region/tenant.

## 5. Required dimensions

Allowed bounded labels include service, region, release channel, app major/minor, route class, transport type, error code, OS family/build bucket, GPU vendor bucket, and tenant plan class. Tenant IDs are restricted to drill-down logs/traces with access controls, not metric labels.
