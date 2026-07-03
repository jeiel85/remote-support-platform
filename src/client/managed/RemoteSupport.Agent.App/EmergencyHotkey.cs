using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace RemoteSupport.Agent.App;

internal sealed partial class EmergencyHotkey : IDisposable
{
    private const int HotkeyId = 0x5253;
    private const int WmHotkey = 0x0312;
    private const uint Modifiers = 0x0001 | 0x0002 | 0x0004 | 0x4000; // Alt+Ctrl+Shift+NoRepeat
    private readonly HwndSource source;
    private bool disposed;

    private EmergencyHotkey(HwndSource source, Action activated, uint virtualKey)
    {
        this.source = source;
        Activated = activated;
        VirtualKey = virtualKey;
        source.AddHook(Hook);
    }

    public Action Activated { get; }
    public uint VirtualKey { get; }

    public static EmergencyHotkey Register(System.Windows.Window window, Action activated)
    {
        nint handle = new WindowInteropHelper(window).Handle;
        HwndSource source = HwndSource.FromHwnd(handle) ?? throw new InvalidOperationException("Agent window source is unavailable.");
        foreach (uint key in new uint[] { 0x7B, 0x7A }) // F12, then F11.
        {
            if (RegisterHotKey(handle, HotkeyId, Modifiers, key)) return new EmergencyHotkey(source, activated, key);
        }
        throw new InvalidOperationException("The mandatory emergency disconnect hotkey could not be registered.");
    }

    public void Dispose()
    {
        if (disposed) return;
        source.RemoveHook(Hook);
        UnregisterHotKey(source.Handle, HotkeyId);
        disposed = true;
        GC.SuppressFinalize(this);
    }

    private nint Hook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == WmHotkey && wParam == HotkeyId)
        {
            handled = true;
            Activated();
        }
        return 0;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint window, int id);
}
