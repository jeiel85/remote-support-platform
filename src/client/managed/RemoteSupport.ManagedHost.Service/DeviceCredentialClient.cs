using System.Net.Http.Json;

namespace RemoteSupport.ManagedHost.Service;

/// <summary>Device-side implementation of the credential-refresh, key-rotation, heartbeat
/// and managed-session polling flows in 02-protocol/managed-host-command-channel.md.</summary>
public sealed class DeviceCredentialClient(HttpClient httpClient, Guid deviceId)
{
    public async Task<DeviceCredentialResult> RefreshCredentialAsync(IDeviceIdentityKey key, int keyVersion,
        CancellationToken cancellationToken)
    {
        DeviceCredentialChallenge challenge = await RequestChallengeAsync(keyVersion, "CREDENTIAL_REFRESH", cancellationToken)
            .ConfigureAwait(false);
        byte[] proofBytes = ManagedHostSignedPayloads.DeviceCredentialProof(challenge.CanonicalizationVersion, deviceId,
            challenge.ChallengeId, "CREDENTIAL_REFRESH", challenge.Nonce);
        DeviceCredentialExchangeRequest exchange = new(challenge.ChallengeId, keyVersion,
            new DetachedProof(challenge.Nonce, key.Thumbprint, "ecdsa-p256-sha256-p1363", key.Sign(proofBytes)));
        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            $"/v1/devices/{deviceId:D}/credentials", exchange, ManagedHostJson.Options, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<DeviceCredentialResult>(ManagedHostJson.Options, cancellationToken)
            .ConfigureAwait(false))!;
    }

    public async Task<DeviceKeyRotationResult> RotateKeyAsync(IDeviceIdentityKey currentKey, IDeviceIdentityKey newKey,
        int newKeyVersion, string currentCredential, CancellationToken cancellationToken)
    {
        DeviceCredentialChallenge challenge = await RequestChallengeAsync(newKeyVersion, "KEY_ROTATION", cancellationToken)
            .ConfigureAwait(false);
        byte[] proofBytes = ManagedHostSignedPayloads.DeviceCredentialProof(challenge.CanonicalizationVersion, deviceId,
            challenge.ChallengeId, "KEY_ROTATION", challenge.Nonce, newKey.Thumbprint);
        DeviceKeyRotationRequest request = new(newKey.PublicJwk,
            new DetachedProof(challenge.Nonce, currentKey.Thumbprint, "ecdsa-p256-sha256-p1363", currentKey.Sign(proofBytes)),
            challenge.ChallengeId);
        Uri target = new(httpClient.BaseAddress!, $"/v1/devices/{deviceId:D}/keys/rotate");
        using HttpRequestMessage message = BuildDpopRequest(HttpMethod.Post, target, currentKey, currentCredential, request);
        using HttpResponseMessage response = await httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<DeviceKeyRotationResult>(ManagedHostJson.Options, cancellationToken)
            .ConfigureAwait(false))!;
    }

    public async Task SendHeartbeatAsync(IDeviceIdentityKey key, string credential, DeviceHeartbeat heartbeat,
        CancellationToken cancellationToken)
    {
        Uri target = new(httpClient.BaseAddress!, $"/v1/devices/{deviceId:D}/heartbeat");
        using HttpRequestMessage message = BuildDpopRequest(HttpMethod.Post, target, key, credential, heartbeat);
        using HttpResponseMessage response = await httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PagedManagedSessionRequests> PollPendingSessionsAsync(IDeviceIdentityKey key, string credential,
        int waitSeconds, CancellationToken cancellationToken)
    {
        Uri target = new(httpClient.BaseAddress!, $"/v1/devices/{deviceId:D}/pending-session-requests?waitSeconds={waitSeconds}");
        using HttpRequestMessage message = BuildDpopRequest(HttpMethod.Get, target, key, credential, content: null);
        using HttpResponseMessage response = await httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<PagedManagedSessionRequests>(ManagedHostJson.Options, cancellationToken)
            .ConfigureAwait(false))!;
    }

    public async Task<ManagedHostDecisionResult> SubmitDecisionAsync(IDeviceIdentityKey key, string credential,
        Guid sessionId, long expectedStateVersion, ManagedHostDecisionRequest decision, CancellationToken cancellationToken)
    {
        Uri target = new(httpClient.BaseAddress!, $"/v1/sessions/{sessionId:D}/managed-host-decision");
        using HttpRequestMessage message = BuildDpopRequest(HttpMethod.Post, target, key, credential, decision);
        message.Headers.TryAddWithoutValidation("If-Match", $"\"{expectedStateVersion}\"");
        using HttpResponseMessage response = await httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<ManagedHostDecisionResult>(ManagedHostJson.Options, cancellationToken)
            .ConfigureAwait(false))!;
    }

    private async Task<DeviceCredentialChallenge> RequestChallengeAsync(int keyVersion, string purpose,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient.PostAsJsonAsync($"/v1/devices/{deviceId:D}/credential-challenges",
            new DeviceCredentialChallengeRequest(keyVersion, purpose), ManagedHostJson.Options, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<DeviceCredentialChallenge>(ManagedHostJson.Options, cancellationToken)
            .ConfigureAwait(false))!;
    }

    private static HttpRequestMessage BuildDpopRequest(HttpMethod method, Uri target, IDeviceIdentityKey key,
        string credential, object? content)
    {
        HttpRequestMessage message = new(method, target)
        {
            Content = content is null ? null : JsonContent.Create(content, options: ManagedHostJson.Options),
        };
        message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("DPoP", credential);
        message.Headers.TryAddWithoutValidation("DPoP", DeviceDpopProof.Create(key, method.Method, target, credential, DateTimeOffset.UtcNow));
        return message;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        ProblemContract? problem = null;
        try { problem = await response.Content.ReadFromJsonAsync<ProblemContract>(ManagedHostJson.Options, cancellationToken).ConfigureAwait(false); }
        catch (System.Text.Json.JsonException) { }
        throw new DeviceCredentialException((int)response.StatusCode, problem?.Code ?? "UNKNOWN", problem?.Message ?? response.ReasonPhrase ?? "Request failed.");
    }
}

public sealed class DeviceCredentialException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
