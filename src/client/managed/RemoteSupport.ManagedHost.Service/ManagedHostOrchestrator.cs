using Microsoft.Extensions.Logging;
using RemoteSupport.Ipc.V1;
using RemoteSupport.Protocol.V1;

namespace RemoteSupport.ManagedHost.Service;

public sealed record ManagedHostDeviceState(Guid DeviceId, IDeviceIdentityKey Key, int KeyVersion,
    string AppVersion, string OsVersion)
{
    public string? Credential { get; set; }
    public DateTimeOffset CredentialExpiresAt { get; set; }
}

/// <summary>
/// Drives one credential-refresh/heartbeat/poll/decide cycle of the managed-host command
/// channel (02-protocol/managed-host-command-channel.md). Kept independent of any hosting
/// framework so the cycle logic is directly unit-testable with fake credential clients and
/// launchers.
/// </summary>
public sealed class ManagedHostOrchestrator(
    DeviceCredentialClient credentialClient,
    IInteractiveAgentLauncher agentLauncher,
    ManagedHostDeviceState device,
    ILogger<ManagedHostOrchestrator> logger,
    TimeSpan? consentTimeout = null)
{
    private readonly TimeSpan consentTimeout = consentTimeout ?? TimeSpan.FromSeconds(60);

    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        await EnsureCredentialAsync(cancellationToken).ConfigureAwait(false);
        await credentialClient.SendHeartbeatAsync(device.Key, device.Credential!,
            new DeviceHeartbeat(device.AppVersion, device.OsVersion, "HEALTHY", 1, DateTimeOffset.UtcNow),
            cancellationToken).ConfigureAwait(false);

        PagedManagedSessionRequests pending = await credentialClient
            .PollPendingSessionsAsync(device.Key, device.Credential!, waitSeconds: 20, cancellationToken)
            .ConfigureAwait(false);
        foreach (PendingManagedSessionRequest item in pending.Items)
        {
            try
            {
                await ProcessPendingSessionAsync(item, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is DeviceCredentialException or InvalidOperationException)
            {
                Log.PendingSessionFailed(logger, item.SessionId, exception);
            }
        }
    }

    private async Task EnsureCredentialAsync(CancellationToken cancellationToken)
    {
        if (device.Credential is not null && device.CredentialExpiresAt > DateTimeOffset.UtcNow + TimeSpan.FromMinutes(1))
            return;
        DeviceCredentialResult result = await credentialClient
            .RefreshCredentialAsync(device.Key, device.KeyVersion, cancellationToken).ConfigureAwait(false);
        device.Credential = result.DeviceCredential;
        device.CredentialExpiresAt = result.ExpiresAt;
    }

    private async Task ProcessPendingSessionAsync(PendingManagedSessionRequest item, CancellationToken cancellationToken)
    {
        bool isUnattended = item.SessionType == "UNATTENDED";
        ManagedSessionConsentRequest consentRequest = new()
        {
            SessionId = item.SessionId.ToString("D"),
            OperatorDisplayName = item.Operator.DisplayName,
            OperatorTenantDisplayName = item.Operator.TenantDisplayName,
            // Unattended is already policy-bound, MFA-stepped-up and device-opted-in
            // server-side (05-security/unattended-threat-model.md §3); the local prompt is
            // a mandatory *notification* here, never a gate the server waits on, since no
            // local human may be present to answer it.
            PolicyRequiresConsent = item.LocalConsentRequired && !isUnattended,
            ExpiresUtcUnixMs = item.ExpiresAt.ToUnixTimeMilliseconds(),
        };
        consentRequest.RequestedScopes.AddRange(item.RequestedScopes.Select(ParseScope).Where(scope => scope is not null)
            .Select(scope => scope!.Value));

        bool approved;
        string[] grantedScopes;
        if (isUnattended)
        {
            approved = true;
            grantedScopes = item.RequestedScopes.ToArray();
            // Best-effort local notification only; a missing/unreachable Agent must not
            // block or fail an already server-authorized unattended session, but is logged
            // because "no hidden indicator" still requires attempting to show one.
            try
            {
                ManagedSessionConsentResult? notified = await agentLauncher
                    .RequestConsentAsync(consentRequest, consentTimeout, cancellationToken).ConfigureAwait(false);
                if (notified is null) Log.UnattendedNotificationUndelivered(logger, item.SessionId);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Log.UnattendedNotificationFailed(logger, item.SessionId, exception);
            }
        }
        else
        {
            ManagedSessionConsentResult? consent = await agentLauncher
                .RequestConsentAsync(consentRequest, consentTimeout, cancellationToken).ConfigureAwait(false);
            approved = consent?.Approved ?? false;
            grantedScopes = approved
                ? consent!.GrantedScopes.Select(FormatScope).Where(scope => scope is not null).Select(scope => scope!).ToArray()
                : [];
        }

        using CngDeviceIdentityKey hostEphemeralKey = CngDeviceIdentityKey.CreateEphemeral();
        try
        {
            byte[] proofBytes = ManagedHostSignedPayloads.ManagedHostDecisionProof(item.SessionId, approved,
                grantedScopes, item.ConsentNonce, hostEphemeralKey.Thumbprint);
            ManagedHostDecisionRequest decision = new(approved, grantedScopes, item.ConsentNonce, hostEphemeralKey.PublicJwk,
                new DetachedProof(item.ConsentNonce, device.Key.Thumbprint, "ecdsa-p256-sha256-p1363", device.Key.Sign(proofBytes)));
            ManagedHostDecisionResult result = await credentialClient
                .SubmitDecisionAsync(device.Key, device.Credential!, item.SessionId, item.StateVersion, decision, cancellationToken)
                .ConfigureAwait(false);
            Log.DecisionSubmitted(logger, item.SessionId, result.Session.State);
        }
        finally
        {
            hostEphemeralKey.Delete();
        }
    }

    private static readonly IReadOnlyDictionary<string, CapabilityScope> ScopesByName = new Dictionary<string, CapabilityScope>(StringComparer.Ordinal)
    {
        ["VIEW_SCREEN"] = CapabilityScope.ViewScreen,
        ["CONTROL_POINTER"] = CapabilityScope.ControlPointer,
        ["CONTROL_KEYBOARD"] = CapabilityScope.ControlKeyboard,
        ["SYNC_CLIPBOARD_TEXT_HOST_TO_OPERATOR"] = CapabilityScope.SyncClipboardTextHostToOperator,
        ["SYNC_CLIPBOARD_TEXT_OPERATOR_TO_HOST"] = CapabilityScope.SyncClipboardTextOperatorToHost,
        ["TRANSFER_FILE_HOST_TO_OPERATOR"] = CapabilityScope.TransferFileHostToOperator,
        ["TRANSFER_FILE_OPERATOR_TO_HOST"] = CapabilityScope.TransferFileOperatorToHost,
        ["CHAT"] = CapabilityScope.Chat,
        ["SWITCH_MONITOR"] = CapabilityScope.SwitchMonitor,
        ["REQUEST_REBOOT"] = CapabilityScope.RequestReboot,
        ["RECONNECT_AFTER_REBOOT"] = CapabilityScope.ReconnectAfterReboot,
        ["UNATTENDED_SESSION"] = CapabilityScope.UnattendedSession,
    };
    private static readonly IReadOnlyDictionary<CapabilityScope, string> ScopeNames =
        ScopesByName.ToDictionary(pair => pair.Value, pair => pair.Key);

    private static CapabilityScope? ParseScope(string scope) => ScopesByName.GetValueOrDefault(scope);

    private static string? FormatScope(CapabilityScope scope) => ScopeNames.GetValueOrDefault(scope);
}

internal static partial class Log
{
    [LoggerMessage(EventId = 10, Level = LogLevel.Information,
        Message = "Managed-host decision submitted for session {SessionId}; new state {State}.")]
    public static partial void DecisionSubmitted(ILogger logger, Guid sessionId, string state);

    [LoggerMessage(EventId = 11, Level = LogLevel.Warning,
        Message = "Processing pending managed session {SessionId} failed.")]
    public static partial void PendingSessionFailed(ILogger logger, Guid sessionId, Exception exception);

    [LoggerMessage(EventId = 12, Level = LogLevel.Warning,
        Message = "Unattended session {SessionId} local notification was not delivered to any Agent.")]
    public static partial void UnattendedNotificationUndelivered(ILogger logger, Guid sessionId);

    [LoggerMessage(EventId = 13, Level = LogLevel.Warning,
        Message = "Unattended session {SessionId} local notification attempt failed.")]
    public static partial void UnattendedNotificationFailed(ILogger logger, Guid sessionId, Exception exception);
}
