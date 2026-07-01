# Goal 07 — Internet Connectivity and TURN

## Goal objective

Connect peers over the internet with authenticated signaling and hardened regional TURN fallback.

## Required work

1. Implement WebSocket signaling protocol with connection binding.
2. Add SDP/ICE validation, sequence/epoch and rate limits.
3. Deploy patched coturn with authenticated short-lived credentials.
4. Implement direct, TURN/UDP and TURN/TCP/TLS route policy.
5. Restrict relay destinations and management access.
6. Add relay metrics, quotas and per-session metering.
7. Build network impairment and restrictive firewall lab profiles.
8. Add reconnect behavior that preserves active media when signaling alone drops.

## Acceptance criteria

- Defined NAT/firewall matrix reaches expected route.
- Anonymous or expired TURN credentials fail.
- TURN cannot relay to private/control-plane networks.
- Allocation and bandwidth quotas work.
- Signaling replay/sequence violations fail.
- Route type and quality are visible in diagnostics.
- Load test establishes safe per-node capacity/headroom.

## Forbidden shortcuts

- No static TURN credentials in client package.
- No public TURN admin/metrics endpoint.
- No assumption that UDP is always available.
- No raw customer content stored in signaling/relay logs.

## Required deliverables

- production-like signaling deployment;
- hardened TURN configuration;
- network test results;
- capacity model;
- TURN operations runbook;
- security test evidence.

## Audited contract inputs

The implementation for this goal must treat the following files as binding inputs:

- `01-architecture/networking-and-turn.md`
- `03-client/webrtc-integration.md`
- `04-backend/turn-service.md`
- `09-templates/coturn.production.example.conf`
- `08-operations/runbooks/turn-capacity-or-ddos.md`

## Evidence package

The goal is complete only when the repository contains:

- runnable implementation and deterministic setup instructions;
- automated tests mapped to the applicable IDs in `07-delivery/acceptance-test-catalog.md`;
- performance/security evidence required by the goal acceptance criteria;
- updated contract, ADR, risk, and compatibility records when behavior differs from the design;
- no unresolved placeholder implementation, disabled security gate, or undocumented manual step.
