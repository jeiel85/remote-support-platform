using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace RemoteSupport.LocalViewer.App;

public partial class MainWindow : Window
{
    private static readonly NativeMethods.FrameCallback FrameHandler = OnFrame;
    private static readonly NativeMethods.CursorCallback CursorHandler = OnCursor;
    private static readonly NativeMethods.ErrorCallback ErrorHandler = OnError;
    private static readonly NativeMethods.DisplayCallback DisplayHandler = OnDisplay;
    private static MainWindow? current;

    private readonly List<DisplayChoice> displays = [];
    private nint runtime;
    private nint renderer;
    private nint capture;

    public MainWindow()
    {
        InitializeComponent();
        current = this;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
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
        StatusText.Text = $"Ready — {displays.Count} display(s)";
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
            StatusText.Text = $"Capturing with {source}";
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
        StatusText.Text = "Stopped";
    }

    private void OnClosed(object? sender, EventArgs args)
    {
        StopCapture();
        if (renderer != 0) NativeMethods.rs_renderer_destroy(renderer);
        if (runtime != 0) NativeMethods.rs_runtime_destroy(runtime);
        current = null;
    }

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
