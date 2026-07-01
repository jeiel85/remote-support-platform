# ADR-0005: No Control-Plane Storage of Session Content

- Status: Accepted
- Date: 2026-07-01

## Decision

Do not store screen frames, keystrokes, clipboard contents or transferred-file contents in the SaaS control plane. Chat is peer-only by default. Recording is a future separately consented product.

## Consequences

- Reduces privacy, breach and storage risk.
- Limits retrospective content investigation.
- Diagnostic tooling must rely on metrics and user-approved captures.
