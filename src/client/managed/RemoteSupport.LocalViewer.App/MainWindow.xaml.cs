using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Net.Http;
using System.Net.WebSockets;
using System.IO;
using System.Windows;
using RemoteSupport.Infrastructure;
using RemoteSupport.Observability;
using RemoteSupport.Application;
using RemoteSupport.Media;
using RemoteSupport.Protocol;
using RemoteSupport.Protocol.V1;
using System.Windows.Input;
using System.Windows.Media;

namespace RemoteSupport.LocalViewer.App;

public partial class MainWindow : Window, IDisposable
{
    private static readonly Regex SupportCodePattern = new("^[0-9A-HJKMNP-TV-Z]{5}-[0-9A-HJKMNP-TV-Z]{5}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly NativeMethods.FrameCallback FrameHandler = OnFrame;
    private static readonly NativeMethods.CursorCallback CursorHandler = OnCursor;
    private static readonly NativeMethods.ErrorCallback ErrorHandler = OnError;
    private static readonly NativeMethods.DisplayCallback DisplayHandler = OnDisplay;
    private static MainWindow? current;

    private readonly List<DisplayChoice> displays = [];
    private nint runtime;
    private nint renderer;
    private nint capture;
    private HttpClient? http;
    private EphemeralPeerIdentity? peerIdentity;
    private OperatorOidcSession? oidcSession;
    private OperatorResolvedSession? resolvedSession;
    private AttendedControlPlaneClient? controlPlane;
    private CancellationTokenSource? sessionCancellation;
    private bool disposed;
    private readonly CrashRecoveryGuard crashRecovery = CrashRecoveryGuard.Start("operator", ProductVersion.Current);
    private NativeAttendedSession? remoteSession;
    private SignalingClient? signaling;
    private PeerDataSession? peerData;
    private PeerChatService? chat;
    private NativeRemoteFrame? remoteFrame;
    private ulong inputSequence;
    private bool pointerAllowed;
    private bool keyboardAllowed;
    private LocalDataFeatureAuditSink? dataAudit;
    private readonly System.Windows.Threading.DispatcherTimer indicatorTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTimeOffset? connectedAt;
    private PeerClipboardController? clipboardController;
    private PeerFileTransferController? fileTransfers;
    private PeerAuthorizationResponse? peerAuthorization;

    public MainWindow()
    {
        InitializeComponent();
        indicatorTimer.Tick += (_, _) => UpdateSessionIndicator();
        current = this;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        try
        {
        NativeMethods.RuntimeOptions options = new()
        {
            StructSize = (uint)Marshal.SizeOf<NativeMethods.RuntimeOptions>(),
            RequestedAbiMajor = 1,
            RequestedAbiMinor = 1,
        };
        NativeMethods.Callbacks callbacks = new()
        {
            StructSize = (uint)Marshal.SizeOf<NativeMethods.Callbacks>(),
            OnCaptureFrame = Marshal.GetFunctionPointerForDelegate(FrameHandler),
            OnError = Marshal.GetFunctionPointerForDelegate(ErrorHandler),
            OnCursor = Marshal.GetFunctionPointerForDelegate(CursorHandler),
        };
        Require(NativeMethods.rs_runtime_create(in options, in callbacks, out runtime), "create runtime");
        NativeMethods.RendererOptions rendererOptions = new()
        {
            StructSize = (uint)Marshal.SizeOf<NativeMethods.RendererOptions>(),
            TargetHwnd = (nuint)RenderHost.ChildHandle,
            ViewMode = 0,
        };
        Require(NativeMethods.rs_renderer_create(runtime, in rendererOptions, out renderer), "create renderer");
        Require(NativeMethods.rs_runtime_enumerate_displays(runtime, Marshal.GetFunctionPointerForDelegate(DisplayHandler), 0), "enumerate displays");
        DisplaySelector.ItemsSource = displays;
        DisplaySelector.SelectedIndex = displays.Count > 0 ? 0 : -1;
        StatusText.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture,
            System.Text.CompositeFormat.Parse(Resource("ReadyDisplaysFormat")), displays.Count);
        }
        catch (Exception exception) when (exception is DllNotFoundException or BadImageFormatException or InvalidOperationException)
        {
            StatusText.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture,
                System.Text.CompositeFormat.Parse(Resource("DiagnosticsUnavailableFormat")), exception.Message);
        }
    }

    private async void SignIn_Click(object sender, RoutedEventArgs args)
    {
        SignInButton.IsEnabled = false;
        try
        {
            ClientConfiguration configuration = await ClientConfiguration.LoadAsync(
                Path.Combine(AppContext.BaseDirectory, "client-config.json"));
            if (configuration.OperatorOidc is null)
                throw new InvalidDataException(Resource("ErrorOidcRequired"));
            http?.Dispose();
            http = new HttpClient { BaseAddress = configuration.ApiBaseUrl, Timeout = TimeSpan.FromSeconds(30) };
            oidcSession = await new OperatorOidcClient(http).SignInAsync(configuration.OperatorOidc);
            controlPlane = new AttendedControlPlaneClient(http);
            IdentityText.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture, System.Text.CompositeFormat.Parse(Resource("SignedInExpiryFormat")), oidcSession.ExpiresAt.LocalDateTime);
            ConnectButton.IsEnabled = true;
            StatusText.Text = Resource("StatusSignedIn");
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException or ControlPlaneClientException)
        {
            StatusText.Text = exception.Message;
            SignInButton.IsEnabled = true;
        }
    }

    private async void Connect_Click(object sender, RoutedEventArgs args)
    {
        if (controlPlane is null || oidcSession is null) return;
        string code = SupportCodeInput.Text.Trim().ToUpperInvariant();
        if (!SupportCodePattern.IsMatch(code))
        {
            StatusText.Text = Resource("StatusCodeInvalid");
            return;
        }
        ConnectButton.IsEnabled = false;
        sessionCancellation = new CancellationTokenSource();
        peerIdentity = new EphemeralPeerIdentity();
        try
        {
            string[] scopes = RequestedScopes();
            resolvedSession = await controlPlane.ResolveAsync(code, scopes, peerIdentity,
                oidcSession.AccessToken, sessionCancellation.Token);
            SessionDetails.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture,
                System.Text.CompositeFormat.Parse(Resource("SessionSummaryFormat")), resolvedSession.SessionId,
                resolvedSession.ExpiresAt.LocalDateTime);
            StatusText.Text = Resource("WaitingConsent");
            DisconnectButton.IsEnabled = true;
            while (true)
            {
                ConsentSessionResponse state = await controlPlane.GetSessionAsync(resolvedSession.SessionId,
                    oidcSession.AccessToken, sessionCancellation.Token);
                if (state.State == "AUTHORIZED") break;
                if (state.State is "REJECTED" or "EXPIRED" or "TERMINATED")
                    throw new ControlPlaneClientException("SESSION_REVOKED", $"Session ended in state {state.State}.", 409);
                await Task.Delay(TimeSpan.FromSeconds(1), sessionCancellation.Token);
            }
            PeerAuthorizationResponse peer = await controlPlane.AuthorizePeerAsync(resolvedSession.SessionId,
                resolvedSession.OperatorBootstrapToken, peerIdentity, sessionCancellation.Token);
            peerAuthorization = peer;
            SessionDetails.Text += $"\nAuthorized scopes: {string.Join(", ", peer.GrantedScopes)}";
            Task<SignalingTicketResponse> ticketTask = controlPlane.GetSignalingTicketAsync(peer, peerIdentity, sessionCancellation.Token);
            Task<TurnCredentialsResponse> turnTask = controlPlane.GetTurnCredentialsAsync(peer, peerIdentity, sessionCancellation.Token);
            await Task.WhenAll(ticketTask, turnTask);
            StopCapture();
            if (renderer != 0) { NativeMethods.rs_renderer_destroy(renderer); renderer = 0; }
            if (runtime != 0) { NativeMethods.rs_runtime_destroy(runtime); runtime = 0; }
            remoteSession = new NativeAttendedSession(peer, peerIdentity, await turnTask, (nuint)RenderHost.ChildHandle);
            peerData = new PeerDataSession(peer);
            chat = new PeerChatService(SessionPeerRole.Operator, peerData.PermissionGate);
            clipboardController = new PeerClipboardController(peerData, remoteSession, 256 * 1024);
            clipboardController.TextReady += text => Dispatcher.BeginInvoke(() =>
            {
                try { Clipboard.SetText(text); } catch (System.Runtime.InteropServices.COMException) { StatusText.Text = Resource("StatusClipboardLocked"); }
            });
            string received = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Remote Support");
            dataAudit = new LocalDataFeatureAuditSink("operator");
            fileTransfers = new PeerFileTransferController(peer.SessionId, peerData, remoteSession, received,
                safety: new WindowsAttachmentSafety(), audit: dataAudit);
            fileTransfers.ApprovalRequested = acceptance => Dispatcher.InvokeAsync(() => MessageBox.Show(this,
                string.Format(System.Globalization.CultureInfo.CurrentCulture, System.Text.CompositeFormat.Parse(Resource("ReceiveFileFormat")), acceptance.NormalizedName, acceptance.DestinationPath), Resource("IncomingFile"),
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes).Task;
            fileTransfers.StatusChanged += status => Dispatcher.BeginInvoke(() => StatusText.Text = status);
            signaling = new SignalingClient();
            remoteSession.LocalDescriptionReady += description => _ = RelayDescriptionAsync(description);
            remoteSession.LocalIceCandidateReady += candidate => _ = RelayCandidateAsync(candidate);
            remoteSession.StateChanged += stateText => Dispatcher.BeginInvoke(() => StatusText.Text = stateText);
            remoteSession.SecurityBindingChanged += binding => Dispatcher.BeginInvoke(() => StatusText.Text = binding);
            remoteSession.ErrorOccurred += codeText => Dispatcher.BeginInvoke(() => StatusText.Text = codeText);
            remoteSession.RemoteFrameChanged += frame => remoteFrame = frame;
            remoteSession.DataReceived += HandleData;
            signaling.MessageReceived += HandleSignalingAsync;
            await signaling.ConnectAsync(await ticketTask, peer, sessionCancellation.Token);
            remoteSession.StartNegotiation();
            StatusText.Text = Resource("StatusNegotiating");
            DiagnosticsText.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture,
                System.Text.CompositeFormat.Parse(Resource("DiagnosticsFormat")), peer.SessionId, peer.PeerId,
                peer.TransportEpoch, peer.PermissionRevision);
            ApplyScopeControls(peer.GrantedScopes);
            connectedAt = DateTimeOffset.UtcNow;
            indicatorTimer.Start();
            UpdateSessionIndicator();
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = Resource("StatusDisconnected");
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException or ControlPlaneClientException)
        {
            StatusText.Text = exception is ControlPlaneClientException control ? $"{control.Code}: {control.Message}" : exception.Message;
            EndProductSession();
            ConnectButton.IsEnabled = oidcSession is not null;
        }
    }

    private void HandleData(NativeDataPacket packet)
    {
        if (peerData is null || remoteSession is null) return;
        try
        {
            Envelope envelope = peerData.Decode(packet.Label, packet.Payload);
            if (envelope.BodyCase == Envelope.BodyOneofCase.PermissionState)
            {
                Dispatcher.BeginInvoke(async () => await ApplyPermissionStateAsync(envelope.PermissionState));
            }
            else if (envelope.BodyCase == Envelope.BodyOneofCase.ChatMessage && chat is not null)
            {
                PeerChatMessage message = new(Guid.Parse(envelope.ChatMessage.ChatMessageId), envelope.ChatMessage.Text,
                    DateTimeOffset.FromUnixTimeMilliseconds(envelope.ChatMessage.SentUtcUnixMs));
                PeerChatReceipt receipt = chat.Receive(message, SessionPeerRole.Host, envelope.PermissionRevision, DateTimeOffset.UtcNow);
                if (receipt.Delivered) Dispatcher.BeginInvoke(() => ChatMessages.Items.Add($"{Resource("HostPrefix")}: {message.Text}"));
                byte[] ack = peerData.Encode(PeerChannel.Chat, value => value.ChatAck = new ChatAck
                {
                    ChatMessageId = receipt.MessageId.ToString("D"),
                    ReceivedUtcUnixMs = receipt.ReceivedAt.ToUnixTimeMilliseconds(),
                });
                remoteSession.Send("rsp.chat.v1", ack);
            }
            else if (envelope.BodyCase is Envelope.BodyOneofCase.ClipboardOffer or Envelope.BodyOneofCase.ClipboardDecision or
                     Envelope.BodyOneofCase.ClipboardText)
            {
                Dispatcher.BeginInvoke(() => clipboardController?.Handle(envelope));
            }
            else if (envelope.BodyCase is Envelope.BodyOneofCase.FileOffer or Envelope.BodyOneofCase.FileDecision or
                     Envelope.BodyOneofCase.FileChunk or Envelope.BodyOneofCase.FileAck or Envelope.BodyOneofCase.FileCancel)
            {
                Dispatcher.BeginInvoke(async () =>
                {
                    try { if (fileTransfers is not null) await fileTransfers.HandleAsync(envelope); }
                    catch (Exception exception) when (exception is IOException or DataFeatureException or InvalidOperationException or OperationCanceledException)
                    { StatusText.Text = exception.Message; }
                });
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
        if (peerData is null || remoteSession is null || chat is null) return;
        try
        {
            PeerChatMessage message = chat.Create(ChatInput.Text, DateTimeOffset.UtcNow);
            byte[] frame = peerData.Encode(PeerChannel.Chat, value => value.ChatMessage = new ChatMessage
            {
                ChatMessageId = message.MessageId.ToString("D"), Text = message.Text,
                SentUtcUnixMs = message.SentAt.ToUnixTimeMilliseconds(),
            });
            remoteSession.Send("rsp.chat.v1", frame);
            ChatMessages.Items.Add($"{Resource("YouPrefix")}: {message.Text}");
            ChatInput.Clear();
        }
        catch (DataFeatureException exception) { StatusText.Text = exception.Message; }
    }

    private void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed || e.RightButton == MouseButtonState.Pressed) SendPointer(PointerEvent.Types.Kind.Move,
            PointerEvent.Types.Button.Unspecified, 0, fast: true, e.GetPosition(RenderHost));
        else SendPointer(PointerEvent.Types.Kind.Move, PointerEvent.Types.Button.Unspecified, 0, fast: true, e.GetPosition(RenderHost));
    }

    private void Viewport_MouseDown(object sender, MouseButtonEventArgs e)
    {
        RenderHost.Focus();
        SendPointer(PointerEvent.Types.Kind.ButtonDown, Button(e.ChangedButton), 0, false, e.GetPosition(RenderHost));
    }

    private void Viewport_MouseUp(object sender, MouseButtonEventArgs e) =>
        SendPointer(PointerEvent.Types.Kind.ButtonUp, Button(e.ChangedButton), 0, false, e.GetPosition(RenderHost));

    private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e) =>
        SendPointer(PointerEvent.Types.Kind.Wheel, PointerEvent.Types.Button.Unspecified, e.Delta, false, e.GetPosition(RenderHost));

    private void SendPointer(PointerEvent.Types.Kind kind, PointerEvent.Types.Button button, int wheel, bool fast, Point point)
    {
        if (peerData is null || remoteSession is null || remoteFrame is null || !pointerAllowed) return;
        PermissionSnapshot permission = peerData.PermissionGate.Current;
        try { peerData.PermissionGate.Demand(RemoteSupport.Domain.CapabilityScope.ControlPointer, permission.Revision, "INPUT_PERMISSION_REVOKED"); }
        catch (DataFeatureException) { return; }
        DpiScale dpi = VisualTreeHelper.GetDpi(RenderHost);
        PhysicalPoint physical = ViewportMapper.LogicalToPhysical(point.X, point.Y, dpi.DpiScaleX, dpi.DpiScaleY);
        if (!ViewportMapper.TryMapToDesktop(physical, Math.Max(1, (int)(RenderHost.ActualWidth * dpi.DpiScaleX)),
            Math.Max(1, (int)(RenderHost.ActualHeight * dpi.DpiScaleY)), checked((int)remoteFrame.Width),
            checked((int)remoteFrame.Height), remoteFrame.DesktopX, remoteFrame.DesktopY,
            new ViewportTransform(ViewportMode.Fit), out DesktopPoint desktop)) return;
        ulong sequence = checked(++inputSequence);
        byte[] frame = peerData.Encode(fast ? PeerChannel.InputFast : PeerChannel.InputReliable, value => value.PointerEvent = new PointerEvent
        {
            Kind = kind, Button = button, DesktopX = desktop.X, DesktopY = desktop.Y, WheelDelta = wheel,
            DisplayGeneration = remoteFrame.DisplayGeneration, InputSequence = sequence,
        });
        try { remoteSession.Send(fast ? "rsp.input.fast.v1" : "rsp.input.reliable.v1", frame); }
        catch (InvalidOperationException) { }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e) => SendKey(e, true);
    private void Window_KeyUp(object sender, KeyEventArgs e) => SendKey(e, false);

    private void SendKey(KeyEventArgs e, bool down)
    {
        if (peerData is null || remoteSession is null || !keyboardAllowed || !RenderHost.IsKeyboardFocusWithin || e.Key == Key.System) return;
        PermissionSnapshot permission = peerData.PermissionGate.Current;
        try { peerData.PermissionGate.Demand(RemoteSupport.Domain.CapabilityScope.ControlKeyboard, permission.Revision, "INPUT_PERMISSION_REVOKED"); }
        catch (DataFeatureException) { return; }
        int virtualKey = KeyInterop.VirtualKeyFromKey(e.Key);
        uint scan = NativeKeyboard.MapVirtualKey((uint)virtualKey, 0);
        ulong sequence = checked(++inputSequence);
        byte[] frame = peerData.Encode(PeerChannel.InputReliable, value => value.KeyboardEvent = new KeyboardEvent
        {
            Kind = down ? KeyboardEvent.Types.Kind.KeyDown : KeyboardEvent.Types.Kind.KeyUp,
            VirtualKey = (uint)virtualKey, ScanCode = scan, Extended = IsExtended(e.Key), Repeat = e.IsRepeat,
            InputSequence = sequence,
        });
        try { remoteSession.Send("rsp.input.reliable.v1", frame); e.Handled = true; }
        catch (InvalidOperationException) { }
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (peerData is null || remoteSession is null) return;
        ulong sequence = inputSequence;
        byte[] frame = peerData.Encode(PeerChannel.InputReliable, value => value.ReleaseAllInput = new ReleaseAllInput
        {
            ThroughInputSequence = sequence, ReasonCode = "OPERATOR_FOCUS_LOST",
        });
        try { remoteSession.Send("rsp.input.reliable.v1", frame); }
        catch (InvalidOperationException) { }
    }

    private static PointerEvent.Types.Button Button(MouseButton button) => button switch
    {
        MouseButton.Left => PointerEvent.Types.Button.Left,
        MouseButton.Right => PointerEvent.Types.Button.Right,
        MouseButton.Middle => PointerEvent.Types.Button.Middle,
        MouseButton.XButton1 => PointerEvent.Types.Button.X1,
        MouseButton.XButton2 => PointerEvent.Types.Button.X2,
        _ => PointerEvent.Types.Button.Unspecified,
    };

    private static bool IsExtended(Key key) => key is Key.RightAlt or Key.RightCtrl or Key.Insert or Key.Delete or
        Key.Home or Key.End or Key.PageUp or Key.PageDown or Key.Left or Key.Right or Key.Up or Key.Down or Key.NumLock or Key.PrintScreen;

    private Task HandleSignalingAsync(SignalingMessage message)
    {
        if (remoteSession is null) return Task.CompletedTask;
        switch (message.Type)
        {
            case "SDP_ANSWER":
                remoteSession.ApplyRemoteDescription(new NativeSessionDescription(false,
                    message.Payload.GetProperty("sdp").GetString() ?? string.Empty));
                break;
            case "ICE_CANDIDATE":
                remoteSession.AddRemoteIce(new NativeIceCandidate(
                    message.Payload.GetProperty("candidate").GetString() ?? string.Empty,
                    message.Payload.TryGetProperty("sdpMid", out System.Text.Json.JsonElement mid) && mid.ValueKind != System.Text.Json.JsonValueKind.Null ? mid.GetString() : null,
                    message.Payload.TryGetProperty("sdpMLineIndex", out System.Text.Json.JsonElement line) && line.ValueKind != System.Text.Json.JsonValueKind.Null ? line.GetInt32() : 0));
                break;
            case "SESSION_END":
                Dispatcher.BeginInvoke(() =>
                {
                    EndProductSession();
                    StatusText.Text = Resource("StatusHostEnded");
                    ConnectButton.IsEnabled = oidcSession is not null;
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

    private string[] RequestedScopes()
    {
        List<string> scopes = ["VIEW_SCREEN"];
        if (PointerScope.IsChecked == true) scopes.Add("CONTROL_POINTER");
        if (KeyboardScope.IsChecked == true) scopes.Add("CONTROL_KEYBOARD");
        if (ClipboardScope.IsChecked == true)
        {
            scopes.Add("SYNC_CLIPBOARD_TEXT_HOST_TO_OPERATOR");
            scopes.Add("SYNC_CLIPBOARD_TEXT_OPERATOR_TO_HOST");
        }
        if (FileScope.IsChecked == true)
        {
            scopes.Add("TRANSFER_FILE_HOST_TO_OPERATOR");
            scopes.Add("TRANSFER_FILE_OPERATOR_TO_HOST");
        }
        if (ChatScope.IsChecked == true) scopes.Add("CHAT");
        return scopes.ToArray();
    }

    private async Task ApplyPermissionStateAsync(PermissionState state)
    {
        string[] active = state.ActiveScopes.Select(ProtocolScopeName).Order(StringComparer.Ordinal).ToArray();
        string[] revoked = state.RevokedScopes.Select(ProtocolScopeName).ToArray();
        ApplyScopeControls(active);
        UpdateSessionIndicator();
        SessionDetails.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture,
            System.Text.CompositeFormat.Parse(Resource("ActiveScopesFormat")), string.Join(", ", active));
        StatusText.Text = Resource("StatusScopesChanged");
        if (fileTransfers is not null && revoked.Any(scope => scope.StartsWith("TRANSFER_FILE_", StringComparison.Ordinal)))
            await fileTransfers.CancelAllAsync("FILE_PERMISSION_REVOKED", false);
        if (controlPlane is not null && resolvedSession is not null && peerIdentity is not null)
        {
            try
            {
                peerAuthorization = await controlPlane.AuthorizePeerAsync(resolvedSession.SessionId,
                    resolvedSession.OperatorBootstrapToken, peerIdentity, sessionCancellation?.Token ?? CancellationToken.None);
            }
            catch (Exception exception) when (exception is IOException or HttpRequestException or ControlPlaneClientException or OperationCanceledException)
            { StatusText.Text = exception.Message; }
        }
    }

    private void ApplyScopeControls(IReadOnlyCollection<string> scopes)
    {
        pointerAllowed = scopes.Contains("CONTROL_POINTER", StringComparer.Ordinal);
        keyboardAllowed = scopes.Contains("CONTROL_KEYBOARD", StringComparer.Ordinal);
        SendChatButton.IsEnabled = scopes.Contains("CHAT", StringComparer.Ordinal);
        SendFileButton.IsEnabled = scopes.Contains("TRANSFER_FILE_OPERATOR_TO_HOST", StringComparer.Ordinal);
        SendClipboardButton.IsEnabled = scopes.Contains("SYNC_CLIPBOARD_TEXT_OPERATOR_TO_HOST", StringComparer.Ordinal);
        CancelTransfersButton.IsEnabled = scopes.Any(scope => scope.StartsWith("TRANSFER_FILE_", StringComparison.Ordinal));
    }

    private static string ProtocolScopeName(RemoteSupport.Protocol.V1.CapabilityScope scope) => scope switch
    {
        RemoteSupport.Protocol.V1.CapabilityScope.ViewScreen => "VIEW_SCREEN",
        RemoteSupport.Protocol.V1.CapabilityScope.ControlPointer => "CONTROL_POINTER",
        RemoteSupport.Protocol.V1.CapabilityScope.ControlKeyboard => "CONTROL_KEYBOARD",
        RemoteSupport.Protocol.V1.CapabilityScope.SyncClipboardTextHostToOperator => "SYNC_CLIPBOARD_TEXT_HOST_TO_OPERATOR",
        RemoteSupport.Protocol.V1.CapabilityScope.SyncClipboardTextOperatorToHost => "SYNC_CLIPBOARD_TEXT_OPERATOR_TO_HOST",
        RemoteSupport.Protocol.V1.CapabilityScope.TransferFileHostToOperator => "TRANSFER_FILE_HOST_TO_OPERATOR",
        RemoteSupport.Protocol.V1.CapabilityScope.TransferFileOperatorToHost => "TRANSFER_FILE_OPERATOR_TO_HOST",
        RemoteSupport.Protocol.V1.CapabilityScope.Chat => "CHAT",
        RemoteSupport.Protocol.V1.CapabilityScope.SwitchMonitor => "SWITCH_MONITOR",
        RemoteSupport.Protocol.V1.CapabilityScope.RequestReboot => "REQUEST_REBOOT",
        RemoteSupport.Protocol.V1.CapabilityScope.ReconnectAfterReboot => "RECONNECT_AFTER_REBOOT",
        RemoteSupport.Protocol.V1.CapabilityScope.UnattendedSession => "UNATTENDED_SESSION",
        _ => throw new DataFeatureException("PERMISSION_STATE_INVALID", "Permission state contained an unknown scope."),
    };

    private void UpdateSessionIndicator()
    {
        if (connectedAt is null || peerData is null) { SessionIndicatorText.Text = string.Empty; return; }
        string elapsed = (DateTimeOffset.UtcNow - connectedAt.Value).ToString(@"hh\:mm\:ss", System.Globalization.CultureInfo.CurrentCulture);
        string scopes = string.Join(", ", peerData.PermissionGate.Current.ActiveScopes.Values);
        SessionIndicatorText.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture,
            System.Text.CompositeFormat.Parse(Resource("SessionIndicatorFormat")), elapsed, scopes);
    }

    private void Disconnect_Click(object sender, RoutedEventArgs args)
    {
        EndProductSession();
        StatusText.Text = Resource("StatusDisconnected");
        ConnectButton.IsEnabled = oidcSession is not null;
    }

    private void Start_Click(object sender, RoutedEventArgs args)
    {
        StopCapture();
        NativeMethods.CaptureSource source = SourceSelector.SelectedIndex switch
        {
            1 => NativeMethods.CaptureSource.Wgc,
            2 => NativeMethods.CaptureSource.Synthetic,
            _ => NativeMethods.CaptureSource.Dxgi,
        };
        string displayId = source == NativeMethods.CaptureSource.Synthetic ? string.Empty : (DisplaySelector.SelectedItem as DisplayChoice)?.Id ?? string.Empty;
        nint encoded = displayId.Length == 0 ? 0 : Marshal.StringToCoTaskMemUTF8(displayId);
        try
        {
            NativeMethods.CaptureOptions options = new()
            {
                StructSize = (uint)Marshal.SizeOf<NativeMethods.CaptureOptions>(),
                TargetFps = 60,
                MaxWidth = source == NativeMethods.CaptureSource.Synthetic ? 1280u : 0u,
                MaxHeight = source == NativeMethods.CaptureSource.Synthetic ? 720u : 0u,
                DisplayId = new NativeMethods.StringView { Data = encoded, Length = (uint)Encoding.UTF8.GetByteCount(displayId) },
                Source = source,
                TargetKind = source == NativeMethods.CaptureSource.Synthetic ? NativeMethods.CaptureTarget.Synthetic : NativeMethods.CaptureTarget.Display,
                FrameQueueCapacity = 3,
                AcquireTimeoutMilliseconds = 100,
            };
            Require(NativeMethods.rs_capture_create(runtime, in options, out capture), "create capture");
            Require(NativeMethods.rs_capture_start(capture), "start capture");
            StatusText.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture,
                System.Text.CompositeFormat.Parse(Resource("CapturingFormat")), source);
        }
        finally
        {
            if (encoded != 0) Marshal.FreeCoTaskMem(encoded);
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs args) => StopCapture();

    private void StopCapture()
    {
        if (capture == 0) return;
        NativeMethods.rs_capture_stop(capture);
        NativeMethods.rs_capture_destroy(capture);
        capture = 0;
        StatusText.Text = Resource("Stopped");
    }

    private void OnClosed(object? sender, EventArgs args)
    {
        Dispose();
    }

    public void Dispose()
    {
        if (disposed) return;
        EndProductSession();
        StopCapture();
        if (renderer != 0) NativeMethods.rs_renderer_destroy(renderer);
        if (runtime != 0) NativeMethods.rs_runtime_destroy(runtime);
        http?.Dispose();
        http = null;
        crashRecovery.Dispose();
        current = null;
        disposed = true;
        GC.SuppressFinalize(this);
    }

    private void EndProductSession()
    {
        if (signaling is not null)
        {
            try { signaling.SendSessionEndAsync("OPERATOR_DISCONNECT").GetAwaiter().GetResult(); }
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
        remoteSession?.Dispose();
        remoteSession = null;
        peerData = null;
        chat = null;
        remoteFrame = null;
        if (signaling is not null)
        {
            signaling.DisposeAsync().AsTask().GetAwaiter().GetResult();
            signaling = null;
        }
        if (controlPlane is not null && peerAuthorization is not null && peerIdentity is not null)
        {
            try { controlPlane.TerminateAsync(peerAuthorization, peerIdentity, "OPERATOR_DISCONNECT").WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult(); }
            catch (Exception exception) when (exception is HttpRequestException or ControlPlaneClientException or TimeoutException) { }
        }
        peerAuthorization = null;
        sessionCancellation?.Cancel();
        sessionCancellation?.Dispose();
        sessionCancellation = null;
        peerIdentity?.Dispose();
        peerIdentity = null;
        resolvedSession = null;
        DisconnectButton.IsEnabled = false;
        SendChatButton.IsEnabled = false;
        SendFileButton.IsEnabled = false;
        SendClipboardButton.IsEnabled = false;
        CancelTransfersButton.IsEnabled = false;
        pointerAllowed = false;
        keyboardAllowed = false;
        indicatorTimer.Stop();
        connectedAt = null;
        SessionIndicatorText.Text = string.Empty;
        SessionDetails.Text = string.Empty;
        DiagnosticsText.Text = Resource("NoRoute");
        ChatMessages.Items.Clear();
    }

    private static string Resource(string key) => System.Windows.Application.Current.TryFindResource(key) as string ?? key;

    private static void OnFrame(nint context, nint frame)
    {
        MainWindow? window = current;
        if (window is not null && window.renderer != 0) NativeMethods.rs_renderer_submit_d3d11_frame(window.renderer, frame);
    }

    private static void OnCursor(nint context, nint cursor)
    {
        MainWindow? window = current;
        if (window is not null && window.renderer != 0) NativeMethods.rs_renderer_submit_cursor(window.renderer, cursor);
    }

    private static void OnError(nint context, NativeMethods.NativeStatus status, NativeMethods.StringView code)
    {
        string stableCode = code.Data == 0 ? status.ToString() : Marshal.PtrToStringUTF8(code.Data, checked((int)code.Length)) ?? status.ToString();
        MainWindow? window = current;
        window?.Dispatcher.BeginInvoke(() => window.StatusText.Text = $"{stableCode} ({status})");
    }

    private static void OnDisplay(nint context, nint displayPointer)
    {
        NativeMethods.DisplayInfo display = Marshal.PtrToStructure<NativeMethods.DisplayInfo>(displayPointer);
        string id = Marshal.PtrToStringUTF8(display.DisplayId.Data, checked((int)display.DisplayId.Length)) ?? string.Empty;
        string name = Marshal.PtrToStringUTF8(display.DeviceName.Data, checked((int)display.DeviceName.Length)) ?? id;
        current?.displays.Add(new DisplayChoice(id, $"{name} — {display.Width}×{display.Height} @ ({display.DesktopX},{display.DesktopY})"));
    }

    private static void Require(NativeMethods.NativeStatus status, string operation)
    {
        if (status != NativeMethods.NativeStatus.Ok) throw new InvalidOperationException($"Native {operation} failed with {status}.");
    }

    private sealed record DisplayChoice(string Id, string Name);
}

internal static partial class NativeKeyboard
{
    [LibraryImport("user32.dll")]
    internal static partial uint MapVirtualKey(uint code, uint mapType);
}
