using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace RemoteSupport.Ipc;

/// <summary>
/// Pipe ACL and OS-verified caller identity per 01-architecture/windows-process-model.md
/// IPC design: "Pipe ACL allows only LocalSystem, Administrators and the exact logged-on
/// user SID" and "Service re-evaluates authorization; it never trusts the UI process claim alone."
/// </summary>
[SupportedOSPlatform("windows")]
public static class NamedPipeSecurity
{
    public static NamedPipeServerStream CreateSecureServerStream(string pipeName, SecurityIdentifier allowedUserSid,
        int maxInstances = 1)
    {
        PipeSecurity security = new();
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(allowedUserSid,
            PipeAccessRights.ReadWrite, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.InOut, maxInstances,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(SafeHandle pipe, out uint clientProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientSessionId(SafeHandle pipe, out uint clientSessionId);

    /// <summary>Reads the OS-reported caller process ID for an accepted pipe connection.
    /// Callers must compare this against any PID claimed in an IpcEnvelope; the claim is
    /// never trusted on its own.</summary>
    public static uint GetClientProcessId(PipeStream pipe) =>
        GetNamedPipeClientProcessId(pipe.SafePipeHandle, out uint pid) ? pid
            : throw new IOException("Unable to read the named-pipe client process ID.", Marshal.GetLastWin32Error());

    public static uint GetClientSessionId(PipeStream pipe) =>
        GetNamedPipeClientSessionId(pipe.SafePipeHandle, out uint sessionId) ? sessionId
            : throw new IOException("Unable to read the named-pipe client session ID.", Marshal.GetLastWin32Error());
}
