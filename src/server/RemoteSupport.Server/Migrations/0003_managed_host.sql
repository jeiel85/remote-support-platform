-- Cross-tenant device-id lookup so anonymous device-credential endpoints
-- (challenge/exchange) can locate a device's tenant before tenant context is
-- established, mirroring governance_secret_lookups. Contains no secret
-- material, so it carries no RLS policy, matching that table's pattern.
create table if not exists governance_device_lookups (
    device_id uuid primary key,
    tenant_id uuid not null references governance_tenant_aggregates(tenant_id) on delete cascade
);

-- Managed/host-pending sessions have no human-entered support code and no
-- host peer bound at creation time.
alter table attended_session_aggregates alter column code_lookup_hash drop not null;
drop index if exists attended_session_aggregates_code_lookup_hash_key;
alter table attended_session_aggregates drop constraint if exists attended_session_aggregates_code_lookup_hash_key;
create unique index if not exists ux_attended_sessions_code_lookup_hash
    on attended_session_aggregates(code_lookup_hash) where code_lookup_hash is not null;

alter table attended_session_aggregates drop constraint if exists attended_session_aggregates_state_check;
alter table attended_session_aggregates add constraint attended_session_aggregates_state_check
    check (state in (
      'WAITING_FOR_OPERATOR','CONSENT_PENDING','HOST_PENDING','AUTHORIZED',
      'EXPIRED','REJECTED','FAILED','CANCELLED','ENDED'
    ));
