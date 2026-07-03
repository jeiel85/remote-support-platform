using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using RemoteSupport.Application;

namespace RemoteSupport.Infrastructure;

public sealed record NativeDisplay(string Id, string Name, int X, int Y, uint Width, uint Height, ulong Generation);
public sealed record NativeSessionDescription(bool Offer, string Sdp);
public sealed record NativeIceCandidate(string Candidate, string? SdpMid, int SdpMLineIndex);
public sealed record NativeDataPacket(string Label, byte[] Payload);
public sealed record NativeRemoteFrame(uint Width, uint Height, int DesktopX, int DesktopY, ulong DisplayGeneration);

[SupportedOSPlatform("windows")]
public sealed class NativeAttendedSession : IDisposable
{
    private readonly bool host;
    private readonly GCHandle self;
    private readonly Dictionary<uint, string> channelLabels = [];
    private readonly Dictionary<string, uint> channelIds = new(StringComparer.Ordinal);
    private readonly List<NativeDisplay> displays = [];
    private readonly NativeMethods.FrameCallback captureCallback;
    private readonly NativeMethods.FrameCallback remoteVideoCallback;
    private readonly NativeMethods.ErrorCallback errorCallback;
    private readonly NativeMethods.TransportStateCallback stateCallback;
    private readonly NativeMethods.DescriptionCallback descriptionCallback;
    private readonly NativeMethods.IceCallback iceCallback;
    private readonly NativeMethods.ChannelStateCallback channelCallback;
    private readonly NativeMethods.DataCallback dataCallback;
    private readonly NativeMethods.BindingCallback bindingCallback;
    private readonly NativeMethods.DisplayCallback displayCallback;
    private nint runtime;
    private nint transport;
    private nint capture;
    private nint renderer;
    private nint inputInjector;
    private bool disposed;

    public NativeAttendedSession(PeerAuthorizationResponse authorization, EphemeralPeerIdentity identity,
        TurnCredentialsResponse turn, nuint rendererWindow = 0)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(turn);
        host = authorization.Role == "HOST";
        if (!host && authorization.Role != "OPERATOR" || authorization.RemoteRole == authorization.Role ||
            authorization.AuthorizationContextSha256.Length != 43)
            throw new ArgumentException("Peer authorization binding is invalid.", nameof(authorization));
        self = GCHandle.Alloc(this);
        captureCallback = CaptureFrame;
        remoteVideoCallback = RemoteVideoFrame;
        errorCallback = Error;
        stateCallback = TransportState;
        descriptionCallback = LocalDescription;
        iceCallback = LocalIce;
        channelCallback = ChannelState;
        dataCallback = Data;
        bindingCallback = Binding;
        displayCallback = Display;
        NativeMethods.Callbacks callbacks = new()
        {
            StructSize = (uint)Marshal.SizeOf<NativeMethods.Callbacks>(),
            UserContext = GCHandle.ToIntPtr(self),
            OnCaptureFrame = Marshal.GetFunctionPointerForDelegate(captureCallback),
            OnRemoteVideoFrame = Marshal.GetFunctionPointerForDelegate(remoteVideoCallback),
            OnError = Marshal.GetFunctionPointerForDelegate(errorCallback),
            OnTransportState = Marshal.GetFunctionPointerForDelegate(stateCallback),
            OnLocalDescription = Marshal.GetFunctionPointerForDelegate(descriptionCallback),
            OnLocalIceCandidate = Marshal.GetFunctionPointerForDelegate(iceCallback),
            OnDataChannelState = Marshal.GetFunctionPointerForDelegate(channelCallback),
            OnDataMessage = Marshal.GetFunctionPointerForDelegate(dataCallback),
            OnTransportBindingState = Marshal.GetFunctionPointerForDelegate(bindingCallback),
        };
        NativeMethods.RuntimeOptions runtimeOptions = new()
        {
            StructSize = (uint)Marshal.SizeOf<NativeMethods.RuntimeOptions>(),
            RequestedAbiMajor = 1,
            RequestedAbiMinor = 4,
            UserContext = callbacks.UserContext,
        };
        try
        {
            Require(NativeMethods.rs_runtime_create(in runtimeOptions, in callbacks, out runtime), "create runtime");
            if (rendererWindow != 0)
            {
                NativeMethods.RendererOptions options = new()
                {
                    StructSize = (uint)Marshal.SizeOf<NativeMethods.RendererOptions>(),
                    TargetHwnd = rendererWindow,
                };
                Require(NativeMethods.rs_renderer_create(runtime, in options, out renderer), "create renderer");
            }
            CreateTransport(authorization, identity, turn);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public event Action<string>? StateChanged;
    public event Action<string>? SecurityBindingChanged;
    public event Action<NativeSessionDescription>? LocalDescriptionReady;
    public event Action<NativeIceCandidate>? LocalIceCandidateReady;
    public event Action<NativeDataPacket>? DataReceived;
    public event Action<string>? ErrorOccurred;
    public event Action<NativeRemoteFrame>? RemoteFrameChanged;
    public IReadOnlyList<NativeDisplay> Displays => displays;

    public void EnumerateDisplays()
    {
        displays.Clear();
        Require(NativeMethods.rs_runtime_enumerate_displays(runtime, Marshal.GetFunctionPointerForDelegate(displayCallback),
            GCHandle.ToIntPtr(self)), "enumerate displays");
    }

    public void StartHostCapture(string? displayId = null)
    {
        if (!host) throw new InvalidOperationException("Only the host captures video.");
        if (capture != 0) return;
        if (string.IsNullOrEmpty(displayId))
        {
            EnumerateDisplays();
            displayId = displays.FirstOrDefault()?.Id ?? throw new InvalidOperationException("No capturable display is available.");
        }
        using NativeAllocations memory = new();
        NativeMethods.CaptureOptions options = new()
        {
            StructSize = (uint)Marshal.SizeOf<NativeMethods.CaptureOptions>(),
            TargetFps = 30,
            DisplayId = memory.String(displayId),
            Source = 1,
            TargetKind = 1,
            FrameQueueCapacity = 3,
            AcquireTimeoutMilliseconds = 100,
        };
        Require(NativeMethods.rs_capture_create(runtime, in options, out capture), "create capture");
        Require(NativeMethods.rs_capture_start(capture), "start capture");
    }

    public void StartNegotiation()
    {
        if (host) return;
        OpenProductChannels();
        Require(NativeMethods.rs_transport_create_offer(transport, 0), "create offer");
    }

    public void ApplyRemoteDescription(NativeSessionDescription description)
    {
        using NativeAllocations memory = new();
        NativeMethods.SessionDescription native = new()
        {
            StructSize = (uint)Marshal.SizeOf<NativeMethods.SessionDescription>(),
            Type = description.Offer ? 1 : 2,
            Sdp = memory.String(description.Sdp),
        };
        Require(NativeMethods.rs_transport_set_remote_description(transport, in native), "set remote description");
        if (host && description.Offer) Require(NativeMethods.rs_transport_create_answer(transport), "create answer");
    }

    public void AddRemoteIce(NativeIceCandidate candidate)
    {
        using NativeAllocations memory = new();
        NativeMethods.IceCandidate native = new()
        {
            StructSize = (uint)Marshal.SizeOf<NativeMethods.IceCandidate>(),
            Candidate = memory.String(candidate.Candidate),
            SdpMid = memory.String(candidate.SdpMid ?? string.Empty),
            SdpMLineIndex = candidate.SdpMLineIndex,
        };
        Require(NativeMethods.rs_transport_add_remote_ice_candidate(transport, in native), "add ICE candidate");
    }

    public void Send(string channelLabel, ReadOnlySpan<byte> payload)
    {
        if (!channelIds.TryGetValue(channelLabel, out uint channel)) throw new InvalidOperationException("Data channel is not open.");
        if (payload.Length > 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(payload));
        unsafe
        {
            fixed (byte* data = payload)
            {
                NativeMethods.DataMessage message = new()
                {
                    StructSize = (uint)Marshal.SizeOf<NativeMethods.DataMessage>(),
                    ChannelId = channel,
                    Binary = 1,
                    Payload = new NativeMethods.ByteView { Data = (nint)data, Length = (uint)payload.Length },
                };
                NativeMethods.NativeStatus result = NativeMethods.rs_transport_send_data(transport, in message);
                if (result == NativeMethods.NativeStatus.WouldBlock)
                    throw new DataFeatureException("RATE_LIMITED", "Native data channel backpressure is active.");
                Require(result, "send data");
            }
        }
    }

    public void EnableHostInput(ulong displayGeneration, bool allowPointer, bool allowKeyboard)
    {
        if (!host) throw new InvalidOperationException("Only the host injects input.");
        if (inputInjector != 0) NativeMethods.rs_input_injector_destroy(inputInjector);
        NativeMethods.InputOptions options = new()
        {
            StructSize = (uint)Marshal.SizeOf<NativeMethods.InputOptions>(),
            ExpectedDisplayGeneration = displayGeneration,
            Flags = (allowPointer ? 1u : 0u) | (allowKeyboard ? 2u : 0u),
        };
        Require(NativeMethods.rs_input_injector_create(runtime, in options, out inputInjector), "create input injector");
        Require(NativeMethods.rs_input_injector_set_enabled(inputInjector, 1), "enable input injector");
    }

    public void InjectPointer(RemoteSupport.Protocol.V1.PointerEvent input)
    {
        if (inputInjector == 0) throw new InvalidOperationException("Input injector is not enabled.");
        NativeMethods.PointerInput native = new()
        {
            StructSize = (uint)Marshal.SizeOf<NativeMethods.PointerInput>(),
            Kind = input.Kind switch
            {
                RemoteSupport.Protocol.V1.PointerEvent.Types.Kind.Move => 0,
                RemoteSupport.Protocol.V1.PointerEvent.Types.Kind.ButtonDown => 1,
                RemoteSupport.Protocol.V1.PointerEvent.Types.Kind.ButtonUp => 2,
                RemoteSupport.Protocol.V1.PointerEvent.Types.Kind.Wheel => 3,
                RemoteSupport.Protocol.V1.PointerEvent.Types.Kind.HWheel => 4,
                _ => throw new ArgumentOutOfRangeException(nameof(input)),
            },
            X = input.DesktopX,
            Y = input.DesktopY,
            Button = (int)input.Button,
            WheelDelta = input.WheelDelta,
            DisplayGeneration = input.DisplayGeneration,
            Sequence = input.InputSequence,
        };
        Require(NativeMethods.rs_input_inject_pointer(inputInjector, in native), "inject pointer");
    }

    public void InjectKeyboard(RemoteSupport.Protocol.V1.KeyboardEvent input)
    {
        if (inputInjector == 0) throw new InvalidOperationException("Input injector is not enabled.");
        using NativeAllocations memory = new();
        NativeMethods.KeyboardInput native = new()
        {
            StructSize = (uint)Marshal.SizeOf<NativeMethods.KeyboardInput>(),
            Kind = input.Kind switch
            {
                RemoteSupport.Protocol.V1.KeyboardEvent.Types.Kind.KeyDown => 0,
                RemoteSupport.Protocol.V1.KeyboardEvent.Types.Kind.KeyUp => 1,
                RemoteSupport.Protocol.V1.KeyboardEvent.Types.Kind.UnicodeText => 2,
                _ => throw new ArgumentOutOfRangeException(nameof(input)),
            },
            ScanCode = input.ScanCode,
            VirtualKey = input.VirtualKey,
            Extended = input.Extended ? 1u : 0u,
            Repeat = input.Repeat ? 1u : 0u,
            Text = memory.String(input.Text ?? string.Empty),
            Sequence = input.InputSequence,
            KeyboardLayout = input.KeyboardLayoutId,
        };
        Require(NativeMethods.rs_input_inject_keyboard(inputInjector, in native), "inject keyboard");
    }

    public void ReleaseAllInput(ulong sequence)
    {
        if (inputInjector != 0) _ = NativeMethods.rs_input_release_all(inputInjector, sequence);
    }

    public void UpdatePermissions(ulong revision, IReadOnlyCollection<string> activeScopes,
        IReadOnlyCollection<string> revokedScopes, ulong effectiveAtReliableInputSequence = 0,
        string reasonCode = "LOCAL_USER_REVOKED")
    {
        if (!host) throw new InvalidOperationException("Only the host can reduce session permissions.");
        ArgumentNullException.ThrowIfNull(activeScopes);
        ArgumentNullException.ThrowIfNull(revokedScopes);
        using NativeAllocations memory = new();
        NativeMethods.StringView[] active = activeScopes.Order(StringComparer.Ordinal).Select(memory.String).ToArray();
        NativeMethods.StringView[] revoked = revokedScopes.Order(StringComparer.Ordinal).Select(memory.String).ToArray();
        NativeMethods.PermissionUpdate update = new()
        {
            StructSize = (uint)Marshal.SizeOf<NativeMethods.PermissionUpdate>(),
            PermissionRevision = revision,
            ActiveScopes = memory.StructArray(active),
            ActiveScopeCount = (uint)active.Length,
            RevokedScopes = memory.StructArray(revoked),
            RevokedScopeCount = (uint)revoked.Length,
            EffectiveAtReliableInputSequence = effectiveAtReliableInputSequence,
            ReasonCode = memory.String(reasonCode),
        };
        Require(NativeMethods.rs_transport_update_permissions(transport, in update), "update permissions");
    }

    public void Dispose()
    {
        if (disposed) return;
        if (capture != 0)
        {
            NativeMethods.rs_capture_stop(capture);
            NativeMethods.rs_capture_destroy(capture);
            capture = 0;
        }
        if (transport != 0)
        {
            NativeMethods.rs_transport_close(transport);
            NativeMethods.rs_transport_destroy(transport);
            transport = 0;
        }
        if (inputInjector != 0) { NativeMethods.rs_input_release_all(inputInjector, ulong.MaxValue); NativeMethods.rs_input_injector_destroy(inputInjector); inputInjector = 0; }
        if (renderer != 0) NativeMethods.rs_renderer_destroy(renderer);
        if (runtime != 0) NativeMethods.rs_runtime_destroy(runtime);
        if (self.IsAllocated) self.Free();
        disposed = true;
        GC.SuppressFinalize(this);
    }

    private void CreateTransport(PeerAuthorizationResponse authorization, EphemeralPeerIdentity identity,
        TurnCredentialsResponse turn)
    {
        NativePeerKeyMaterial local = identity.ExportNativeKeyMaterial();
        byte[] remote = PublicKey(authorization.RemoteEphemeralPublicKey);
        byte[] context = EphemeralPeerIdentity.Base64UrlDecode(authorization.AuthorizationContextSha256);
        using NativeAllocations memory = new();
        NativeMethods.StringView[] scopes = authorization.GrantedScopes.Order(StringComparer.Ordinal).Select(memory.String).ToArray();
        nint scopePointer = memory.StructArray(scopes);
        NativeMethods.TransportBinding binding = new()
        {
            StructSize = (uint)Marshal.SizeOf<NativeMethods.TransportBinding>(),
            RemotePeerId = memory.String(authorization.RemotePeerId.ToString("D")),
            LocalRole = host ? 1 : 2,
            RemoteRole = host ? 2 : 1,
            PermissionRevision = checked((ulong)authorization.PermissionRevision),
            GrantedScopes = scopePointer,
            GrantedScopeCount = (uint)scopes.Length,
            AuthorizationContext = memory.Bytes(context),
            LocalPrivateKey = memory.Bytes(local.PrivateKey),
            LocalPublicKey = memory.Bytes(local.PublicKey),
            RemotePublicKey = memory.Bytes(remote),
            LocalKeyId = memory.String(identity.KeyThumbprint),
            RemoteKeyId = memory.String(authorization.RemoteKeyThumbprint),
        };
        nint bindingPointer = memory.Struct(binding);
        NativeMethods.IceServer[] servers = turn.IceServers.Select(server =>
        {
            NativeMethods.StringView[] urls = server.Urls.Select(memory.String).ToArray();
            return new NativeMethods.IceServer
            {
                StructSize = (uint)Marshal.SizeOf<NativeMethods.IceServer>(),
                Urls = memory.StructArray(urls),
                UrlCount = (uint)urls.Length,
                Username = memory.String(server.Username),
                Credential = memory.String(server.Credential),
            };
        }).ToArray();
        NativeMethods.TransportOptions options = new()
        {
            StructSize = (uint)Marshal.SizeOf<NativeMethods.TransportOptions>(),
            SessionId = memory.String(authorization.SessionId.ToString("D")),
            LocalPeerId = memory.String(authorization.PeerId.ToString("D")),
            TransportEpoch = checked((ulong)authorization.TransportEpoch),
            VideoInputMode = 2,
            IceServers = memory.StructArray(servers),
            IceServerCount = (uint)servers.Length,
            MaxDataMessageBytes = 256 * 1024,
            BufferedAmountLowThresholdBytes = 128 * 1024,
            Binding = bindingPointer,
        };
        Require(NativeMethods.rs_transport_create(runtime, in options, out transport), "create transport");
        CryptographicOperations.ZeroMemory(local.PrivateKey);
    }

    private void OpenProductChannels()
    {
        Open("rsp.input.fast.v1", false, 0);
        Open("rsp.input.reliable.v1", true, -1);
        Open("rsp.clipboard.v1", true, -1);
        Open("rsp.file.control.v1", true, -1);
        Open("rsp.file.data.v1", true, -1);
        Open("rsp.chat.v1", true, -1);
    }

    private void Open(string label, bool ordered, int retransmits)
    {
        using NativeAllocations memory = new();
        NativeMethods.DataChannelOptions options = new()
        {
            StructSize = (uint)Marshal.SizeOf<NativeMethods.DataChannelOptions>(),
            Label = memory.String(label),
            Ordered = ordered ? 1u : 0u,
            MaxRetransmits = retransmits,
            MaxPacketLifetimeMilliseconds = -1,
            NegotiatedId = -1,
        };
        Require(NativeMethods.rs_transport_open_data_channel(transport, in options, out _), "open data channel");
    }

    private void CaptureFrame(nint context, nint frame)
    {
        try { if (transport != 0) _ = NativeMethods.rs_transport_submit_d3d11_video(transport, frame); }
        catch (Exception exception) { ErrorOccurred?.Invoke(exception.GetType().Name); }
    }
    private void RemoteVideoFrame(nint context, nint frame)
    {
        try
        {
            NativeMethods.FrameInfo info = Marshal.PtrToStructure<NativeMethods.FrameInfo>(frame);
            RemoteFrameChanged?.Invoke(new NativeRemoteFrame(info.Width, info.Height, info.DesktopX, info.DesktopY, info.DisplayGeneration));
            if (renderer != 0) _ = NativeMethods.rs_renderer_submit_d3d11_frame(renderer, frame);
        }
        catch (Exception exception) { ErrorOccurred?.Invoke(exception.GetType().Name); }
    }
    private void Error(nint context, NativeMethods.NativeStatus status, NativeMethods.StringView code) =>
        ErrorOccurred?.Invoke(NativeMethods.Text(code) ?? status.ToString());
    private void TransportState(nint context, int state, NativeMethods.StringView reason) =>
        StateChanged?.Invoke(NativeMethods.Text(reason) ?? state.ToString(System.Globalization.CultureInfo.InvariantCulture));
    private void Binding(nint context, int state, NativeMethods.StringView reason) =>
        SecurityBindingChanged?.Invoke(NativeMethods.Text(reason) ?? state.ToString(System.Globalization.CultureInfo.InvariantCulture));
    private void LocalDescription(nint context, nint value)
    {
        NativeMethods.SessionDescription description = Marshal.PtrToStructure<NativeMethods.SessionDescription>(value);
        LocalDescriptionReady?.Invoke(new NativeSessionDescription(description.Type == 1, NativeMethods.Text(description.Sdp) ?? string.Empty));
    }
    private void LocalIce(nint context, nint value)
    {
        NativeMethods.IceCandidate candidate = Marshal.PtrToStructure<NativeMethods.IceCandidate>(value);
        LocalIceCandidateReady?.Invoke(new NativeIceCandidate(NativeMethods.Text(candidate.Candidate) ?? string.Empty,
            NativeMethods.Text(candidate.SdpMid), candidate.SdpMLineIndex));
    }
    private void ChannelState(nint context, uint channel, NativeMethods.StringView label, int state)
    {
        string name = NativeMethods.Text(label) ?? string.Empty;
        if (state == 1)
        {
            channelLabels[channel] = name;
            channelIds[name] = channel;
        }
        else if (state == 3)
        {
            channelLabels.Remove(channel);
            channelIds.Remove(name);
        }
    }
    private void Data(nint context, nint value)
    {
        NativeMethods.DataMessage message = Marshal.PtrToStructure<NativeMethods.DataMessage>(value);
        if (!channelLabels.TryGetValue(message.ChannelId, out string? label) || message.Payload.Length > 1024 * 1024) return;
        byte[] payload = new byte[message.Payload.Length];
        Marshal.Copy(message.Payload.Data, payload, 0, payload.Length);
        DataReceived?.Invoke(new NativeDataPacket(label, payload));
    }
    private void Display(nint context, nint value)
    {
        NativeMethods.DisplayInfo display = Marshal.PtrToStructure<NativeMethods.DisplayInfo>(value);
        displays.Add(new NativeDisplay(NativeMethods.Text(display.Id) ?? string.Empty,
            NativeMethods.Text(display.Name) ?? string.Empty, display.X, display.Y, display.Width, display.Height, display.Generation));
    }

    private static byte[] PublicKey(PeerPublicJwk jwk)
    {
        if (jwk.Kty != "EC" || jwk.Crv != "P-256") throw new ArgumentException("Remote peer JWK is invalid.");
        byte[] x = EphemeralPeerIdentity.Base64UrlDecode(jwk.X);
        byte[] y = EphemeralPeerIdentity.Base64UrlDecode(jwk.Y);
        if (x.Length != 32 || y.Length != 32) throw new ArgumentException("Remote peer JWK is invalid.");
        byte[] result = new byte[65];
        result[0] = 4;
        x.CopyTo(result, 1);
        y.CopyTo(result, 33);
        return result;
    }

    private static void Require(NativeMethods.NativeStatus status, string operation)
    {
        if (status != NativeMethods.NativeStatus.Ok) throw new InvalidOperationException($"Native {operation} failed with {status}.");
    }

    private sealed class NativeAllocations : IDisposable
    {
        private readonly List<nint> allocations = [];
        public NativeMethods.StringView String(string value)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value);
            nint pointer = Marshal.AllocHGlobal(bytes.Length + 1);
            allocations.Add(pointer);
            Marshal.Copy(bytes, 0, pointer, bytes.Length);
            Marshal.WriteByte(pointer, bytes.Length, 0);
            return new NativeMethods.StringView { Data = pointer, Length = (uint)bytes.Length };
        }
        public NativeMethods.ByteView Bytes(byte[] value)
        {
            nint pointer = Marshal.AllocHGlobal(value.Length);
            allocations.Add(pointer);
            Marshal.Copy(value, 0, pointer, value.Length);
            return new NativeMethods.ByteView { Data = pointer, Length = (uint)value.Length };
        }
        public nint Struct<T>(T value) where T : struct
        {
            nint pointer = Marshal.AllocHGlobal(Marshal.SizeOf<T>());
            allocations.Add(pointer);
            Marshal.StructureToPtr(value, pointer, false);
            return pointer;
        }
        public nint StructArray<T>(T[] values) where T : struct
        {
            if (values.Length == 0) return 0;
            int size = Marshal.SizeOf<T>();
            nint pointer = Marshal.AllocHGlobal(checked(size * values.Length));
            allocations.Add(pointer);
            for (int index = 0; index < values.Length; index++) Marshal.StructureToPtr(values[index], pointer + index * size, false);
            return pointer;
        }
        public void Dispose()
        {
            foreach (nint pointer in allocations) Marshal.FreeHGlobal(pointer);
            allocations.Clear();
        }
    }
}

internal static partial class NativeMethods
{
    private const string Library = "remote_support_native";
    internal enum NativeStatus { Ok = 0, InvalidArgument = 1, NotSupported = 4, WouldBlock = 12 }
    [StructLayout(LayoutKind.Sequential)] internal struct StringView { public nint Data; public uint Length; }
    [StructLayout(LayoutKind.Sequential)] internal struct ByteView { public nint Data; public uint Length; }
    [StructLayout(LayoutKind.Sequential)] internal struct RuntimeOptions { public uint StructSize, RequestedAbiMajor, RequestedAbiMinor, Flags; public nint UserContext; }
    [StructLayout(LayoutKind.Sequential)] internal struct Callbacks
    {
        public uint StructSize; public nint UserContext, OnLog, OnCaptureFrame, OnEncodedFrame, OnRemoteVideoFrame, OnError,
            OnTransportState, OnLocalDescription, OnLocalIceCandidate, OnDataChannelState, OnDataMessage, OnBufferedAmountLow,
            OnCursor, OnDecodedFrame, OnEncoderFallback, OnTransportBindingState, OnTransportVideoFeedback;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct RendererOptions { public uint StructSize; public nuint TargetHwnd; public int ViewMode; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] internal struct CaptureOptions
    { public uint StructSize, TargetFps, MaxWidth, MaxHeight, Flags; public StringView DisplayId; public int Source, TargetKind; public uint FrameQueueCapacity, AcquireTimeoutMilliseconds; }
    [StructLayout(LayoutKind.Sequential)] internal struct DisplayInfo
    { public uint StructSize; public StringView Id, Name; public int X, Y; public uint Width, Height, Rotation, DpiX, DpiY, AdapterLow; public int AdapterHigh; public ulong Generation; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] internal struct IceServer
    { public uint StructSize; public nint Urls; public uint UrlCount; public StringView Username, Credential; }
    [StructLayout(LayoutKind.Sequential)] internal struct TransportBinding
    { public uint StructSize; public StringView RemotePeerId; public int LocalRole, RemoteRole; public ulong PermissionRevision; public nint GrantedScopes; public uint GrantedScopeCount; public ByteView AuthorizationContext, LocalPrivateKey, LocalPublicKey, RemotePublicKey; public StringView LocalKeyId, RemoteKeyId; }
    [StructLayout(LayoutKind.Sequential)] internal struct TransportOptions
    { public uint StructSize; public StringView SessionId, LocalPeerId; public ulong TransportEpoch; public int VideoInputMode; public nint IceServers; public uint IceServerCount, MaxDataMessageBytes, BufferedAmountLowThresholdBytes, Flags; public nint Binding; }
    [StructLayout(LayoutKind.Sequential)] internal struct SessionDescription { public uint StructSize; public int Type; public StringView Sdp; }
    [StructLayout(LayoutKind.Sequential)] internal struct IceCandidate { public uint StructSize; public StringView SdpMid; public int SdpMLineIndex; public StringView Candidate; }
    [StructLayout(LayoutKind.Sequential)] internal struct DataChannelOptions
    { public uint StructSize; public StringView Label; public uint Ordered; public int MaxRetransmits, MaxPacketLifetimeMilliseconds; public uint Negotiated; public int NegotiatedId; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] internal struct DataMessage { public uint StructSize, ChannelId, Binary; public ByteView Payload; }
    [StructLayout(LayoutKind.Sequential)] internal struct PermissionUpdate
    { public uint StructSize; public ulong PermissionRevision; public nint ActiveScopes; public uint ActiveScopeCount; public nint RevokedScopes; public uint RevokedScopeCount; public ulong EffectiveAtReliableInputSequence; public StringView ReasonCode; }
    [StructLayout(LayoutKind.Sequential)] internal struct FrameInfo
    { public uint StructSize; public ulong FrameId, Timestamp; public uint Width, Height, Rotation; public int PixelFormat; public ulong DisplayGeneration; public uint Primaries, Transfer, Matrix; public nint Texture; public int DesktopX, DesktopY; }
    [StructLayout(LayoutKind.Sequential)] internal struct InputOptions { public uint StructSize; public ulong ExpectedDisplayGeneration; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] internal struct PointerInput
    { public uint StructSize; public int Kind, X, Y, Button, WheelDelta; public ulong DisplayGeneration, Sequence; }
    [StructLayout(LayoutKind.Sequential)] internal struct KeyboardInput
    { public uint StructSize; public int Kind; public uint ScanCode, VirtualKey, Extended, Repeat; public StringView Text; public ulong Sequence; public uint KeyboardLayout; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void FrameCallback(nint context, nint frame);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void ErrorCallback(nint context, NativeStatus status, StringView code);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void TransportStateCallback(nint context, int state, StringView reason);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void DescriptionCallback(nint context, nint description);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void IceCallback(nint context, nint candidate);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void ChannelStateCallback(nint context, uint channel, StringView label, int state);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void DataCallback(nint context, nint message);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void BindingCallback(nint context, int state, StringView reason);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void DisplayCallback(nint context, nint display);
    internal static string? Text(StringView value) => value.Data == 0 ? null : Marshal.PtrToStringUTF8(value.Data, checked((int)value.Length));
    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])] internal static partial NativeStatus rs_runtime_create(in RuntimeOptions options, in Callbacks callbacks, out nint runtime);
    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])] internal static partial void rs_runtime_destroy(nint runtime);
    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])] internal static partial NativeStatus rs_runtime_enumerate_displays(nint runtime, nint callback, nint context);
    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])] internal static partial NativeStatus rs_renderer_create(nint runtime, in RendererOptions options, out nint renderer);
    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])] internal static partial NativeStatus rs_renderer_submit_d3d11_frame(nint renderer, nint frame);
    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])] internal static partial void rs_renderer_destroy(nint renderer);
    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])] internal static partial NativeStatus rs_capture_create(nint runtime, in CaptureOptions options, out nint capture);
    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])] internal static partial NativeStatus rs_capture_start(nint capture);
    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])] internal static partial NativeStatus rs_capture_stop(nint capture);
    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])] internal static partial void rs_capture_destroy(nint capture);
    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])] internal static partial NativeStatus rs_transport_create(nint runtime, in TransportOptions options, out nint transport);
    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])] internal static partial NativeStatus rs_transport_create_offer(nint transport, uint restart);
    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])] internal static partial NativeStatus rs_transport_create_answer(nint transport);
    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])] internal static partial NativeStatus rs_transport_set_remote_description(nint transport, in SessionDescription description);
    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])] internal static partial NativeStatus rs_transport_add_remote_ice_candidate(nint transport, in IceCandidate candidate);
    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])] internal static partial NativeStatus rs_transport_submit_d3d11_video(nint transport, nint frame);
    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])] internal static partial NativeStatus rs_transport_open_data_channel(nint transport, in DataChannelOptions options, out uint channel);
    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])] internal static partial NativeStatus rs_transport_send_data(nint transport, in DataMessage message);
    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])] internal static partial NativeStatus rs_transport_update_permissions(nint transport, in PermissionUpdate update);
    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])] internal static partial NativeStatus rs_transport_close(nint transport);
    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])] internal static partial void rs_transport_destroy(nint transport);
    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])] internal static partial NativeStatus rs_input_injector_create(nint runtime, in InputOptions options, out nint injector);
    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])] internal static partial NativeStatus rs_input_injector_set_enabled(nint injector, uint enabled);
    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])] internal static partial NativeStatus rs_input_inject_pointer(nint injector, in PointerInput input);
    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])] internal static partial NativeStatus rs_input_inject_keyboard(nint injector, in KeyboardInput input);
    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])] internal static partial NativeStatus rs_input_release_all(nint injector, ulong sequence);
    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])] internal static partial void rs_input_injector_destroy(nint injector);
}
