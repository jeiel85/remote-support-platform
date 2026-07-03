using System.Net.Http;
using System.Net.WebSockets;
using System.IO;
using System.Windows;
using RemoteSupport.Application;
using RemoteSupport.Infrastructure;
using RemoteSupport.Observability;
using RemoteSupport.Protocol;
using RemoteSupport.Protocol.V1;

namespace RemoteSupport.Agent.App;

public partial class MainWindow : Window, IDisposable
{
    private CancellationTokenSource? sessionCancellation;
    private HttpClient? http;
    private EphemeralPeerIdentity? identity;
    private HostSessionCreated? session;
    private bool disposed;
    private readonly CrashRecoveryGuard crashRecovery = CrashRecoveryGuard.Start("agent", ProductVersion.Current);
    private NativeAttendedSession? nativeSession;
    private SignalingClient? signaling;
    private AttendedControlPlaneClient? controlPlane;
    private PeerAuthorizationResponse? peerAuthorization;
    private PeerDataSession? peerData;
    private PeerChatService? chat;
    private PeerClipboardController? clipboardController;
    private PeerFileTransferController? fileTransfers;
    private EmergencyHotkey? emergencyHotkey;
    private LocalDataFeatureAuditSink? dataAudit;
    private long sessionStateVersion;
    private readonly System.Windows.Threading.DispatcherTimer indicatorTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTimeOffset? connectedAt;
    private string remoteDisplayName = string.Empty;

    public MainWindow()
    {
        InitializeComponent();
        indicatorTimer.Tick += (_, _) => UpdateSessionIndicator();
        SourceInitialized += (_, _) => emergencyHotkey = EmergencyHotkey.Register(this, () =>
        {
            Disconnect();
            StatusText.Text = Resource("StatusEmergency");
            StartButton.IsEnabled = true;
        });
        Closed += (_, _) => Dispose();
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        StartButton.IsEnabled = false;
        try
        {
            string configurationPath = Path.Combine(AppContext.BaseDirectory, "client-config.json");
            ClientConfiguration configuration = await ClientConfiguration.LoadAsync(configurationPath);
            sessionCancellation = new CancellationTokenSource();
            identity = new EphemeralPeerIdentity();
            http = new HttpClient { BaseAddress = configuration.ApiBaseUrl, Timeout = TimeSpan.FromSeconds(20) };
            AttendedControlPlaneClient client = new(http);
            controlPlane = client;
            StatusText.Text = Resource("StatusCreating");
            session = await client.CreateHostSessionAsync(identity,
                System.Globalization.CultureInfo.CurrentUICulture.Name, sessionCancellation.Token);
            SupportCodeText.Text = session.SupportCode;
            ExpiryText.Text = session.ExpiresAt.LocalDateTime.ToString("g", System.Globalization.CultureInfo.CurrentCulture);
            CopyButton.IsEnabled = true;
            DisconnectButton.IsEnabled = true;
            StatusText.Text = Resource("StatusAwaitingOperator");
            await PollForConsentAsync(client, sessionCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = Resource("StatusDisconnected");
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException or ControlPlaneClientException or
                                          InvalidOperationException or DllNotFoundException or BadImageFormatException)
        {
            StatusText.Text = exception is ControlPlaneClientException control ? $"{control.Code}: {control.Message}" : exception.Message;
            Disconnect();
            StartButton.IsEnabled = true;
        }
    }

    private async Task PollForConsentAsync(AttendedControlPlaneClient client, CancellationToken cancellationToken)
    {
        while (session is not null && identity is not null)
        {
            PendingConsentResponse? pending = await client.GetPendingConsentAsync(session, cancellationToken);
            if (pending is null)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                continue;
            }
            VerifiedConsentRequest request = new(pending.SessionId, pending.ConsentRequestId,
                pending.Operator.DisplayName, pending.Operator.TenantDisplayName, pending.Operator.VerifiedTenant,
                pending.RequestedScopes, pending.ExpiresAt, pending.StateVersion);
            remoteDisplayName = pending.Operator.DisplayName;
            ConsentWindow consent = new() { Owner = this };
            consent.Present(request);
            bool? accepted = consent.ShowDialog();
            IReadOnlyList<string> granted = accepted == true ? consent.SelectedScopes : [];
            ConsentSessionResponse result = await client.DecideConsentAsync(session, pending, identity,
                accepted == true, granted, cancellationToken);
            sessionStateVersion = result.StateVersion;
            if (accepted != true)
            {
                Disconnect();
                StatusText.Text = Resource("StatusDenied");
                StartButton.IsEnabled = true;
                return;
            }
            PeerAuthorizationResponse peer = await client.AuthorizePeerAsync(session.SessionId, session.HostBootstrapToken,
                identity, cancellationToken);
            peerAuthorization = peer;
            ActiveScopes.ItemsSource = peer.GrantedScopes;
            RevokeScopesButton.IsEnabled = peer.GrantedScopes.Count != 0;
            Task<SignalingTicketResponse> ticketTask = client.GetSignalingTicketAsync(peer, identity, cancellationToken);
            Task<TurnCredentialsResponse> turnTask = client.GetTurnCredentialsAsync(peer, identity, cancellationToken);
            await Task.WhenAll(ticketTask, turnTask);
            nativeSession = new NativeAttendedSession(peer, identity, await turnTask);
            peerData = new PeerDataSession(peer);
            chat = new PeerChatService(SessionPeerRole.Host, peerData.PermissionGate);
            clipboardController = new PeerClipboardController(peerData, nativeSession, 256 * 1024);
            clipboardController.TextReady += text => Dispatcher.BeginInvoke(() =>
            {
                try { Clipboard.SetText(text); } catch (System.Runtime.InteropServices.COMException) { StatusText.Text = Resource("StatusClipboardLocked"); }
            });
            string received = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Remote Support");
            dataAudit = new LocalDataFeatureAuditSink("agent");
            fileTransfers = new PeerFileTransferController(peer.SessionId, peerData, nativeSession, received,
                safety: new WindowsAttachmentSafety(), audit: dataAudit);
            fileTransfers.ApprovalRequested = acceptance => Dispatcher.InvokeAsync(() => MessageBox.Show(this,
                string.Format(System.Globalization.CultureInfo.CurrentCulture, System.Text.CompositeFormat.Parse(Resource("ReceiveFileFormat")), acceptance.NormalizedName, acceptance.DestinationPath), Resource("IncomingFile"),
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes).Task;
            fileTransfers.StatusChanged += status => Dispatcher.BeginInvoke(() => StatusText.Text = status);
            signaling = new SignalingClient();
            nativeSession.LocalDescriptionReady += description => _ = RelayDescriptionAsync(description);
            nativeSession.LocalIceCandidateReady += candidate => _ = RelayCandidateAsync(candidate);
            nativeSession.StateChanged += state => Dispatcher.BeginInvoke(() => StatusText.Text = state);
            nativeSession.SecurityBindingChanged += state => Dispatcher.BeginInvoke(() => StatusText.Text = state);
            nativeSession.ErrorOccurred += code => Dispatcher.BeginInvoke(() => StatusText.Text = code);
            nativeSession.DataReceived += HandleData;
            signaling.MessageReceived += HandleSignalingAsync;
            await signaling.ConnectAsync(await ticketTask, peer, cancellationToken);
            nativeSession.StartHostCapture();
            NativeDisplay display = nativeSession.Displays[0];
            nativeSession.EnableHostInput(display.Generation,
                peer.GrantedScopes.Contains("CONTROL_POINTER", StringComparer.Ordinal),
                peer.GrantedScopes.Contains("CONTROL_KEYBOARD", StringComparer.Ordinal));
            connectedAt = DateTimeOffset.UtcNow;
            indicatorTimer.Start();
            UpdateSessionIndicator();
            ApplyScopeControls(peer.GrantedScopes);
            StatusText.Text = Resource("StatusNegotiating");
            return;
        }
    }

    private void HandleData(NativeDataPacket packet)
    {
        if (peerData is null || nativeSession is null) return;
        try
        {
            Envelope envelope = peerData.Decode(packet.Label, packet.Payload);
            switch (envelope.BodyCase)
            {
                case Envelope.BodyOneofCase.PointerEvent:
                    peerData.PermissionGate.Demand(RemoteSupport.Domain.CapabilityScope.ControlPointer,
                        envelope.PermissionRevision, "INPUT_PERMISSION_REVOKED");
                    nativeSession.InjectPointer(envelope.PointerEvent);
                    break;
                case Envelope.BodyOneofCase.KeyboardEvent:
                    peerData.PermissionGate.Demand(RemoteSupport.Domain.CapabilityScope.ControlKeyboard,
                        envelope.PermissionRevision, "INPUT_PERMISSION_REVOKED");
                    nativeSession.InjectKeyboard(envelope.KeyboardEvent);
                    break;
                case Envelope.BodyOneofCase.ReleaseAllInput:
                    nativeSession.ReleaseAllInput(envelope.ReleaseAllInput.ThroughInputSequence);
                    break;
                case Envelope.BodyOneofCase.ChatMessage when chat is not null:
                    PeerChatMessage message = new(Guid.Parse(envelope.ChatMessage.ChatMessageId), envelope.ChatMessage.Text,
                        DateTimeOffset.FromUnixTimeMilliseconds(envelope.ChatMessage.SentUtcUnixMs));
                    PeerChatReceipt receipt = chat.Receive(message, SessionPeerRole.Operator, envelope.PermissionRevision, DateTimeOffset.UtcNow);
                    if (receipt.Delivered) Dispatcher.BeginInvoke(() => ChatMessages.Items.Add($"{Resource("OperatorPrefix")}: {message.Text}"));
                    byte[] ack = peerData.Encode(PeerChannel.Chat, value => value.ChatAck = new ChatAck
                    {
                        ChatMessageId = receipt.MessageId.ToString("D"),
                        ReceivedUtcUnixMs = receipt.ReceivedAt.ToUnixTimeMilliseconds(),
                    });
                    nativeSession.Send("rsp.chat.v1", ack);
                    break;
                case Envelope.BodyOneofCase.ClipboardOffer:
                case Envelope.BodyOneofCase.ClipboardDecision:
                case Envelope.BodyOneofCase.ClipboardText:
                    Dispatcher.BeginInvoke(() => clipboardController?.Handle(envelope));
                    break;
                case Envelope.BodyOneofCase.FileOffer:
                case Envelope.BodyOneofCase.FileDecision:
                case Envelope.BodyOneofCase.FileChunk:
                case Envelope.BodyOneofCase.FileAck:
                case Envelope.BodyOneofCase.FileCancel:
                    Dispatcher.BeginInvoke(async () =>
                    {
                        try { if (fileTransfers is not null) await fileTransfers.HandleAsync(envelope); }
                        catch (Exception exception) when (exception is IOException or DataFeatureException or InvalidOperationException or OperationCanceledException)
                        { StatusText.Text = exception.Message; }
                    });
                    break;
            }
        }
        catch (Exception exception) when (exception is PeerProtocolException or DataFeatureException or InvalidOperationException)
        {
            Dispatcher.BeginInvoke(() => StatusText.Text = exception.Message);
        }
    }

    private void SendClipboard_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (clipboardController is not null && Clipboard.ContainsText(TextDataFormat.UnicodeText))
                clipboardController.Offer(Clipboard.GetText(TextDataFormat.UnicodeText));
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or DataFeatureException)
        { StatusText.Text = exception.Message; }
    }

    private async void SendFile_Click(object sender, RoutedEventArgs e)
    {
        if (fileTransfers is null) return;
        Microsoft.Win32.OpenFileDialog picker = new() { CheckFileExists = true, Multiselect = false };
        if (picker.ShowDialog(this) != true) return;
        try { await fileTransfers.OfferAsync(picker.FileName); }
        catch (Exception exception) when (exception is IOException or DataFeatureException)
        { StatusText.Text = exception.Message; }
    }

    private async void CancelTransfers_Click(object sender, RoutedEventArgs e)
    {
        if (fileTransfers is null) return;
        try { await fileTransfers.CancelAllAsync(); }
        catch (Exception exception) when (exception is IOException or DataFeatureException or InvalidOperationException)
        { StatusText.Text = exception.Message; }
    }

    private void SendChat_Click(object sender, RoutedEventArgs e)
    {
        if (peerData is null || nativeSession is null || chat is null) return;
        try
        {
            PeerChatMessage message = chat.Create(ChatInput.Text, DateTimeOffset.UtcNow);
            byte[] frame = peerData.Encode(PeerChannel.Chat, value => value.ChatMessage = new ChatMessage
            {
                ChatMessageId = message.MessageId.ToString("D"),
                Text = message.Text,
                SentUtcUnixMs = message.SentAt.ToUnixTimeMilliseconds(),
            });
            nativeSession.Send("rsp.chat.v1", frame);
            ChatMessages.Items.Add($"{Resource("YouPrefix")}: {message.Text}");
            ChatInput.Clear();
        }
        catch (DataFeatureException exception)
        {
            StatusText.Text = exception.Message;
        }
    }

    private async void RevokeScopes_Click(object sender, RoutedEventArgs e)
    {
        if (controlPlane is null || peerAuthorization is null || identity is null || session is null ||
            peerData is null || nativeSession is null) return;
        string[] revoked = ActiveScopes.SelectedItems.Cast<string>().Distinct(StringComparer.Ordinal).ToArray();
        if (revoked.Length == 0) return;
        string[] active = peerData.PermissionGate.Current.ActiveScopes.Values.Select(ScopeName)
            .Except(revoked, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        RevokeScopesButton.IsEnabled = false;
        peerData.RestrictLocalPermissions(active);
        ActiveScopes.ItemsSource = active;
        ApplyScopeControls(active);
        nativeSession.ReleaseAllInput(ulong.MaxValue);
        try
        {
            if (fileTransfers is not null && revoked.Any(scope => scope.StartsWith("TRANSFER_FILE_", StringComparison.Ordinal)))
                await fileTransfers.CancelAllAsync("FILE_PERMISSION_REVOKED", false);
            ConsentSessionResponse updated = await controlPlane.RevokeScopesAsync(peerAuthorization, identity,
                sessionStateVersion, revoked, sessionCancellation?.Token ?? CancellationToken.None);
            peerData.ApplyLocalPermissionRevision(checked((ulong)updated.PermissionRevision), updated.GrantedScopes);
            nativeSession.UpdatePermissions(checked((ulong)updated.PermissionRevision), updated.GrantedScopes, revoked);
            sessionStateVersion = updated.StateVersion;
            ActiveScopes.ItemsSource = updated.GrantedScopes;
            ApplyScopeControls(updated.GrantedScopes);
            UpdateSessionIndicator();
            peerAuthorization = await controlPlane.AuthorizePeerAsync(session.SessionId, session.HostBootstrapToken,
                identity, sessionCancellation?.Token ?? CancellationToken.None);
            StatusText.Text = Resource("StatusScopesRevoked");
            RevokeScopesButton.IsEnabled = updated.GrantedScopes.Count != 0;
            if (updated.State == "TERMINATED") Disconnect();
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException or ControlPlaneClientException or
                                          InvalidOperationException or OperationCanceledException)
        {
            StatusText.Text = exception.Message;
            Disconnect();
            StartButton.IsEnabled = true;
        }
    }

    private void ApplyScopeControls(IReadOnlyCollection<string> scopes)
    {
        SendChatButton.IsEnabled = scopes.Contains("CHAT", StringComparer.Ordinal);
        SendClipboardButton.IsEnabled = scopes.Contains("SYNC_CLIPBOARD_TEXT_HOST_TO_OPERATOR", StringComparer.Ordinal);
        SendFileButton.IsEnabled = scopes.Contains("TRANSFER_FILE_HOST_TO_OPERATOR", StringComparer.Ordinal);
        CancelTransfersButton.IsEnabled = scopes.Any(scope => scope.StartsWith("TRANSFER_FILE_", StringComparison.Ordinal));
    }

    private static string ScopeName(RemoteSupport.Domain.CapabilityScope scope) => scope switch
    {
        RemoteSupport.Domain.CapabilityScope.ViewScreen => "VIEW_SCREEN",
        RemoteSupport.Domain.CapabilityScope.ControlPointer => "CONTROL_POINTER",
        RemoteSupport.Domain.CapabilityScope.ControlKeyboard => "CONTROL_KEYBOARD",
        RemoteSupport.Domain.CapabilityScope.SyncClipboardTextHostToOperator => "SYNC_CLIPBOARD_TEXT_HOST_TO_OPERATOR",
        RemoteSupport.Domain.CapabilityScope.SyncClipboardTextOperatorToHost => "SYNC_CLIPBOARD_TEXT_OPERATOR_TO_HOST",
        RemoteSupport.Domain.CapabilityScope.TransferFileHostToOperator => "TRANSFER_FILE_HOST_TO_OPERATOR",
        RemoteSupport.Domain.CapabilityScope.TransferFileOperatorToHost => "TRANSFER_FILE_OPERATOR_TO_HOST",
        RemoteSupport.Domain.CapabilityScope.Chat => "CHAT",
        RemoteSupport.Domain.CapabilityScope.SwitchMonitor => "SWITCH_MONITOR",
        RemoteSupport.Domain.CapabilityScope.RequestReboot => "REQUEST_REBOOT",
        RemoteSupport.Domain.CapabilityScope.ReconnectAfterReboot => "RECONNECT_AFTER_REBOOT",
        RemoteSupport.Domain.CapabilityScope.UnattendedSession => "UNATTENDED_SESSION",
        _ => throw new ArgumentOutOfRangeException(nameof(scope)),
    };

    private void UpdateSessionIndicator()
    {
        if (connectedAt is null || peerData is null) { SessionIndicatorText.Text = string.Empty; return; }
        string elapsed = (DateTimeOffset.UtcNow - connectedAt.Value).ToString(@"hh\:mm\:ss", System.Globalization.CultureInfo.CurrentCulture);
        string scopes = string.Join(", ", peerData.PermissionGate.Current.ActiveScopes.Values.Select(ScopeName));
        SessionIndicatorText.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture,
            System.Text.CompositeFormat.Parse(Resource("SessionIndicatorFormat")), remoteDisplayName, elapsed, scopes);
    }

    private Task HandleSignalingAsync(SignalingMessage message)
    {
        if (nativeSession is null) return Task.CompletedTask;
        switch (message.Type)
        {
            case "SDP_OFFER":
                nativeSession.ApplyRemoteDescription(new NativeSessionDescription(true,
                    message.Payload.GetProperty("sdp").GetString() ?? string.Empty));
                break;
            case "ICE_CANDIDATE":
                nativeSession.AddRemoteIce(new NativeIceCandidate(
                    message.Payload.GetProperty("candidate").GetString() ?? string.Empty,
                    message.Payload.TryGetProperty("sdpMid", out System.Text.Json.JsonElement mid) && mid.ValueKind != System.Text.Json.JsonValueKind.Null ? mid.GetString() : null,
                    message.Payload.TryGetProperty("sdpMLineIndex", out System.Text.Json.JsonElement line) && line.ValueKind != System.Text.Json.JsonValueKind.Null ? line.GetInt32() : 0));
                break;
            case "SESSION_END":
                Dispatcher.BeginInvoke(() =>
                {
                    Disconnect();
                    StatusText.Text = Resource("StatusPeerEnded");
                    StartButton.IsEnabled = true;
                });
                break;
        }
        return Task.CompletedTask;
    }

    private async Task RelayDescriptionAsync(NativeSessionDescription description)
    {
        try
        {
            if (signaling is not null) await signaling.SendDescriptionAsync(description.Offer ? "SDP_OFFER" : "SDP_ANSWER", description.Sdp);
        }
        catch (Exception exception) when (exception is WebSocketException or ControlPlaneClientException)
        {
            await Dispatcher.BeginInvoke(() => StatusText.Text = exception.Message);
        }
    }

    private async Task RelayCandidateAsync(NativeIceCandidate candidate)
    {
        try
        {
            if (signaling is not null) await signaling.SendCandidateAsync(candidate.Candidate, candidate.SdpMid, candidate.SdpMLineIndex);
        }
        catch (Exception exception) when (exception is WebSocketException or ControlPlaneClientException)
        {
            await Dispatcher.BeginInvoke(() => StatusText.Text = exception.Message);
        }
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try { if (session is not null) Clipboard.SetText(session.SupportCode); }
        catch (System.Runtime.InteropServices.COMException) { StatusText.Text = Resource("StatusClipboardLocked"); }
    }

    private void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        Disconnect();
        StatusText.Text = Resource("StatusDisconnected");
        StartButton.IsEnabled = true;
    }

    private void Disconnect()
    {
        if (signaling is not null)
        {
            try { signaling.SendSessionEndAsync("LOCAL_USER_DISCONNECT").GetAwaiter().GetResult(); }
            catch (Exception exception) when (exception is WebSocketException or InvalidOperationException) { }
        }
        if (fileTransfers is not null)
        {
            fileTransfers.DisposeAsync().AsTask().GetAwaiter().GetResult();
            fileTransfers = null;
        }
        dataAudit?.Dispose();
        dataAudit = null;
        clipboardController = null;
        nativeSession?.Dispose();
        nativeSession = null;
        peerData = null;
        chat = null;
        if (signaling is not null)
        {
            signaling.DisposeAsync().AsTask().GetAwaiter().GetResult();
            signaling = null;
        }
        if (controlPlane is not null && peerAuthorization is not null && identity is not null)
        {
            try { controlPlane.TerminateAsync(peerAuthorization, identity, "LOCAL_USER_DISCONNECT").WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult(); }
            catch (Exception exception) when (exception is HttpRequestException or ControlPlaneClientException or TimeoutException) { }
        }
        peerAuthorization = null;
        sessionCancellation?.Cancel();
        sessionCancellation?.Dispose();
        sessionCancellation = null;
        identity?.Dispose();
        identity = null;
        http?.Dispose();
        http = null;
        controlPlane = null;
        session = null;
        SupportCodeText.Text = "—";
        ExpiryText.Text = string.Empty;
        CopyButton.IsEnabled = false;
        DisconnectButton.IsEnabled = false;
        ActiveScopes.ItemsSource = null;
        RevokeScopesButton.IsEnabled = false;
        sessionStateVersion = 0;
        indicatorTimer.Stop();
        connectedAt = null;
        remoteDisplayName = string.Empty;
        SessionIndicatorText.Text = string.Empty;
        ChatMessages.Items.Clear();
        SendChatButton.IsEnabled = false;
        SendClipboardButton.IsEnabled = false;
        SendFileButton.IsEnabled = false;
        CancelTransfersButton.IsEnabled = false;
    }

    private static string Resource(string key) => System.Windows.Application.Current.TryFindResource(key) as string ?? key;

    public void Dispose()
    {
        if (disposed) return;
        Disconnect();
        emergencyHotkey?.Dispose();
        emergencyHotkey = null;
        crashRecovery.Dispose();
        disposed = true;
        GC.SuppressFinalize(this);
    }
}
