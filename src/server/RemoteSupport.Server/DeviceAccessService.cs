using System.Text.Json;

namespace RemoteSupport.Server;

internal sealed record DeviceAccessContext(Guid TenantId, Guid DeviceId, string KeyThumbprint, int KeyVersion,
    long AuthorizationVersion);

internal sealed class DeviceAccessService(
    IGovernanceStore store,
    ControlPlaneCrypto crypto,
    ControlPlaneOptions options,
    ISystemClock clock)
{
    public DeviceAccessContext Authenticate(HttpRequest request, Guid expectedDeviceId) =>
        Authenticate(request, (Guid?)expectedDeviceId);

    /// <summary>Authenticates a device DPoP token without pinning to a path-supplied device ID,
    /// for routes (e.g. managed-host-decision) keyed by session rather than device.</summary>
    public DeviceAccessContext AuthenticateAny(HttpRequest request) => Authenticate(request, (Guid?)null);

    private DeviceAccessContext Authenticate(HttpRequest request, Guid? expectedDeviceId)
    {
        string token = DpopProof.RequireSingleHeader(request.Headers.Authorization, "DPoP ", Unauthorized);
        string proof = DpopProof.RequireSingleHeader(request.Headers["DPoP"], string.Empty, Unauthorized);
        if (token.Length is < 32 or > 8192 || proof.Length is < 64 or > 8192 ||
            !crypto.VerifyDeviceToken(token, clock.UtcNow, out JsonDocument? tokenDocument) || tokenDocument is null)
            throw Unauthorized("DPOP_TOKEN_INVALID");

        using (tokenDocument)
        {
            DeviceAccessContext access = ReadAccess(tokenDocument.RootElement, expectedDeviceId, clock.UtcNow);
            ValidatedDpopProof validated = DpopProof.Validate(proof, token, request, access.KeyThumbprint,
                options.DpopProofLifetime, clock.UtcNow, Unauthorized);
            return store.Execute(access.TenantId, tenant =>
            {
                if (!tenant.Devices.TryGetValue(access.DeviceId, out DeviceRecord? device) ||
                    device.Status != "ACTIVE" || device.AuthorizationVersion != access.AuthorizationVersion ||
                    device.ActiveKeyVersion != access.KeyVersion ||
                    !DpopProof.FixedEquals(device.ActiveKey.KeyThumbprint, access.KeyThumbprint))
                    throw Unauthorized("DPOP_AUTHORIZATION_STALE");

                foreach (string expired in device.DpopReplays
                    .Where(item => item.Value.ExpiresAt <= clock.UtcNow).Select(item => item.Key).ToArray())
                    device.DpopReplays.Remove(expired);
                if (device.DpopReplays.Count >= 2048)
                    throw new ControlPlaneException(429, "DPOP_REPLAY_WINDOW_FULL", "Proof replay capacity was exceeded.");
                string replayHash = crypto.LookupHash($"device-dpop\0{access.KeyThumbprint}\0{validated.Jti}");
                if (device.DpopReplays.ContainsKey(replayHash)) throw Unauthorized("DPOP_PROOF_REPLAYED");
                device.DpopReplays.Add(replayHash, new DpopReplayRecord(replayHash,
                    validated.IssuedAt + options.DpopProofLifetime + TimeSpan.FromSeconds(30)));
                return access;
            });
        }
    }

    private static DeviceAccessContext ReadAccess(JsonElement payload, Guid? expectedDeviceId, DateTimeOffset now)
    {
        DpopProof.RequireUniqueObject(payload);
        Guid tenantId = payload.GetProperty("tenantId").GetGuid();
        Guid deviceId = payload.GetProperty("deviceId").GetGuid();
        long authorizationVersion = payload.GetProperty("authorizationVersion").GetInt64();
        int keyVersion = payload.GetProperty("keyVersion").GetInt32();
        long issuedAt = payload.GetProperty("iat").GetInt64();
        long expiresAt = payload.GetProperty("exp").GetInt64();
        string thumbprint = payload.GetProperty("cnf").GetProperty("jkt").GetString() ?? string.Empty;
        if ((expectedDeviceId is { } expected && deviceId != expected) || tenantId == Guid.Empty ||
            authorizationVersion <= 0 || keyVersion <= 0 || thumbprint.Length is < 32 or > 128 ||
            expiresAt <= now.ToUnixTimeSeconds() || issuedAt > now.ToUnixTimeSeconds() + 30 ||
            expiresAt - issuedAt > 15 * 60)
            throw Unauthorized("DPOP_TOKEN_INVALID");
        return new DeviceAccessContext(tenantId, deviceId, thumbprint, keyVersion, authorizationVersion);
    }

    private static ControlPlaneException Unauthorized(string code) =>
        new(401, code, "Device DPoP authentication failed.");
}
