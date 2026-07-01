# ADR-0004: Attended Support Is the Default Trust Model

- Status: Accepted
- Date: 2026-07-01

## Decision

The portable product supports only attended, explicitly approved sessions. Unattended access exists only in installed managed-host mode and requires explicit enrollment, MFA and policy.

## Consequences

- Lower abuse risk and easier initial trust story.
- Some support cases require local approval for UAC or reconnect.
- Persistent access features cannot leak into the portable agent.
