# Document Index

Generated for the final re-audited bundle. Machine-readable contracts are marked **contract**.

## ROOT

- `FINAL_AUDIT_REPORT.md`
- `IMPLEMENTATION_READINESS.md`
- `README.md`
- `START_HERE.md`

## 00-product

- `00-product/commercialization-and-compliance.md`
- `00-product/data-processing-agreement.md`
- `00-product/functional-requirements.md`
- `00-product/nonfunctional-requirements.md`
- `00-product/privacy-notice.md`
- `00-product/product-scope.md`
- `00-product/risk-register.md`
- `00-product/terms-of-service.md`
- `00-product/user-flows.md`

## 01-architecture

- `01-architecture/adr/0001-modular-monolith.md`
- `01-architecture/adr/0002-native-media-core.md`
- `01-architecture/adr/0003-webrtc-transport.md`
- `01-architecture/adr/0004-attended-default.md`
- `01-architecture/adr/0005-no-content-storage.md`
- `01-architecture/adr/0006-signed-update-chain.md`
- `01-architecture/data-architecture.md`
- `01-architecture/media-pipeline.md`
- `01-architecture/networking-and-turn.md`
- `01-architecture/runtime-sequences.md`
- `01-architecture/system-architecture.md`
- `01-architecture/windows-process-model.md`

## 02-protocol

- `02-protocol/api-flow-contract.md`
- `02-protocol/canonicalization-and-signatures.md`
- `02-protocol/control-channel.md`
- `02-protocol/error-model.md`
- `02-protocol/file-transfer-protocol.md`
- `02-protocol/ipc/service_ipc.proto` — **contract**
- `02-protocol/managed-host-command-channel.md`
- `02-protocol/native/remote_support_native.h` — **contract**
- `02-protocol/openapi/openapi.yaml` — **contract**
- `02-protocol/protobuf/remote_support.proto` — **contract**
- `02-protocol/schemas/appsettings.schema.json` — **contract**
- `02-protocol/schemas/audit-event.schema.json` — **contract**
- `02-protocol/schemas/client-config.schema.json` — **contract**
- `02-protocol/schemas/policy-document.schema.json` — **contract**
- `02-protocol/schemas/update-manifest.schema.json` — **contract**
- `02-protocol/schemas/update-root.schema.json` — **contract**
- `02-protocol/session-state-machine.md`
- `02-protocol/signaling-protocol.md`

## 03-client

- `03-client/accessibility-localization.md`
- `03-client/admin-portal.md`
- `03-client/capture-encoder-implementation.md`
- `03-client/media-kernel-contract.md`
- `03-client/client-solution-structure.md`
- `03-client/elevation-broker.md`
- `03-client/input-clipboard.md`
- `03-client/installer-updater.md`
- `03-client/local-data-and-logs.md`
- `03-client/native-bridge-contract.md`
- `03-client/operator-console.md`
- `03-client/portable-agent.md`
- `03-client/webrtc-integration.md`
- `03-client/windows-service.md`

## 04-backend

- `04-backend/authorization-matrix.md`
- `04-backend/backend-structure.md`
- `04-backend/capacity-and-cost.md`
- `04-backend/database-schema.sql` — **contract**
- `04-backend/deployment-topology.md`
- `04-backend/domain-model.md`
- `04-backend/migration-policy.md`
- `04-backend/policy-engine.md`
- `04-backend/signaling-service.md`
- `04-backend/turn-service.md`

## 05-security

- `05-security/abuse-prevention.md`
- `05-security/audit-and-incident.md`
- `05-security/identity-and-access.md`
- `05-security/key-management.md`
- `05-security/secure-sdlc.md`
- `05-security/security-architecture.md`
- `05-security/threat-model.md`
- `05-security/unattended-threat-model.md`

## 06-quality

- `06-quality/av-edr-false-positive-process.md`
- `06-quality/compatibility-matrix.md`
- `06-quality/observability.md`
- `06-quality/penetration-test-scope-and-closure.md`
- `06-quality/performance-and-slo.md`
- `06-quality/release-gates.md`
- `06-quality/supported-capability-matrix.md`
- `06-quality/test-strategy.md`

## 07-delivery

- `07-delivery/acceptance-test-cases.csv` — **contract**
- `07-delivery/acceptance-test-catalog.md`
- `07-delivery/beta-program.md`
- `07-delivery/bootstrap-and-build.md`
- `07-delivery/ci-cd.md`
- `07-delivery/coding-standards.md`
- `07-delivery/goals/goal-01-foundation.md`
- `07-delivery/goals/goal-02-capture-render.md`
- `07-delivery/goals/goal-03-encoding.md`
- `07-delivery/goals/goal-04-lan-transport.md`
- `07-delivery/goals/goal-05-input-topology.md`
- `07-delivery/goals/goal-06-control-plane-consent.md`
- `07-delivery/goals/goal-07-internet-turn.md`
- `07-delivery/goals/goal-08-data-features.md`
- `07-delivery/goals/goal-09-attended-packaging.md`
- `07-delivery/goals/goal-10-tenancy-policy-audit.md`
- `07-delivery/goals/goal-11-update-observability-ops.md`
- `07-delivery/goals/goal-12-attended-ga.md`
- `07-delivery/goals/goal-13-managed-host.md`
- `07-delivery/goals/goal-14-unattended.md`
- `07-delivery/implementation-order.md`
- `07-delivery/product-backlog.md`
- `07-delivery/release-gate-approval.md`
- `07-delivery/repository-structure.md`
- `07-delivery/traceability/requirements-traceability.csv` — **contract**
- `07-delivery/traceability/requirements-traceability.md`

## 08-operations

- `08-operations/disaster-recovery-plan.md`
- `08-operations/failure-drill-plan.md`
- `08-operations/operations-overview.md`
- `08-operations/runbooks/control-plane-outage.md`
- `08-operations/runbooks/database-restore.md`
- `08-operations/runbooks/security-incident.md`
- `08-operations/runbooks/signing-key-compromise.md`
- `08-operations/runbooks/abuse-response.md`
- `08-operations/runbooks/support-bundle-collection.md`
- `08-operations/runbooks/turn-capacity-or-ddos.md`
- `08-operations/runbooks/update-rollout-incident.md`
- `08-operations/slo-alert-catalog.md`
- `08-operations/tabletop-exercises.md`

## 09-templates

- `09-templates/appsettings.example.json`
- `09-templates/audit-event-catalog.md`
- `09-templates/audit-event.example.json`
- `09-templates/client-config.example.json`
- `09-templates/coturn.production.example.conf`
- `09-templates/definition-of-done.md`
- `09-templates/docker-compose.dev.yml`
- `09-templates/error-codes.md`
- `09-templates/policy-document.example.json`
- `09-templates/security-review-template.md`
- `09-templates/update-manifest.example.json`
- `09-templates/update-root.example.json`

## 10-references

- `10-references/dependency-and-license-policy.md`
- `10-references/official-references.md`
- `10-references/version-baseline.md`

## 11-bootstrap

- `11-bootstrap/README.md`
- `11-bootstrap/initialize-repository.ps1`
- `11-bootstrap/requirements.txt`
- `11-bootstrap/validate-bundle.py`
