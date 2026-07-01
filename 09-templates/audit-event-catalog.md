# Audit Event Catalog

Audit event names and required detail keys are stable contracts. Detail values are schema-validated allowlists; arbitrary request bodies are never copied into audit data.

| Event | Required fields |
|---|---|
| `identity.sign_in.succeeded` | actor, tenant?, MFA state, source class, correlation |
| `identity.sign_in.failed` | normalized reason, source class, correlation |
| `identity.step_up.succeeded` | actor, tenant, method, authentication age |
| `membership.role.changed` | actor, target user, before/after roles, privilege version |
| `tenant.status.changed` | actor, before/after status, reason reference |
| `device.enrollment_token.created` | actor, tenant, expiry, max uses, group? |
| `device.enrolled` | actor, tenant, device, key version, installation ID hash |
| `device.key.rotated` | actor/device, old/new key version, result |
| `device.revoked` | actor, device, reason, authorization version |
| `device.heartbeat.state_changed` | device, before/after health state, app version |
| `session.attended.created` | host session ID, expiry, client version |
| `session.code.resolved` | operator, tenant, session, outcome, requested scopes |
| `session.consent.decided` | host, operator, granted scopes, outcome, state version |
| `session.peer.authorized` | peer role, key thumbprint hash, scopes, epoch |
| `session.connected` | peers, route class, policy decision, epoch |
| `session.reconnected` | peer, previous/new epoch, reason |
| `session.scope.changed` | before/after scopes, revision, initiator |
| `session.reboot.requested` | initiator, local decision/policy, reconnect requested |
| `session.ended` | initiator, reason, duration and route summary |
| `session.unattended.requested` | operator, device, MFA, policy decision |
| `session.unattended.started` | operator, device, policy decision, notification result |
| `clipboard.transfer.result` | direction, byte size, outcome, policy result |
| `file.transfer.started` | direction, size, redacted-name policy, policy result |
| `file.transfer.completed` | direction, size, result, duration; optional justified hash |
| `policy.version.created` | actor, policy, version, document hash |
| `policy.version.activated` | actor, policy, before/after active version |
| `audit.export.configured` | actor, endpoint, subscribed events, secret reference only |
| `update.release.published` | release, sequence, artifact hashes, signer IDs |
| `update.release.revoked` | release, actor, reason |
| `update.device.result` | device, release, result/error code, rollback result |
| `abuse.report.received` | report ID, category, session/tenant references if authorized |
| `abuse.case.actioned` | actor, case, action, reason |
| `support.break_glass.used` | employee, approval, tenant, expiry, purpose |
| `security.authorization.revoked` | subject type/id, reason, authorization version |
| `security.ipc.denied` | process identity summary, command category, stable reason |

Never store screen frames, clipboard/chat text, keystrokes, file bytes, credentials, raw tokens, proof signatures, SDP, TURN credentials, or unredacted diagnostic dumps in audit details.

## Final-audit additions

| Event | Required fields |
|---|---|
| `membership.invitation.created` | actor, tenant, invitation ID, normalized email hash, roles, expiry |
| `membership.invitation.accepted` | actor/user, tenant, invitation ID, resulting privilege version |
| `membership.invitation.revoked` | actor, tenant, invitation ID, reason |
| `tenant.data_export.requested` | actor, tenant, request ID, format, inventory version |
| `tenant.data_export.ready` | tenant, request ID, object hash, expiry, record counts |
| `tenant.data_export.downloaded` | actor, tenant, request ID, outcome |
| `tenant.closure.requested` | actor, tenant, request ID, effective time, reauthentication state |
| `tenant.closure.cancelled` | actor, tenant, request ID, state version |
| `tenant.closure.completed` | tenant, request ID, deletion/anonymization evidence references |
| `device.credential.refreshed` | device, key version, authorization version, expiry, outcome |
| `device.credential.challenge_rejected` | device?, purpose, normalized reason, source class |
| `session.managed.requested` | operator, tenant, device, policy decision, requested scopes, expiry |
| `session.managed.delivered` | device, session, delivery attempt, state version |
| `session.managed.host_decided` | device, session, outcome, granted scopes, key thumbprint, state version |
| `session.transport_binding.verified` | session, peer, epoch, permission revision, fingerprint hashes |
| `session.transport_binding.failed` | session, peer?, stable reason, epoch, correlation |
| `security.dpop.replay_rejected` | subject class, key thumbprint, request class, normalized reason |
| `update.root.published` | root version, role thresholds, key IDs, expiry, signer IDs |
| `update.root.rotated` | old/new root versions, old/new signer sets, verification result |
