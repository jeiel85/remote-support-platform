# Runtime Sequences

## 1. Attended session

```mermaid
sequenceDiagram
    participant H as Portable Agent
    participant API as Control Plane API
    participant O as Operator Console
    participant SIG as Signaling Service
    participant TURN as TURN

    H->>H: Generate ephemeral peer key pair
    H->>API: Create attended session + host public key
    API-->>H: sessionId, supportCode, hostBootstrapToken
    H->>API: Open pending-event stream
    O->>API: OIDC-authenticated code resolve + operator public key + requested scopes
    API-->>O: operatorBootstrapToken, consentRequestId
    API-->>H: Verified operator/tenant identity and requested scopes
    H->>API: Consent decision + expected state version
    API-->>H: Authorized state
    O->>API: Exchange bootstrap token + key proof
    H->>API: Exchange bootstrap token + key proof
    API-->>O: scoped peer token
    API-->>H: scoped peer token
    O->>API: Request signaling ticket and TURN credentials
    H->>API: Request signaling ticket and TURN credentials
    O->>SIG: Connect with one-time signaling ticket
    H->>SIG: Connect with one-time signaling ticket
    O->>H: SDP/ICE through authenticated signaling
    alt direct candidate succeeds
        O-->>H: WebRTC direct media/data
    else direct route fails
        O-->>TURN: Relay allocation
        H-->>TURN: Relay allocation
        O-->>H: WebRTC through TURN
    end
```

### Invariants

- Support code lookup does not authenticate either peer.
- Bootstrap credentials cannot access media or TURN and expire quickly.
- Peer tokens are issued only after authorization and proof of possession.
- Signaling tickets are single-use and bound to session, peer, role, epoch, and key thumbprint.
- Consent may grant a strict subset of requested scopes.

## 2. Scope revocation

```mermaid
sequenceDiagram
    participant H as Host Agent
    participant API as Control Plane
    participant O as Operator Console

    H->>API: Revoke scope with stateVersion
    API->>API: Commit session state + outbox atomically
    API-->>H: New permissionRevision and scopes
    API-->>O: Policy update over signaling
    H-->>O: PermissionState over reliable peer channel
    O->>O: Disable UI and release pressed input state
    H->>H: Reject later events with stale permission revision
```

The host enforces revocation locally even if the signaling notification is delayed. The server enforces it for subsequent grants and reconnects.

## 3. Transport reconnect

1. Peer detects transport failure using WebRTC state and heartbeat deadlines.
2. Peer requests a reconnect grant with current peer token, prior epoch, and last permission revision.
3. Server rejects if session, identity, policy, or scopes are no longer valid.
4. Server increments epoch using compare-and-swap and returns a one-time grant.
5. Both peers obtain new signaling tickets; old epoch signaling is rejected.
6. Host releases remotely pressed keys/buttons before accepting new input.
7. File transfers reconcile verified chunk ranges before resuming.

## 4. Reboot continuity

```mermaid
sequenceDiagram
    participant O as Operator
    participant H as User Agent
    participant S as Windows Service
    participant API as Control Plane

    O-->>H: Reboot request
    H->>H: Show local approval if policy requires
    H->>API: Request single-use reboot reconnect grant
    API-->>H: Encrypted scoped grant
    H->>S: IPC store grant + reboot request
    S->>S: Validate command, grant, caller and policy
    S->>S: Initiate reboot
    S->>S: Start after boot and detect eligible interactive session
    S->>H: Launch signed Agent in user session
    H->>API: Consume grant with device proof
    API-->>H: New session epoch/authorization
    O->>API: Resume using operator-side grant
```

A reboot grant cannot create a different session, change scopes, change operator, or survive its expiry.

## 5. Managed unattended session

- Operator must have an OIDC session with required MFA/step-up claims.
- Policy evaluation returns allowed scopes, duration, notification, local-consent, and schedule decisions.
- Device must be active, enrolled, healthy, and within the accepted minimum version.
- Device proves possession of the current device key.
- Service launches the interactive Agent visibly; session indicators remain active.
- Revocation of tenant, membership, device, key, or policy prevents new authorization and terminates active access within the configured revocation SLO.

## Managed-host sequence

Operator request -> policy decision -> `HOST_PENDING` -> transactional outbox -> authenticated device poll -> local consent/notification -> fresh host ephemeral key -> signed managed-host decision -> host/operator peer authorization -> signaling/TURN -> reciprocal signed transport binding -> content enabled.
