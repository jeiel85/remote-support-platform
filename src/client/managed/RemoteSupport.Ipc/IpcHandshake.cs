using System.Security.Cryptography;
using Google.Protobuf;
using RemoteSupport.Ipc.V1;

namespace RemoteSupport.Ipc;

/// <summary>
/// Mutual challenge-response handshake over the one-time launch secret the Service
/// passes to the Agent it just started (01-architecture/windows-process-model.md:
/// "Pass only a one-time bootstrap handle/token through secure IPC"). Both sides prove
/// possession of the same secret before any privileged message is accepted; OS-verified
/// caller identity (NamedPipeSecurity) and image verification are separate, required checks.
/// </summary>
public sealed class IpcHandshakeException(string message) : Exception(message);

public static class IpcHandshake
{
    public const uint ProtocolMajor = 1;
    public const uint ProtocolMinor = 0;

    private static byte[] Hmac(byte[] launchSecret, byte[] nonce, string label)
    {
        byte[] message = [.. System.Text.Encoding.ASCII.GetBytes(label), 0, .. nonce];
        return HMACSHA256.HashData(launchSecret, message);
    }

    public static ClientHello CreateClientHello(byte[] launchSecret, byte[] clientNonce, string executableVersion,
        byte[] executableSha256, IEnumerable<BrokerCapability> requestedCapabilities)
    {
        ClientHello hello = new()
        {
            LaunchNonceProof = ByteString.CopyFrom(Hmac(launchSecret, clientNonce, "client-hello")),
            ClientNonce = ByteString.CopyFrom(clientNonce),
            ExecutableVersion = executableVersion,
            ExecutableSha256 = ByteString.CopyFrom(executableSha256),
        };
        hello.RequestedCapabilities.AddRange(requestedCapabilities);
        return hello;
    }

    public static void VerifyClientHello(byte[] launchSecret, ClientHello hello)
    {
        byte[] expected = Hmac(launchSecret, hello.ClientNonce.ToByteArray(), "client-hello");
        if (hello.ClientNonce.Length is < 16 or > 64 ||
            !CryptographicOperations.FixedTimeEquals(expected, hello.LaunchNonceProof.ToByteArray()))
            throw new IpcHandshakeException("Client launch-nonce proof was invalid.");
    }

    public static ServiceHello CreateServiceHello(byte[] launchSecret, byte[] clientNonce, byte[] serviceNonce,
        string serviceVersion, uint maxMessageBytes, IEnumerable<BrokerCapability> grantedCapabilities,
        long capabilityExpiresUtcUnixMs) => new()
    {
        ServiceNonce = ByteString.CopyFrom(serviceNonce),
        ClientNonceSignature = ByteString.CopyFrom(Hmac(launchSecret, clientNonce, "service-hello")),
        ServiceVersion = serviceVersion,
        MaxMessageBytes = maxMessageBytes,
        CapabilityExpiresUtcUnixMs = capabilityExpiresUtcUnixMs,
        GrantedCapabilities = { grantedCapabilities },
    };

    public static void VerifyServiceHello(byte[] launchSecret, byte[] clientNonce, ServiceHello hello)
    {
        byte[] expected = Hmac(launchSecret, clientNonce, "service-hello");
        if (hello.ServiceNonce.Length is < 16 or > 64 ||
            !CryptographicOperations.FixedTimeEquals(expected, hello.ClientNonceSignature.ToByteArray()))
            throw new IpcHandshakeException("Service hello proof was invalid.");
    }

    public static ChallengeResponse CreateChallengeResponse(byte[] launchSecret, byte[] serviceNonce) => new()
    {
        ServiceNonceSignature = ByteString.CopyFrom(Hmac(launchSecret, serviceNonce, "challenge-response")),
    };

    public static void VerifyChallengeResponse(byte[] launchSecret, byte[] serviceNonce, ChallengeResponse response)
    {
        byte[] expected = Hmac(launchSecret, serviceNonce, "challenge-response");
        if (!CryptographicOperations.FixedTimeEquals(expected, response.ServiceNonceSignature.ToByteArray()))
            throw new IpcHandshakeException("Challenge response proof was invalid.");
    }

    public static byte[] GenerateNonce(int bytes = 32) => RandomNumberGenerator.GetBytes(bytes);

    /// <summary>Drives the client side of the handshake over an already-connected transport.
    /// Returns once mutual proof succeeds; throws IpcHandshakeException otherwise.</summary>
    public static async Task<ServiceHello> RunClientHandshakeAsync(IpcMessageTransport transport, byte[] launchSecret,
        string executableVersion, byte[] executableSha256, IEnumerable<BrokerCapability> requestedCapabilities,
        CancellationToken cancellationToken)
    {
        byte[] clientNonce = GenerateNonce();
        ClientHello hello = CreateClientHello(launchSecret, clientNonce, executableVersion, executableSha256, requestedCapabilities);
        await transport.SendAsync(new IpcEnvelope { ClientHello = hello, ProtocolMajor = ProtocolMajor, ProtocolMinor = ProtocolMinor }, cancellationToken).ConfigureAwait(false);
        IpcEnvelope response = await transport.ReceiveAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new IpcHandshakeException("Pipe closed before the service hello arrived.");
        if (response.BodyCase != IpcEnvelope.BodyOneofCase.ServiceHello)
            throw new IpcHandshakeException("Expected a service hello.");
        VerifyServiceHello(launchSecret, clientNonce, response.ServiceHello);
        ChallengeResponse challenge = CreateChallengeResponse(launchSecret, response.ServiceHello.ServiceNonce.ToByteArray());
        await transport.SendAsync(new IpcEnvelope { ChallengeResponse = challenge, ProtocolMajor = ProtocolMajor, ProtocolMinor = ProtocolMinor }, cancellationToken).ConfigureAwait(false);
        return response.ServiceHello;
    }

    /// <summary>Drives the service side of the handshake. Returns the verified client hello.</summary>
    public static async Task<ClientHello> RunServiceHandshakeAsync(IpcMessageTransport transport, byte[] launchSecret,
        string serviceVersion, uint maxMessageBytes, IEnumerable<BrokerCapability> grantedCapabilities,
        long capabilityExpiresUtcUnixMs, CancellationToken cancellationToken)
    {
        IpcEnvelope request = await transport.ReceiveAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new IpcHandshakeException("Pipe closed before the client hello arrived.");
        if (request.BodyCase != IpcEnvelope.BodyOneofCase.ClientHello)
            throw new IpcHandshakeException("Expected a client hello.");
        VerifyClientHello(launchSecret, request.ClientHello);
        byte[] serviceNonce = GenerateNonce();
        ServiceHello hello = CreateServiceHello(launchSecret, request.ClientHello.ClientNonce.ToByteArray(), serviceNonce,
            serviceVersion, maxMessageBytes, grantedCapabilities, capabilityExpiresUtcUnixMs);
        await transport.SendAsync(new IpcEnvelope { ServiceHello = hello, ProtocolMajor = ProtocolMajor, ProtocolMinor = ProtocolMinor }, cancellationToken).ConfigureAwait(false);
        IpcEnvelope challengeEnvelope = await transport.ReceiveAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new IpcHandshakeException("Pipe closed before the challenge response arrived.");
        if (challengeEnvelope.BodyCase != IpcEnvelope.BodyOneofCase.ChallengeResponse)
            throw new IpcHandshakeException("Expected a challenge response.");
        VerifyChallengeResponse(launchSecret, serviceNonce, challengeEnvelope.ChallengeResponse);
        return request.ClientHello;
    }
}
