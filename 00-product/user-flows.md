# User Flows

## 1. Attended session

```mermaid
sequenceDiagram
    actor H as Host user
    participant A as Portable Agent
    participant C as Control Plane
    actor O as Operator
    participant OC as Operator Console

    H->>A: Launch
    A->>A: Generate ephemeral peer signing key
    A->>C: Create pending session + public key
    C-->>A: Code + host peer ID + host bootstrap credential
    H->>O: Share code out-of-band
    O->>OC: Sign in and enter code
    OC->>OC: Generate operator peer signing key
    OC->>C: Resolve code + requested scopes + public key
    C-->>OC: Operator peer ID + operator bootstrap credential
    C-->>A: Verified operator identity + scopes + consent nonce
    A->>H: Consent dialog
    H->>A: Approve selected scopes
    A->>C: Signed consent decision
    par Host peer authorization
        A->>C: Request single-use peer challenge
        A->>C: Challenge ID + signed key proof
        C-->>A: Host DPoP-bound peer token
    and Operator peer authorization
        OC->>C: Request single-use peer challenge
        OC->>C: Challenge ID + signed key proof
        C-->>OC: Operator DPoP-bound peer token
    end
    A->>C: One-use signaling ticket + TURN credential request
    OC->>C: One-use signaling ticket + TURN credential request
    A<<->>OC: WebRTC DTLS direct or TURN relay
    A<<->>OC: Reciprocal signed DTLS transport binding
    A<<->>OC: Enable only granted media/control channels
    H->>A: Revoke scope or disconnect
    A->>C: Signed/authorized state change and final metadata
```

The support code is a locator only. Consent, peer authorization, DPoP, TURN authorization and transport binding remain independent gates.

## 2. Managed attended session

1. Tenant admin creates a short-lived enrollment token and local admin installs the separately released Managed Host package.
2. The Service creates the device key, proves possession during enrollment and receives a renewable DPoP-bound device credential.
3. An authenticated operator requests `MANAGED_ATTENDED`; policy evaluation records an immutable decision and the session enters `HOST_PENDING`.
4. The installed Service receives the request through the authenticated bounded-long-poll channel.
5. The interactive Agent displays verified operator identity and requested scopes, generates a fresh host peer key and obtains local consent.
6. The active device key signs the host decision and binds the fresh host peer key.
7. Host and operator independently complete peer challenge/authorization, signaling/TURN issuance and reciprocal transport binding.
8. Device, membership or policy revocation invalidates credentials and terminates or blocks the session within the documented SLO.

## 3. Unattended session — separate release

The unattended flow starts only after Managed Host is approved. It additionally requires unattended enablement evidence, operator MFA/step-up, an unattended-specific policy decision and `UNATTENDED_SESSION` scope. The local host follows the configured notification/disclosure rule; hidden operation is not supported. It uses the same device-key decision, peer authorization, DPoP and transport-binding gates as managed attended support.

## 4. File transfer

```mermaid
stateDiagram-v2
    [*] --> Proposed
    Proposed --> Rejected: receiver denies / policy blocks
    Proposed --> Negotiated: receiver accepts destination and size
    Negotiated --> Transferring
    Transferring --> Paused: transport interruption
    Paused --> Transferring: session and transfer state remain valid
    Transferring --> Verifying: all chunks received
    Verifying --> Completed: size and hashes match
    Verifying --> Failed: mismatch / safety check fails
    Completed --> [*]
    Rejected --> [*]
    Failed --> [*]
```

## 5. Reboot continuity — Managed Host only

- Operator requests reboot; local consent/notification follows policy.
- The server issues a short-lived, single-use reboot reconnect grant bound to tenant, session, device, operator, authorization version and expected next epoch.
- The Service stores only a DPAPI machine-bound sealed continuity record; reusable peer/device access tokens and private peer keys are not persisted.
- Active inputs are released and the old transport/session epoch is closed cleanly before reboot.
- After boot and an eligible interactive session, the Service launches the Agent, consumes the grant, creates fresh peer key material and repeats authorization and transport binding.
- Expiry, reuse, revocation, unexpected boot state or policy change fails closed.
- No logon-screen or secure-desktop control is claimed without a separately proven compatibility and security contract.
