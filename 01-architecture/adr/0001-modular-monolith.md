# ADR-0001: Start with a Modular Monolith Control Plane

- Status: Accepted
- Date: 2026-07-01

## Context

The product requires identity, tenant, session, signaling, policy, audit, update and metering capabilities. Splitting each into a service at inception would increase deployment, consistency and observability burden before scale evidence exists.

## Decision

Use one ASP.NET Core deployment with strict modules, internal interfaces, module-owned schemas/tables and an outbox. Signaling may run in the same deployment initially but retains a separable interface.

## Consequences

- Faster delivery and simpler transactions.
- Requires architectural tests to prevent boundary erosion.
- Modules can be extracted when independent scaling or release cadence is demonstrated.
