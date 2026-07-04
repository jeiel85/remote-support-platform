# Goal 11 failure drills

Run `python tools/operations/run_goal11_drills.py .` on each release candidate,
then execute the provider-backed drills below in an isolated recovery
environment before Goal 12 approval.

The deterministic local drill verifies backup plus WAL replay and audit-chain
continuity, two-node TURN drain/replacement selection, alert thresholds for
allocation failure/audit backlog/signature failure, privacy canaries and the
checked-in capacity arithmetic. It is fast feedback, not a substitute for
PostgreSQL PITR or live coturn measurements.

The provider-backed record must identify exact database backup/WAL IDs,
PostgreSQL version, coturn image digest, VM/NIC/kernel profile, timestamps,
observer and approver. Record achieved data loss and wall-clock recovery time;
tenant-isolation queries, audit/outbox replay and synthetic attended session
must pass before declaring the restore usable. Drain one of at least two TURN
nodes, verify it disappears from credential responses, preserve existing
sessions where possible, replace from the immutable image, and measure
allocation success, p95 latency, packet drops, CPU, NIC and relay-port pressure.
