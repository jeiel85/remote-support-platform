using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace RemoteSupport.Infrastructure;

public sealed record HostSessionCreated(Guid SessionId, string SupportCode, DateTimeOffset ExpiresAt,
    string HostBootstrapToken, Guid HostPeerId, string State, long StateVersion, string PendingEventsUrl);
public sealed record OperatorDisplayResponse(string DisplayName, string TenantDisplayName, bool VerifiedTenant);
public sealed record PendingConsentResponse(Guid SessionId, Guid ConsentRequestId, OperatorDisplayResponse Operator,
    IReadOnlyList<string> RequestedScopes, DateTimeOffset ExpiresAt, long StateVersion, string ConsentNonce,
    string HostEphemeralKeyThumbprint);
public sealed record ConsentSessionResponse(Guid Id, Guid? TenantId, string SessionType, string State, long StateVersion,
    long PermissionRevision, long TransportEpoch, IReadOnlyList<string> RequestedScopes,
    IReadOnlyList<string> GrantedScopes, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);
public sealed record OperatorResolvedSession(Guid SessionId, Guid ConsentRequestId, OperatorDisplayResponse Operator,
    IReadOnlyList<string> RequestedScopes, DateTimeOffset ExpiresAt, long StateVersion, string OperatorBootstrapToken);
public sealed record PeerAuthorizationChallengeResponse(Guid ChallengeId, Guid SessionId, Guid PeerId, string Role,
    string Nonce, string KeyThumbprint, long TransportEpoch, DateTimeOffset ExpiresAt, string CanonicalizationVersion);
public sealed record PeerAuthorizationResponse(Guid SessionId, Guid PeerId, string Role, string PeerToken,
    IReadOnlyList<string> GrantedScopes, long PermissionRevision, long TransportEpoch, DateTimeOffset ExpiresAt,
    Guid RemotePeerId, string RemoteRole, PeerPublicJwk RemoteEphemeralPublicKey, string RemoteKeyThumbprint,
    string AuthorizationContextSha256);
public sealed record SignalingTicketResponse(string Ticket, Uri SignalingUrl, DateTimeOffset ExpiresAt, long TransportEpoch);
public sealed record IceServerResponse(IReadOnlyList<string> Urls, string Username, string Credential);
public sealed record TurnCredentialsResponse(string Region, IReadOnlyList<IceServerResponse> IceServers, DateTimeOffset ExpiresAt);

public sealed class ControlPlaneClientException(string code, string message, int statusCode) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

public sealed class AttendedControlPlaneClient(HttpClient httpClient)
{
    private static readonly string[] Codecs = ["H264"];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };
    private readonly HttpClient http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    public async Task<HostSessionCreated> CreateHostSessionAsync(EphemeralPeerIdentity identity, string locale,
        CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "v1/attended-sessions");
        request.Headers.Add("Idempotency-Key", EphemeralPeerIdentity.Base64Url(RandomNumberGenerator.GetBytes(32)));
        request.Content = JsonContent.Create(new
        {
            hostEphemeralPublicKey = identity.PublicJwk,
            clientVersion = ProductVersion.Current,
            capabilities = new { protocolMajor = 1, protocolMinor = 0, features = ProductCapabilities.Names, codecs = Codecs },
            installationInstanceId = (Guid?)null,
            locale,
        }, options: Json);
        return await SendAsync<HostSessionCreated>(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PendingConsentResponse?> GetPendingConsentAsync(HostSessionCreated session,
        CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage request = Bootstrap(HttpMethod.Get,
            $"v1/attended-sessions/{session.SessionId:D}/pending-consent", session.HostBootstrapToken);
        using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return null;
        return await ReadAsync<PendingConsentResponse>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ConsentSessionResponse> DecideConsentAsync(HostSessionCreated session, PendingConsentResponse consent,
        EphemeralPeerIdentity identity, bool approved, IReadOnlyList<string> grantedScopes,
        CancellationToken cancellationToken = default)
    {
        byte[] payload = EphemeralPeerIdentity.CreateConsentPayload(session, consent, approved, grantedScopes);
        using HttpRequestMessage request = Bootstrap(HttpMethod.Post,
            $"v1/attended-sessions/{session.SessionId:D}/consent", session.HostBootstrapToken);
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{consent.StateVersion}\"");
        request.Content = JsonContent.Create(new
        {
            consentRequestId = consent.ConsentRequestId,
            approved,
            grantedScopes,
            consentNonce = consent.ConsentNonce,
            decisionProof = new
            {
                nonce = consent.ConsentNonce,
                keyId = identity.KeyThumbprint,
                algorithm = "ecdsa-p256-sha256-p1363",
                signature = identity.SignBase64Url(payload),
            },
        }, options: Json);
        return await SendAsync<ConsentSessionResponse>(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OperatorResolvedSession> ResolveAsync(string supportCode, IReadOnlyList<string> requestedScopes,
        EphemeralPeerIdentity identity, string oidcAccessToken, CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "v1/attended-sessions/resolve");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", oidcAccessToken);
        request.Content = JsonContent.Create(new
        {
            supportCode,
            requestedScopes,
            operatorEphemeralPublicKey = identity.PublicJwk,
            clientVersion = ProductVersion.Current,
            capabilities = new { protocolMajor = 1, protocolMinor = 0, features = ProductCapabilities.Names, codecs = Codecs },
        }, options: Json);
        return await SendAsync<OperatorResolvedSession>(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PeerAuthorizationResponse> AuthorizePeerAsync(Guid sessionId, string bootstrapToken,
        EphemeralPeerIdentity identity, CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage challengeRequest = Bootstrap(HttpMethod.Post,
            $"v1/sessions/{sessionId:D}/peer-authorization-challenges", bootstrapToken);
        challengeRequest.Content = JsonContent.Create(new { }, options: Json);
        PeerAuthorizationChallengeResponse challenge = await SendAsync<PeerAuthorizationChallengeResponse>(challengeRequest, cancellationToken)
            .ConfigureAwait(false);
        if (challenge.CanonicalizationVersion != "RSP-PEER-AUTH-V1" || challenge.KeyThumbprint != identity.KeyThumbprint)
            throw new ControlPlaneClientException("AUTH_PROOF_INVALID", "Peer authorization challenge binding is invalid.", 0);
        byte[] payload = EphemeralPeerIdentity.CreatePeerAuthorizationPayload(challenge);
        using HttpRequestMessage authorize = Bootstrap(HttpMethod.Post, $"v1/sessions/{sessionId:D}/peer-authorization", bootstrapToken);
        authorize.Content = JsonContent.Create(new
        {
            challengeId = challenge.ChallengeId,
            role = challenge.Role,
            proof = new
            {
                nonce = challenge.Nonce,
                signature = identity.SignBase64Url(payload),
                publicKey = identity.PublicJwk,
                algorithm = "ecdsa-p256-sha256-p1363",
            },
        }, options: Json);
        return await SendAsync<PeerAuthorizationResponse>(authorize, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ConsentSessionResponse> GetSessionAsync(Guid sessionId, string oidcAccessToken,
        CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, $"v1/sessions/{sessionId:D}");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", oidcAccessToken);
        return await SendAsync<ConsentSessionResponse>(request, cancellationToken).ConfigureAwait(false);
    }

    public Task<SignalingTicketResponse> GetSignalingTicketAsync(PeerAuthorizationResponse authorization,
        EphemeralPeerIdentity identity, CancellationToken cancellationToken = default) => SendDpopAsync<SignalingTicketResponse>(
            HttpMethod.Post, $"v1/sessions/{authorization.SessionId:D}/signaling-tickets", authorization, identity, cancellationToken);

    public Task<TurnCredentialsResponse> GetTurnCredentialsAsync(PeerAuthorizationResponse authorization,
        EphemeralPeerIdentity identity, CancellationToken cancellationToken = default) => SendDpopAsync<TurnCredentialsResponse>(
            HttpMethod.Post, $"v1/sessions/{authorization.SessionId:D}/turn-credentials", authorization, identity, cancellationToken);

    public async Task<ConsentSessionResponse> TerminateAsync(PeerAuthorizationResponse authorization,
        EphemeralPeerIdentity identity, string reasonCode, CancellationToken cancellationToken = default)
    {
        const string suffix = "termination";
        string relative = $"v1/sessions/{authorization.SessionId:D}/{suffix}";
        Uri target = new(http.BaseAddress ?? throw new InvalidOperationException("Control-plane base address is required."), relative);
        using HttpRequestMessage request = new(HttpMethod.Post, target);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("DPoP", authorization.PeerToken);
        request.Headers.Add("DPoP", identity.CreateDpopProof(HttpMethod.Post, target, authorization.PeerToken, DateTimeOffset.UtcNow));
        request.Content = JsonContent.Create(new { reasonCode }, options: Json);
        return await SendAsync<ConsentSessionResponse>(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ConsentSessionResponse> RevokeScopesAsync(PeerAuthorizationResponse authorization,
        EphemeralPeerIdentity identity, long expectedStateVersion, IReadOnlyList<string> scopes,
        CancellationToken cancellationToken = default)
    {
        string relative = $"v1/sessions/{authorization.SessionId:D}/scope-revocations";
        Uri target = new(http.BaseAddress ?? throw new InvalidOperationException("Control-plane base address is required."), relative);
        using HttpRequestMessage request = new(HttpMethod.Post, target);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("DPoP", authorization.PeerToken);
        request.Headers.Add("DPoP", identity.CreateDpopProof(HttpMethod.Post, target, authorization.PeerToken, DateTimeOffset.UtcNow));
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{expectedStateVersion}\"");
        request.Content = JsonContent.Create(new { revokedScopes = scopes, reasonCode = "LOCAL_USER_REVOKED" }, options: Json);
        return await SendAsync<ConsentSessionResponse>(request, cancellationToken).ConfigureAwait(false);
    }

    private static HttpRequestMessage Bootstrap(HttpMethod method, string uri, string token)
    {
        HttpRequestMessage request = new(method, uri);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        return await ReadAsync<T>(response, cancellationToken).ConfigureAwait(false);
    }

    private Task<T> SendDpopAsync<T>(HttpMethod method, string relativeUri, PeerAuthorizationResponse authorization,
        EphemeralPeerIdentity identity, CancellationToken cancellationToken)
    {
        Uri baseAddress = http.BaseAddress ?? throw new InvalidOperationException("Control-plane base address is required.");
        Uri target = new(baseAddress, relativeUri);
        HttpRequestMessage request = new(method, target);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("DPoP", authorization.PeerToken);
        request.Headers.Add("DPoP", identity.CreateDpopProof(method, target, authorization.PeerToken, DateTimeOffset.UtcNow));
        return SendAndDisposeAsync<T>(request, cancellationToken);
    }

    private async Task<T> SendAndDisposeAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using (request) return await SendAsync<T>(request, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            ProblemResponse? problem = null;
            try { problem = await response.Content.ReadFromJsonAsync<ProblemResponse>(Json, cancellationToken).ConfigureAwait(false); }
            catch (JsonException) { }
            throw new ControlPlaneClientException(problem?.Code ?? "INTERNAL_ERROR",
                problem?.Message ?? "The control plane rejected the request.", (int)response.StatusCode);
        }
        return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Control-plane response body is missing.");
    }

    private sealed record ProblemResponse(string Code, string Message);
}

public static class ProductVersion
{
    public const string Current = "0.9.0-beta.1";
}

public static class ProductCapabilities
{
    public static readonly string[] Names =
    [
        "transport-binding-v1", "permission-state-v1", "clipboard-text-v1", "file-transfer-v1", "chat-v1", "attended-only",
    ];
}
