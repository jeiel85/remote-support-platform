create table if not exists attended_session_aggregates (
    id uuid primary key,
    tenant_id uuid,
    state text not null check (state in (
      'WAITING_FOR_OPERATOR','CONSENT_PENDING','AUTHORIZED','EXPIRED','REJECTED','FAILED','CANCELLED','ENDED'
    )),
    state_version bigint not null check (state_version >= 1),
    code_lookup_hash bytea not null unique check (octet_length(code_lookup_hash) = 32),
    expires_at timestamptz not null,
    document jsonb not null,
    updated_at timestamptz not null
);
create index if not exists ix_attended_sessions_expiry on attended_session_aggregates(expires_at)
  where state not in ('EXPIRED','REJECTED','FAILED','CANCELLED','ENDED');

create table if not exists attended_audit_events (
    id uuid primary key,
    session_id uuid not null references attended_session_aggregates(id) on delete cascade,
    tenant_id uuid,
    chain_sequence bigint not null check (chain_sequence >= 1),
    action text not null,
    outcome text not null,
    actor_type text not null,
    actor_id text,
    occurred_at timestamptz not null,
    state_version bigint not null,
    details jsonb not null
    ,previous_hash bytea
    ,event_hash bytea not null check (octet_length(event_hash) = 32)
    ,unique (session_id, chain_sequence)
    ,check (previous_hash is null or octet_length(previous_hash) = 32)
);
create index if not exists ix_attended_audit_session on attended_audit_events(session_id, occurred_at);

create table if not exists outbox_messages (
    id uuid primary key,
    tenant_id uuid,
    occurred_at timestamptz not null,
    type text not null,
    payload jsonb not null,
    attempts integer not null default 0 check (attempts >= 0),
    next_attempt_at timestamptz not null,
    processed_at timestamptz,
    last_error_code text
);
create index if not exists ix_outbox_pending on outbox_messages(next_attempt_at) where processed_at is null;

comment on column attended_session_aggregates.document is
  'Contains public peer keys, verified display identity, scopes and cryptographic hashes only. Raw support codes, tokens, nonces and signatures are never persisted.';
