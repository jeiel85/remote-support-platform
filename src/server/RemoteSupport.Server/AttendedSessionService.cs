using System.Security.Claims;
using System.Text.Json;

namespace RemoteSupport.Server;

internal sealed class ControlPlaneException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}

internal sealed record OperatorIdentity(Guid TenantId, string Subject, string DisplayName,
    string TenantDisplayName, bool VerifiedTenant);

internal sealed class AttendedSessionService(
    IAttendedSessionStore store,
    ControlPlaneCrypto crypto,
    ControlPlaneOptions options,
    ISystemClock clock)
{
    private static readonly HashSet<string> ValidScopes = new(StringComparer.Ordinal)
    {
        "VIEW_SCREEN", "CONTROL_POINTER", "CONTROL_KEYBOARD",
        "SYNC_CLIPBOARD_TEXT_HOST_TO_OPERATOR", "SYNC_CLIPBOARD_TEXT_OPERATOR_TO_HOST",
        "TRANSFER_FILE_HOST_TO_OPERATOR", "TRANSFER_FILE_OPERATOR_TO_HOST", "CHAT", "SWITCH_MONITOR",
        "REQUEST_REBOOT", "RECONNECT_AFTER_REBOOT", "UNATTENDED_SESSION",
    };

    public AttendedSessionCreated Create(CreateAttendedSessionRequest request, string idempotencyKey, string baseUrl)
    {
        ValidateClient(request.ClientVersion, request.Capabilities);
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length is < 16 or > 128)
            throw BadRequest("IDEMPOTENCY_KEY_INVALID");
        string hostThumbprint;
        try { hostThumbprint = ControlPlaneCrypto.Thumbprint(request.HostEphemeralPublicKey); }
        catch (Exception exception) when (exception is FormatException or KeyNotFoundException or InvalidOperationException)
        { throw BadRequest("HOST_KEY_INVALID"); }
        string idempotencyHash = crypto.LookupHash("idempotency\0" + idempotencyKey);
        string requestHash = ControlPlaneCrypto.CreateRequestHash(request, hostThumbprint);
        return store.Execute(collection =>
        {
            SessionAggregate? previous = collection.ById.Values.SingleOrDefault(session =>
                string.Equals(session.IdempotencyHash, idempotencyHash, StringComparison.Ordinal));
            if (previous is not null)
            {
                if (!string.Equals(previous.IdempotencyRequestHash, requestHash, StringComparison.Ordinal))
                    throw Conflict("IDEMPOTENCY_KEY_REUSED");
                return Created(previous,
                    crypto.DeriveSupportCode(idempotencyKey, previous.IdempotencyDerivationCounter),
                    crypto.DeriveSecret("host-bootstrap", idempotencyKey), baseUrl);
            }

            DateTimeOffset now = clock.UtcNow;
            DateTimeOffset expiresAt = now + options.SessionLifetime;
            string supportCode = string.Empty;
            string codeHash = string.Empty;
            int derivationCounter = 0;
            for (int attempt = 0; attempt < 32; attempt++)
            {
                derivationCounter = attempt;
                supportCode = crypto.DeriveSupportCode(idempotencyKey, attempt);
                codeHash = crypto.LookupHash(supportCode);
                if (!collection.ByCodeHash.ContainsKey(codeHash)) break;
            }
            if (collection.ByCodeHash.ContainsKey(codeHash)) throw new ControlPlaneException(503, "CODE_POOL_EXHAUSTED", "Unable to allocate a support code.");
            string hostToken = crypto.DeriveSecret("host-bootstrap", idempotencyKey);
            string hostTokenHash = crypto.LookupHash(hostToken);
            Guid sessionId = crypto.DeriveGuid("session-id", idempotencyKey);
            Guid hostPeerId = crypto.DeriveGuid("host-peer-id", idempotencyKey);
            if (collection.ById.ContainsKey(sessionId))
                throw new ControlPlaneException(503, "IDEMPOTENCY_DERIVATION_COLLISION", "Unable to allocate a session identifier.");
            PeerRecord host = new(hostPeerId, "HOST", request.HostEphemeralPublicKey.GetRawText(), hostThumbprint,
                null, null, null, false);
            SessionAggregate session = new()
            {
                Id = sessionId,
                CreatedAt = now,
                ExpiresAt = expiresAt,
                CodeHash = codeHash,
                Host = host,
                IdempotencyHash = idempotencyHash,
                IdempotencyRequestHash = requestHash,
                IdempotencyDerivationCounter = derivationCounter,
            };
            session.BootstrapCredentials[hostTokenHash] = new BootstrapRecord(hostPeerId, "HOST",
                now + options.HostBootstrapLifetime, 10);
            AppendLifecycle(session, "SESSION_CREATED", "HOST", hostPeerId.ToString("D"), now,
                new { state = session.State, hostPeerId });
            collection.ById.Add(sessionId, session);
            collection.ByCodeHash.Add(codeHash, sessionId);
            return Created(session, supportCode, hostToken, baseUrl);
        });
    }

    public ConsentRequest Resolve(ResolveAttendedSessionRequest request, OperatorIdentity identity)
    {
        if (!identity.VerifiedTenant) throw new ControlPlaneException(403, "TENANT_IDENTITY_UNVERIFIED", "Verified tenant identity is required.");
        if (string.IsNullOrWhiteSpace(request.ClientVersion) || request.ClientVersion.Length > 64 ||
            (request.Capabilities is not null && (request.Capabilities.ProtocolMajor != 1 ||
             request.Capabilities.Features is null || request.Capabilities.Features.Count > 128)))
            throw BadRequest("CLIENT_CAPABILITY_INVALID");
        string[] scopes = NormalizeScopes(request.RequestedScopes);
        if (!ControlPlaneCrypto.TryNormalizeSupportCode(request.SupportCode, out string normalized)) throw NotFound();
        string codeHash = crypto.LookupHash(normalized);
        string operatorThumbprint;
        try { operatorThumbprint = ControlPlaneCrypto.Thumbprint(request.OperatorEphemeralPublicKey); }
        catch (Exception exception) when (exception is FormatException or KeyNotFoundException or InvalidOperationException)
        { throw BadRequest("OPERATOR_KEY_INVALID"); }
        ControlPlaneException? deferredFailure = null;
        ConsentRequest? result = store.Execute(collection =>
        {
            if (!collection.ByCodeHash.TryGetValue(codeHash, out Guid sessionId) || !collection.ById.TryGetValue(sessionId, out SessionAggregate? session)) throw NotFound();
            DateTimeOffset now = clock.UtcNow;
            if (session.CodeAttempts >= options.MaximumCodeAttempts) throw NotFound();
            if (now >= session.ExpiresAt)
            {
                session.CodeAttempts++;
                Expire(session, now);
                deferredFailure = NotFound();
                return null;
            }
            if (session.State != "WAITING_FOR_OPERATOR") throw NotFound();
            session.CodeAttempts++;
            Guid operatorPeerId = Guid.NewGuid();
            session.TenantId = identity.TenantId;
            session.RequestedScopes = scopes;
            session.Operator = new PeerRecord(operatorPeerId, "OPERATOR", request.OperatorEphemeralPublicKey.GetRawText(),
                operatorThumbprint, identity.Subject, identity.DisplayName, identity.TenantDisplayName, identity.VerifiedTenant);
            session.Consent = new ConsentRecord(Guid.NewGuid(), session.ExpiresAt);
            session.State = "CONSENT_PENDING";
            session.StateVersion++;
            string operatorToken = ControlPlaneCrypto.GenerateSecret();
            session.BootstrapCredentials[crypto.LookupHash(operatorToken)] = new BootstrapRecord(operatorPeerId,
                "OPERATOR", now + options.OperatorBootstrapLifetime, 10);
            AppendLifecycle(session, "CONSENT_REQUESTED", "OPERATOR", identity.Subject, now,
                new { session.Consent.Id, identity.DisplayName, identity.TenantDisplayName, requestedScopes = scopes, session.StateVersion });
            return new ConsentRequest(session.Id, session.Consent.Id,
                new OperatorDisplay(identity.DisplayName, identity.TenantDisplayName, identity.VerifiedTenant),
                scopes, session.ExpiresAt, session.StateVersion, operatorToken);
        });
        if (deferredFailure is not null) throw deferredFailure;
        return result!;
    }

    public PendingConsent? GetPendingConsent(Guid sessionId, string bootstrapToken)
    {
        return store.Execute(collection =>
        {
            SessionAggregate session = RequireSession(collection, sessionId);
            RequireBootstrap(session, bootstrapToken, "HOST", consume: false);
            if (clock.UtcNow >= session.ExpiresAt) { Expire(session, clock.UtcNow); return null; }
            if (session.State != "CONSENT_PENDING" || session.Consent is null || session.Operator is null) return null;
            string nonce = ControlPlaneCrypto.GenerateSecret(24);
            session.Consent.NonceHash = crypto.LookupHash(nonce);
            session.Consent.NonceConsumed = false;
            return new PendingConsent(session.Id, session.Consent.Id,
                new OperatorDisplay(session.Operator.DisplayName!, session.Operator.TenantDisplayName!, session.Operator.VerifiedTenant),
                session.RequestedScopes, session.Consent.ExpiresAt, session.StateVersion, nonce, session.Host.KeyThumbprint);
        });
    }

    public SessionResponse Decide(Guid sessionId, string bootstrapToken, long expectedVersion, ConsentDecision decision)
    {
        ControlPlaneException? deferredFailure = null;
        SessionResponse? result = store.Execute(collection =>
        {
            SessionAggregate session = RequireSession(collection, sessionId);
            RequireBootstrap(session, bootstrapToken, "HOST", consume: false);
            DateTimeOffset now = clock.UtcNow;
            if (now >= session.ExpiresAt)
            {
                Expire(session, now);
                deferredFailure = Conflict("SESSION_EXPIRED");
                return null;
            }
            if (session.State != "CONSENT_PENDING" || session.Consent is null || session.Operator is null) throw Conflict("SESSION_STATE_CONFLICT");
            if (decision.DecisionProof is null || string.IsNullOrWhiteSpace(decision.DecisionProof.Signature) ||
                string.IsNullOrWhiteSpace(decision.DecisionProof.Algorithm)) throw BadRequest("CONSENT_PROOF_INVALID");
            if (session.StateVersion != expectedVersion) throw Conflict("STATE_VERSION_CONFLICT");
            if (decision.ConsentRequestId != session.Consent.Id || session.Consent.NonceConsumed ||
                session.Consent.NonceHash is null || !FixedHashEquals(session.Consent.NonceHash, crypto.LookupHash(decision.ConsentNonce)) ||
                !string.Equals(decision.DecisionProof.Nonce, decision.ConsentNonce, StringComparison.Ordinal) ||
                !string.Equals(decision.DecisionProof.KeyId, session.Host.KeyThumbprint, StringComparison.Ordinal)) throw Conflict("CONSENT_NONCE_INVALID");
            string[] granted = NormalizeScopes(decision.GrantedScopes, allowEmpty: !decision.Approved);
            if (!decision.Approved && granted.Length != 0) throw BadRequest("REJECTED_CONSENT_HAS_SCOPES");
            if (granted.Except(session.RequestedScopes, StringComparer.Ordinal).Any()) throw BadRequest("GRANTED_SCOPE_NOT_REQUESTED");
            using JsonDocument hostJwk = JsonDocument.Parse(session.Host.PublicJwk);
            byte[] signed = ControlPlaneCrypto.ConsentBytes(session, decision with { GrantedScopes = granted });
            if (!ControlPlaneCrypto.VerifyP256(hostJwk.RootElement, decision.DecisionProof.Algorithm, signed,
                    decision.DecisionProof.Signature)) throw new ControlPlaneException(403, "CONSENT_SIGNATURE_INVALID", "Consent proof was invalid.");
            session.Consent.NonceConsumed = true;
            session.GrantedScopes = decision.Approved ? granted : [];
            session.PermissionRevision = decision.Approved ? 1 : 0;
            session.TransportEpoch = decision.Approved ? 1 : 0;
            session.State = decision.Approved ? "AUTHORIZED" : "REJECTED";
            session.StateVersion++;
            AppendLifecycle(session, decision.Approved ? "CONSENT_APPROVED" : "CONSENT_REJECTED", "HOST",
                session.Host.PeerId.ToString("D"), now, new { grantedScopes = granted, session.StateVersion });
            return ToResponse(session);
        });
        if (deferredFailure is not null) throw deferredFailure;
        return result!;
    }

    public PeerAuthorizationChallenge CreateChallenge(Guid sessionId, string bootstrapToken)
    {
        return store.Execute(collection =>
        {
            SessionAggregate session = RequireSession(collection, sessionId);
            BootstrapRecord bootstrap = RequireBootstrap(session, bootstrapToken, null, consume: true);
            if (session.State != "AUTHORIZED" || clock.UtcNow >= session.ExpiresAt) throw Conflict("SESSION_NOT_AUTHORIZED");
            PeerRecord peer = bootstrap.Role == "HOST" ? session.Host : session.Operator!;
            string nonce = ControlPlaneCrypto.GenerateSecret(24);
            ChallengeRecord challenge = new(Guid.NewGuid(), peer.PeerId, peer.Role, crypto.LookupHash(nonce),
                clock.UtcNow + options.ChallengeLifetime, session.TransportEpoch);
            session.Challenges.Add(challenge.Id, challenge);
            return new PeerAuthorizationChallenge(challenge.Id, session.Id, peer.PeerId, peer.Role, nonce,
                peer.KeyThumbprint, session.TransportEpoch, challenge.ExpiresAt, "RSP-PEER-AUTH-V1");
        });
    }

    public PeerAuthorization AuthorizePeer(Guid sessionId, string bootstrapToken, PeerAuthorizationRequest request)
    {
        return store.Execute(collection =>
        {
            SessionAggregate session = RequireSession(collection, sessionId);
            if (request.Proof is null || request.Proof.PublicKey.ValueKind != JsonValueKind.Object ||
                string.IsNullOrWhiteSpace(request.Proof.Nonce) || string.IsNullOrWhiteSpace(request.Proof.Signature))
                throw BadRequest("PEER_PROOF_INVALID");
            BootstrapRecord bootstrap = RequireBootstrap(session, bootstrapToken, request.Role, consume: true);
            if (session.State != "AUTHORIZED" || !session.Challenges.TryGetValue(request.ChallengeId, out ChallengeRecord? challenge) ||
                challenge.Consumed || challenge.PeerId != bootstrap.PeerId || challenge.Role != bootstrap.Role ||
                challenge.ExpiresAt <= clock.UtcNow || challenge.TransportEpoch != session.TransportEpoch ||
                !FixedHashEquals(challenge.NonceHash, crypto.LookupHash(request.Proof.Nonce))) throw Conflict("PEER_CHALLENGE_INVALID");
            PeerRecord peer = bootstrap.Role == "HOST" ? session.Host : session.Operator!;
            string presentedThumbprint;
            try { presentedThumbprint = ControlPlaneCrypto.Thumbprint(request.Proof.PublicKey); }
            catch (Exception exception) when (exception is FormatException or KeyNotFoundException or InvalidOperationException)
            { throw BadRequest("PEER_KEY_INVALID"); }
            if (!string.Equals(presentedThumbprint, peer.KeyThumbprint, StringComparison.Ordinal) ||
                !string.Equals(request.Proof.Algorithm, "ecdsa-p256-sha256-p1363", StringComparison.Ordinal))
                throw new ControlPlaneException(403, "PEER_KEY_MISMATCH", "Peer proof key did not match the bootstrap identity.");
            byte[] signed = ControlPlaneCrypto.PeerAuthorizationBytes(session, challenge, peer, request.Proof.Nonce);
            if (!ControlPlaneCrypto.VerifyP256(request.Proof.PublicKey, request.Proof.Algorithm, signed, request.Proof.Signature))
                throw new ControlPlaneException(403, "PEER_PROOF_INVALID", "Peer proof was invalid.");
            challenge.Consumed = true;
            DateTimeOffset issuedAt = clock.UtcNow;
            DateTimeOffset expiresAt = issuedAt + options.PeerTokenLifetime;
            string token = crypto.IssuePeerToken(session, peer, issuedAt, expiresAt);
            AppendLifecycle(session, "PEER_AUTHORIZED", peer.Role, peer.Subject ?? peer.PeerId.ToString("D"),
                clock.UtcNow, new { peer.PeerId, peer.Role, expiresAt });
            PeerRecord remote = peer.Role == "HOST" ? session.Operator! : session.Host;
            using JsonDocument remoteJwk = JsonDocument.Parse(remote.PublicJwk);
            return new PeerAuthorization(session.Id, peer.PeerId, peer.Role, token, session.GrantedScopes,
                session.PermissionRevision, session.TransportEpoch, expiresAt, remote.PeerId, remote.Role,
                remoteJwk.RootElement.Clone(), remote.KeyThumbprint, ControlPlaneCrypto.AuthorizationContext(session));
        });
    }

    public SessionResponse Get(Guid sessionId, OperatorIdentity identity) => store.Execute(collection =>
    {
        SessionAggregate session = RequireSession(collection, sessionId);
        if (session.TenantId != identity.TenantId || session.Operator?.Subject != identity.Subject) throw NotFound();
        return ToResponse(session);
    });

    public SessionResponse Terminate(Guid sessionId, PeerAccessContext access, SessionTerminationRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.ReasonCode) || request.ReasonCode.Length > 64 ||
            request.ReasonCode.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
            throw BadRequest("SESSION_TERMINATION_REASON_INVALID");
        return store.Execute(collection =>
        {
            SessionAggregate session = RequireSession(collection, sessionId);
            if (access.SessionId != sessionId || access.PeerId != session.Host.PeerId && access.PeerId != session.Operator?.PeerId)
                throw new ControlPlaneException(403, "AUTHZ_SCOPE_DENIED", "Peer cannot terminate this session.");
            if (session.State == "TERMINATED") return ToResponse(session);
            if (session.State != "AUTHORIZED") throw Conflict("SESSION_STATE_CONFLICT");
            session.State = "TERMINATED";
            session.StateVersion++;
            session.PermissionRevision++;
            session.GrantedScopes = [];
            AppendLifecycle(session, "SESSION_TERMINATED", access.Role, access.PeerId.ToString("D"), clock.UtcNow,
                new { request.ReasonCode, session.StateVersion, session.PermissionRevision });
            return ToResponse(session);
        });
    }

    public SessionResponse RevokeScopes(Guid sessionId, PeerAccessContext access, long expectedVersion,
        ScopeRevocationRequest request)
    {
        string[] revoked = NormalizeScopes(request?.RevokedScopes ?? [], allowEmpty: false);
        if (request is null || string.IsNullOrWhiteSpace(request.ReasonCode) || request.ReasonCode.Length > 64 ||
            request.ReasonCode.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
            throw BadRequest("SCOPE_REVOCATION_INVALID");
        return store.Execute(collection =>
        {
            SessionAggregate session = RequireSession(collection, sessionId);
            if (access.Role != "HOST" || access.PeerId != session.Host.PeerId)
                throw new ControlPlaneException(403, "AUTHZ_SCOPE_DENIED", "Only the attended host can revoke scopes.");
            if (session.State != "AUTHORIZED" || session.StateVersion != expectedVersion) throw Conflict("SESSION_STATE_CONFLICT");
            if (revoked.Except(session.GrantedScopes, StringComparer.Ordinal).Any()) throw BadRequest("SCOPE_NOT_GRANTED");
            session.GrantedScopes = session.GrantedScopes.Except(revoked, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            session.PermissionRevision++;
            session.StateVersion++;
            if (session.GrantedScopes.Length == 0) session.State = "TERMINATED";
            AppendLifecycle(session, "SCOPES_REVOKED", access.Role, access.PeerId.ToString("D"), clock.UtcNow,
                new { revokedScopes = revoked, request.ReasonCode, session.StateVersion, session.PermissionRevision });
            return ToResponse(session);
        });
    }
    public IReadOnlyCollection<SessionAggregate> Snapshot() => store.Snapshot();

    private BootstrapRecord RequireBootstrap(SessionAggregate session, string token, string? role, bool consume)
    {
        string hash = crypto.LookupHash(token);
        if (!session.BootstrapCredentials.TryGetValue(hash, out BootstrapRecord? credential) ||
            (role is not null && credential.Role != role) || credential.ExpiresAt <= clock.UtcNow ||
            credential.Uses >= credential.MaximumUses) throw new ControlPlaneException(401, "AUTHENTICATION_REQUIRED", "Authentication required.");
        if (consume) credential.Uses++;
        return credential;
    }

    private static SessionAggregate RequireSession(SessionCollection collection, Guid sessionId) =>
        collection.ById.TryGetValue(sessionId, out SessionAggregate? session) ? session : throw NotFound();

    private static SessionResponse ToResponse(SessionAggregate session) => new(session.Id, session.TenantId, "ATTENDED",
        session.State, session.StateVersion, session.PermissionRevision, session.TransportEpoch,
        session.RequestedScopes, session.GrantedScopes, session.CreatedAt, session.ExpiresAt);

    private static AttendedSessionCreated Created(SessionAggregate session, string supportCode, string hostToken,
        string baseUrl) => new(session.Id, supportCode, session.ExpiresAt, hostToken, session.Host.PeerId,
            "WAITING_FOR_OPERATOR", 1,
            $"{baseUrl.TrimEnd('/')}/v1/attended-sessions/{session.Id:D}/pending-consent");

    private static string[] NormalizeScopes(IReadOnlyList<string>? values, bool allowEmpty = false)
    {
        if (values is null || (!allowEmpty && values.Count == 0) || values.Count > 12 || values.Any(value => !ValidScopes.Contains(value)))
            throw BadRequest("SCOPE_INVALID");
        string[] normalized = values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (normalized.Length != values.Count) throw BadRequest("SCOPE_DUPLICATE");
        return normalized;
    }

    private static void ValidateClient(string version, ClientCapabilities? capabilities)
    {
        if (string.IsNullOrWhiteSpace(version) || version.Length > 64 || capabilities is null || capabilities.ProtocolMajor != 1 ||
            capabilities.Features is null || capabilities.Features.Count > 128) throw BadRequest("CLIENT_CAPABILITY_INVALID");
    }

    private static bool FixedHashEquals(string left, string right) =>
        System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(Convert.FromHexString(left), Convert.FromHexString(right));

    private static void Expire(SessionAggregate session, DateTimeOffset now)
    {
        if (session.State is "EXPIRED" or "REJECTED" or "ENDED") return;
        session.State = "EXPIRED";
        session.StateVersion++;
        AppendLifecycle(session, "SESSION_EXPIRED", "SYSTEM", null, now, new { session.StateVersion });
    }

    internal static void AppendLifecycle(SessionAggregate session, string action, string actorType, string? actorId,
        DateTimeOffset now, object details)
    {
        JsonElement detailElement = JsonSerializer.SerializeToElement(details);
        long sequence = session.AuditEvents.Count + 1L;
        string? previousHash = session.AuditEvents.Count == 0 ? null : session.AuditEvents[^1].EventHash;
        JsonElement auditPayload = JsonSerializer.SerializeToElement(new
        {
            action,
            actorId,
            actorType,
            details = detailElement,
            occurredAt = now.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            outcome = "SUCCESS",
            previousHash,
            sequence,
            sessionId = session.Id,
            stateVersion = session.StateVersion,
        });
        byte[] canonical = ControlPlaneCrypto.Canonicalize(auditPayload);
        byte[] domain = System.Text.Encoding.ASCII.GetBytes("RSP-AUDIT-EVENT-V1\0");
        byte[] hashed = new byte[domain.Length + canonical.Length];
        domain.CopyTo(hashed, 0);
        canonical.CopyTo(hashed, domain.Length);
        string eventHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(hashed));
        session.AuditEvents.Add(new AuditRecord(Guid.NewGuid(), sequence, action, "SUCCESS", actorType, actorId,
            now, session.StateVersion, detailElement, previousHash, eventHash));
        session.OutboxMessages.Add(new OutboxRecord(Guid.NewGuid(), $"sessions.{action.ToLowerInvariant()}", now,
            JsonSerializer.SerializeToElement(new { sessionId = session.Id, session.State, session.StateVersion })));
    }

    private static ControlPlaneException BadRequest(string code) => new(400, code, "The request was invalid.");
    private static ControlPlaneException NotFound() => new(404, "SESSION_NOT_FOUND", "The session was not found.");
    private static ControlPlaneException Conflict(string code) => new(409, code, "The request conflicted with current session state.");
}
