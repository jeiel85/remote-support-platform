# Goal 08 — Clipboard, Chat and File Transfer

## Goal objective

Add content-bearing data features with explicit scopes, robust parsers and safe local handling.

## Required work

1. Implement text clipboard sync with direction scopes and loop prevention.
2. Implement peer-only chat.
3. Implement file manifest, acceptance, chunk, resume and hash verification.
4. Implement safe destination selection and filename normalization.
5. Apply external-source marking and local AV/attachment integration where available.
6. Add size/type/concurrency policies and backpressure.
7. Add cancellation and disk-space handling.
8. Fuzz parsers and add traversal/ADS/reserved-name tests.

## Acceptance criteria

- Revoked scope immediately blocks clipboard/file action.
- Clipboard content never appears in logs.
- Transfer resumes only for matching manifest/hash.
- Path traversal and unsafe names cannot escape destination.
- File never auto-opens or executes.
- Final size/hash mismatch fails atomically.
- Large transfer keeps memory bounded and UI responsive.

## Forbidden shortcuts

- No arbitrary sender-chosen absolute path.
- No automatic executable launch.
- No unbounded DataChannel buffering.
- No rich clipboard formats in this goal.

## Required deliverables

- clipboard/chat/file modules;
- policy controls;
- parser fuzz targets;
- file safety test report;
- updated audit catalog.

## Audited contract inputs

The implementation for this goal must treat the following files as binding inputs:

- `02-protocol/file-transfer-protocol.md`
- `02-protocol/control-channel.md`
- `02-protocol/protobuf/remote_support.proto`
- `09-templates/error-codes.md`

## Evidence package

The goal is complete only when the repository contains:

- runnable implementation and deterministic setup instructions;
- automated tests mapped to the applicable IDs in `07-delivery/acceptance-test-catalog.md`;
- performance/security evidence required by the goal acceptance criteria;
- updated contract, ADR, risk, and compatibility records when behavior differs from the design;
- no unresolved placeholder implementation, disabled security gate, or undocumented manual step.
