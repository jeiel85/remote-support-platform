# Remote Support Platform — Production Design Bundle

**Revision:** 1.3 final re-audited  
**Target:** Windows-first commercial remote-support platform  
**Status:** Implementation-ready contracts; production release remains conditional on goal evidence and release gates.

## What this bundle fixes

The final audit separates attended GA, Managed Host, and unattended release trains; defines the missing managed-device command and credential lifecycle; makes consent/device decisions cryptographically sender-constrained; binds WebRTC DTLS fingerprints to peer authorization; defines update/audit canonicalization; adds Admin Portal and tenant-governance APIs; strengthens tenant-bound database relationships; and adds explicit codec/legal gates.

## Start here

1. `FINAL_AUDIT_REPORT.md`
2. `IMPLEMENTATION_READINESS.md`
3. `START_HERE.md`
4. `07-delivery/implementation-order.md`
5. `07-delivery/goals/goal-01-foundation.md`

## Release sequence

- Goals 01–12: attended GA.
- Goal 13: separately released Managed Host foundation.
- Goal 14: separately approved unattended access.

The canonical machine-readable contracts are OpenAPI, Protobuf, JSON Schemas, native C ABI header, PostgreSQL schema, requirements traceability CSV and acceptance-test CSV. Markdown explains intent but does not override those contracts.
