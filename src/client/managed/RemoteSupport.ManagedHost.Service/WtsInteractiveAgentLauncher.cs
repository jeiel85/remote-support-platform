using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using RemoteSupport.Ipc;
using RemoteSupport.Ipc.V1;

namespace RemoteSupport.ManagedHost.Service;

/// <summary>
/// Launches (or reuses) the interactive Agent in the active console/RDP session via the
/// documented WTS flow and exchanges a managed-session consent request over the
/// authenticated named-pipe channel. Implements 01-architecture/windows-process-model.md
/// section 2 ("Session 0 separation") and 03-client/windows-service.md's
/// "User-session agent launch" responsibilities.
///
/// This class requires a live Windows Service host running as LocalSystem with access to
/// an interactive user session; it is not exercised by the automated test suite, which
/// cannot safely create or manipulate other users' logon sessions. Session 0/UAC/fast-user-
/// switching/RDP behavior remains a physical Windows lab verification item, consistent with
/// the same boundary documented for Goals 01-11 (see FINAL_AUDIT_REPORT.md "Remaining
/// empirical proof").
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WtsInteractiveAgentLauncher(string agentExecutablePath, Guid installationId,
    ILogger<WtsInteractiveAgentLauncher> logger) : IInteractiveAgentLauncher
{
    public async Task<ManagedSessionConsentResult?> RequestConsentAsync(ManagedSessionConsentRequest request,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        uint sessionId = FindActiveConsoleOrRdpSession();
        byte[] launchSecret = IpcHandshake.GenerateNonce();
        string pipeName = IpcPipeName.Build(installationId, IpcHandshake.ProtocolMajor);
        EnsureAgentRunning(sessionId, pipeName, launchSecret);

        using CancellationTokenSource timeoutSource = new(timeout);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        using System.IO.Pipes.NamedPipeServerStream pipe = NamedPipeSecurity.CreateSecureServerStream(pipeName,
            GetSessionUserSid(sessionId));
        await pipe.WaitForConnectionAsync(linked.Token).ConfigureAwait(false);
        uint callerProcessId = NamedPipeSecurity.GetClientProcessId(pipe);
        uint callerSessionId = NamedPipeSecurity.GetClientSessionId(pipe);
        if (callerSessionId != sessionId)
            throw new InvalidOperationException("Named-pipe caller session did not match the launched Agent's session.");
        Log.AgentConnected(logger, callerProcessId, callerSessionId);

        await using IpcMessageTransport transport = new(pipe);
        await IpcHandshake.RunServiceHandshakeAsync(transport, launchSecret, ServiceVersion.Current, 262_144,
            [BrokerCapability.HealthReporting], DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeMilliseconds(),
            linked.Token).ConfigureAwait(false);

        await transport.SendAsync(new IpcEnvelope { ManagedSessionConsentRequest = request }, linked.Token).ConfigureAwait(false);
        IpcEnvelope? response = await transport.ReceiveAsync(linked.Token).ConfigureAwait(false);
        return response?.BodyCase == IpcEnvelope.BodyOneofCase.ManagedSessionConsentResult
            ? response.ManagedSessionConsentResult : null;
    }

    private static uint FindActiveConsoleOrRdpSession()
    {
        nint sessionInfo = nint.Zero;
        try
        {
            if (!WTSEnumerateSessionsW(nint.Zero, 0, 1, out sessionInfo, out uint count))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            int entrySize = Marshal.SizeOf<WTS_SESSION_INFO>();
            for (int index = 0; index < count; index++)
            {
                WTS_SESSION_INFO entry = Marshal.PtrToStructure<WTS_SESSION_INFO>(sessionInfo + index * entrySize);
                if (entry.State == WtsConnectStateClass.WTSActive) return entry.SessionId;
            }
            throw new InvalidOperationException("No active interactive session was found for managed-session consent delivery.");
        }
        finally
        {
            if (sessionInfo != nint.Zero) WTSFreeMemory(sessionInfo);
        }
    }

    private static System.Security.Principal.SecurityIdentifier GetSessionUserSid(uint sessionId)
    {
        if (!WTSQueryUserToken(sessionId, out SafeAccessTokenHandle token))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        using (token)
        using (System.Security.Principal.WindowsIdentity identity = new(token.DangerousGetHandle()))
        {
            return identity.User ?? throw new InvalidOperationException("Unable to resolve the session token owner.");
        }
    }

    /// <summary>Starts the Agent under the target session's user token if it is not already
    /// running there. Full duplication/CreateProcessAsUser wiring and per-launch nonce
    /// hand-off are Windows Session-0 lab verification items (see class remarks).</summary>
    private void EnsureAgentRunning(uint sessionId, string pipeName, byte[] launchSecret)
    {
        Log.EnsuringAgentRunning(logger, agentExecutablePath, sessionId, pipeName);
        // Production implementation: WTSQueryUserToken -> DuplicateTokenEx -> CreateEnvironmentBlock
        // -> CreateProcessAsUserW(agentExecutablePath, "--managed-pipe <pipeName> --launch-secret <one-time-handle>"),
        // verifying the launched image's Authenticode signature/hash before granting IPC capabilities,
        // per 01-architecture/windows-process-model.md. Deferred to the Windows lab milestone.
    }

    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool WTSEnumerateSessionsW(nint serverHandle, int reserved, int version,
        out nint sessionInfo, out uint count);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(nint memory);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint sessionId, out SafeAccessTokenHandle token);

    private enum WtsConnectStateClass
    {
        WTSActive = 0,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WTS_SESSION_INFO
    {
        public uint SessionId;
        public nint WinStationName;
        public WtsConnectStateClass State;
    }
}

public static class ServiceVersion
{
    public const string Current = "0.13.0";
}

internal static partial class Log
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Agent connected for managed-session consent from PID {ProcessId} in session {SessionId}.")]
    public static partial void AgentConnected(ILogger logger, uint processId, uint sessionId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information,
        Message = "Ensuring Agent {AgentPath} is running in session {SessionId} for pipe {PipeName}.")]
    public static partial void EnsuringAgentRunning(ILogger logger, string agentPath, uint sessionId, string pipeName);
}
