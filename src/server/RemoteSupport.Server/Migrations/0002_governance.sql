create table if not exists governance_tenant_aggregates (
    tenant_id uuid primary key,
    slug text not null unique,
    status text not null check (status in ('ACTIVE','SUSPENDED','CLOSED')),
    authorization_version bigint not null check (authorization_version >= 1),
    document jsonb not null,
    updated_at timestamptz not null
);

create table if not exists governance_secret_lookups (
    purpose text not null check (purpose in ('INVITATION','ENROLLMENT')),
    secret_hash bytea not null,
    tenant_id uuid not null references governance_tenant_aggregates(tenant_id) on delete cascade,
    resource_id uuid not null,
    expires_at timestamptz not null,
    revoked_at timestamptz,
    primary key (purpose, secret_hash),
    check (octet_length(secret_hash) = 32)
);
create index if not exists ix_governance_secret_expiry on governance_secret_lookups(expires_at)
    where revoked_at is null;

create table if not exists governance_audit_events (
    id uuid primary key,
    tenant_id uuid not null references governance_tenant_aggregates(tenant_id) on delete cascade,
    chain_sequence bigint not null check (chain_sequence >= 1),
    category text not null,
    action text not null,
    outcome text not null check (outcome in ('SUCCEEDED','FAILED','DENIED','CANCELLED')),
    actor_type text not null,
    actor_id text,
    target_type text,
    target_id text,
    correlation_id uuid not null,
    occurred_at timestamptz not null,
    details jsonb not null,
    previous_hash bytea,
    event_hash bytea not null,
    unique (tenant_id, chain_sequence),
    check (previous_hash is null or octet_length(previous_hash) = 32),
    check (octet_length(event_hash) = 32)
);
create index if not exists ix_governance_audit_tenant_time
    on governance_audit_events(tenant_id, occurred_at desc);

alter table governance_tenant_aggregates enable row level security;
alter table governance_audit_events enable row level security;

drop policy if exists governance_tenant_isolation on governance_tenant_aggregates;
create policy governance_tenant_isolation on governance_tenant_aggregates
    using (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
    with check (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);

drop policy if exists governance_audit_isolation on governance_audit_events;
create policy governance_audit_isolation on governance_audit_events
    using (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
    with check (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);

create or replace function reject_governance_audit_mutation() returns trigger
language plpgsql as $$
begin
    raise exception 'governance audit events are append-only' using errcode = '42501';
end;
$$;

drop trigger if exists governance_audit_no_update on governance_audit_events;
create trigger governance_audit_no_update before update or delete on governance_audit_events
    for each row execute function reject_governance_audit_mutation();

comment on table governance_secret_lookups is
    'Contains only HMAC-SHA-256 lookup values. Raw invitation and enrollment secrets are never persisted.';
comment on table governance_audit_events is
    'Append-only tenant audit chain. Application writes are inserts only; verification detects privileged tampering.';
