using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace RemoteSupport.LocalViewer.App;

public sealed partial class NativeRenderHost : HwndHost
{
    public nint ChildHandle { get; private set; }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        ChildHandle = CreateWindowEx(0, "STATIC", string.Empty, 0x40000000 | 0x10000000, 0, 0, 1, 1, hwndParent.Handle, 0, 0, 0);
        if (ChildHandle == 0)
        {
            throw new InvalidOperationException("Native render child window creation failed.");
        }
        return new HandleRef(this, ChildHandle);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        DestroyWindow(hwnd.Handle);
        ChildHandle = 0;
    }

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint CreateWindowEx(uint extendedStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint hwnd);
}
