using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using RemoteSupport.ManagedHost.Service;

namespace RemoteSupport.ManagedHost.Service.Tests;

/// <summary>
/// A minimal, signature-verifying fake of the managed-host device endpoints, used to prove
/// the client's request construction, DPoP header presence and canonical-proof signatures
/// are actually verifiable — not merely that the client sends *something*.
/// </summary>
internal sealed class FakeManagedHostServer : HttpMessageHandler
{
    public readonly Guid DeviceId = Guid.NewGuid();
    public readonly Guid SessionId = Guid.NewGuid();
    public readonly Guid ChallengeId = Guid.NewGuid();
    public const string Nonce = "0123456789abcdef01234567";
    public int PollCallCount;
    public int DecisionCallCount;
    public bool LastDecisionApproved;
    public string[] LastDecisionGrantedScopes = [];

    private readonly JsonElement deviceKeyJwk;

    public FakeManagedHostServer(JsonElement deviceKeyJwk)
    {
        this.deviceKeyJwk = deviceKeyJwk;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string path = request.RequestUri!.AbsolutePath;
        if (path.EndsWith("/credential-challenges", StringComparison.Ordinal))
            return Json(new DeviceCredentialChallenge(ChallengeId, Nonce, DateTimeOffset.UtcNow.AddMinutes(5),
                "RSP-DEVICE-CREDENTIAL-V1", "CREDENTIAL_REFRESH"));

        if (path.EndsWith("/credentials", StringComparison.Ordinal))
        {
            DeviceCredentialExchangeRequest exchange = (await request.Content!.ReadFromJsonAsync<DeviceCredentialExchangeRequest>(
                ManagedHostJson.Options, cancellationToken))!;
            byte[] expected = ManagedHostSignedPayloads.DeviceCredentialProof("RSP-DEVICE-CREDENTIAL-V1", DeviceId,
                exchange.ChallengeId, "CREDENTIAL_REFRESH", exchange.Proof.Nonce);
            if (!VerifyP256(deviceKeyJwk, expected, exchange.Proof.Signature))
                return new HttpResponseMessage(HttpStatusCode.Forbidden);
            return Json(new DeviceCredentialResult("fake-device-token", DateTimeOffset.UtcNow.AddMinutes(15), 1, exchange.KeyVersion));
        }

        RequireDpop(request);

        if (path.EndsWith("/heartbeat", StringComparison.Ordinal))
            return new HttpResponseMessage(HttpStatusCode.NoContent);

        if (path.Contains("/pending-session-requests", StringComparison.Ordinal))
        {
            PollCallCount++;
            PendingManagedSessionRequest[] items = PollCallCount == 1
                ?
                [
                    new PendingManagedSessionRequest(SessionId, "MANAGED_ATTENDED",
                        new PendingOperatorDisplay(Guid.NewGuid(), "Test Operator", "Test Tenant"),
                        ["VIEW_SCREEN", "CONTROL_POINTER"], "deadbeef", "consent-nonce-0000000000000000", true, true,
                        DateTimeOffset.UtcNow.AddMinutes(10), 1),
                ]
                : [];
            return Json(new PagedManagedSessionRequests(items));
        }

        if (path.EndsWith("/managed-host-decision", StringComparison.Ordinal))
        {
            DecisionCallCount++;
            ManagedHostDecisionRequest decision = (await request.Content!.ReadFromJsonAsync<ManagedHostDecisionRequest>(
                ManagedHostJson.Options, cancellationToken))!;
            LastDecisionApproved = decision.Approved;
            LastDecisionGrantedScopes = [.. decision.GrantedScopes];
            string hostThumbprint = CngDeviceIdentityKey.ComputeThumbprint(decision.HostEphemeralPublicKey);
            byte[] expected = ManagedHostSignedPayloads.ManagedHostDecisionProof(SessionId, decision.Approved,
                decision.GrantedScopes, decision.ConsentNonce, hostThumbprint);
            if (!VerifyP256(deviceKeyJwk, expected, decision.DecisionProof.Signature))
                return new HttpResponseMessage(HttpStatusCode.Forbidden);
            SessionResponse session = new(SessionId, Guid.NewGuid(), "MANAGED_ATTENDED",
                decision.Approved ? "AUTHORIZED" : "FAILED", 2, 1, 1, ["VIEW_SCREEN", "CONTROL_POINTER"],
                decision.GrantedScopes, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(10));
            return Json(new ManagedHostDecisionResult(session, Guid.NewGuid(), decision.Approved ? "fake-host-bootstrap" : null));
        }

        throw new InvalidOperationException($"Unhandled fake managed-host request: {path}");
    }

    private static void RequireDpop(HttpRequestMessage request)
    {
        if (request.Headers.Authorization?.Scheme != "DPoP" || !request.Headers.Contains("DPoP"))
            throw new InvalidOperationException("Expected a DPoP-authenticated request.");
    }

    private static bool VerifyP256(JsonElement jwk, byte[] data, string signature)
    {
        byte[] x = Base64UrlDecode(jwk.GetProperty("x").GetString()!);
        byte[] y = Base64UrlDecode(jwk.GetProperty("y").GetString()!);
        using ECDsa ecdsa = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = x, Y = y } });
        return ecdsa.VerifyData(data, Base64UrlDecode(signature), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    private static byte[] Base64UrlDecode(string value)
    {
        string normalized = value.Replace('-', '+').Replace('_', '/');
        normalized += new string('=', (4 - normalized.Length % 4) % 4);
        return Convert.FromBase64String(normalized);
    }

    private static HttpResponseMessage Json<T>(T body) => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(body, options: ManagedHostJson.Options),
    };
}
