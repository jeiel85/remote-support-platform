using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace RemoteSupport.LocalViewer.App;

internal static partial class NativeMethods
{
    private const string Library = "remote_support_native";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void FrameCallback(nint context, nint frame);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void CursorCallback(nint context, nint cursor);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void ErrorCallback(nint context, NativeStatus status, StringView code);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void DisplayCallback(nint context, nint display);

    internal enum NativeStatus : int
    {
        Ok = 0,
        InvalidArgument = 1,
        NotSupported = 4,
    }

    internal enum CaptureSource : int
    {
        Auto = 1,
        Dxgi = 2,
        Wgc = 3,
        Synthetic = 4,
    }

    internal enum CaptureTarget : int
    {
        Display = 1,
        Synthetic = 3,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct StringView
    {
        public nint Data;
        public uint Length;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RuntimeOptions
    {
        public uint StructSize;
        public uint RequestedAbiMajor;
        public uint RequestedAbiMinor;
        public uint Flags;
        public nint UserContext;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Callbacks
    {
        public uint StructSize;
        public nint UserContext;
        public nint OnLog;
        public nint OnCaptureFrame;
        public nint OnEncodedFrame;
        public nint OnRemoteVideoFrame;
        public nint OnError;
        public nint OnTransportState;
        public nint OnLocalDescription;
        public nint OnLocalIceCandidate;
        public nint OnDataChannelState;
        public nint OnDataMessage;
        public nint OnBufferedAmountLow;
        public nint OnCursor;
        public nint OnDecodedFrame;
        public nint OnEncoderFallback;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CaptureOptions
    {
        public uint StructSize;
        public uint TargetFps;
        public uint MaxWidth;
        public uint MaxHeight;
        public uint Flags;
        public StringView DisplayId;
        public CaptureSource Source;
        public CaptureTarget TargetKind;
        public uint FrameQueueCapacity;
        public uint AcquireTimeoutMilliseconds;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RendererOptions
    {
        public uint StructSize;
        public nuint TargetHwnd;
        public int ViewMode;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayInfo
    {
        public uint StructSize;
        public StringView DisplayId;
        public StringView DeviceName;
        public int DesktopX;
        public int DesktopY;
        public uint Width;
        public uint Height;
        public uint RotationDegrees;
        public uint DpiX;
        public uint DpiY;
        public uint AdapterLuidLow;
        public int AdapterLuidHigh;
        public ulong DisplayGeneration;
        public uint Flags;
    }

    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])] internal static partial NativeStatus rs_runtime_create(in RuntimeOptions options, in Callbacks callbacks, out nint runtime);
    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])] internal static partial void rs_runtime_destroy(nint runtime);
    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])] internal static partial NativeStatus rs_runtime_enumerate_displays(nint runtime, nint callback, nint context);
    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])] internal static partial NativeStatus rs_capture_create(nint runtime, in CaptureOptions options, out nint capture);
    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])] internal static partial NativeStatus rs_capture_start(nint capture);
    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])] internal static partial NativeStatus rs_capture_stop(nint capture);
    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])] internal static partial void rs_capture_destroy(nint capture);
    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])] internal static partial NativeStatus rs_renderer_create(nint runtime, in RendererOptions options, out nint renderer);
    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])] internal static partial NativeStatus rs_renderer_submit_d3d11_frame(nint renderer, nint frame);
    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])] internal static partial NativeStatus rs_renderer_submit_cursor(nint renderer, nint cursor);
    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])] internal static partial NativeStatus rs_renderer_resize(nint renderer, uint width, uint height);
    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])] internal static partial void rs_renderer_destroy(nint renderer);
}
