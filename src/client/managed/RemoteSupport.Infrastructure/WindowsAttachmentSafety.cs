using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using RemoteSupport.Application;

namespace RemoteSupport.Infrastructure;

public sealed class WindowsAttachmentSafety : IReceivedFileSafety
{
    public ValueTask InspectAsync(string temporaryPath, string normalizedName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) return ValueTask.CompletedTask;
        InspectWindows(temporaryPath, normalizedName);
        return ValueTask.CompletedTask;
    }

    public async ValueTask MarkExternalAsync(string completedPath, Uri source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) return;
        string root = Path.GetPathRoot(Path.GetFullPath(completedPath)) ?? string.Empty;
        string format;
        try { format = new DriveInfo(root).DriveFormat; }
        catch (IOException) { return; }
        if (!string.Equals(format, "NTFS", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(format, "ReFS", StringComparison.OrdinalIgnoreCase)) return;
        string zone = "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=" + source.AbsoluteUri + "\r\n";
        await File.WriteAllTextAsync(completedPath + ":Zone.Identifier", zone, new UTF8Encoding(false), cancellationToken)
            .ConfigureAwait(false);
    }

    [SupportedOSPlatform("windows")]
    private static void InspectWindows(string temporaryPath, string normalizedName)
    {
        IAttachmentExecute? attachment = null;
        try
        {
            attachment = (IAttachmentExecute)(object)new AttachmentServices();
            ThrowIfFailed(attachment.SetClientTitle("Remote Support"));
            Guid client = new("85FB310C-4C7A-49CE-8D3D-4FC090F83892");
            ThrowIfFailed(attachment.SetClientGuid(ref client));
            ThrowIfFailed(attachment.SetLocalPath(temporaryPath));
            ThrowIfFailed(attachment.SetFileName(normalizedName));
            ThrowIfFailed(attachment.SetSource("https://remote-support.invalid/peer-transfer"));
            ThrowIfFailed(attachment.CheckPolicy());
            ThrowIfFailed(attachment.Save());
        }
        catch (COMException exception)
        {
            throw new DataFeatureException("FILE_POLICY_BLOCKED",
                $"Windows attachment inspection rejected the received file ({exception.ErrorCode:X8}).");
        }
        finally
        {
            if (attachment is not null) Marshal.FinalReleaseComObject(attachment);
        }
    }

    private static void ThrowIfFailed(int result)
    {
        if (result < 0) Marshal.ThrowExceptionForHR(result);
    }

    [ComImport]
    [Guid("4125DD96-E03A-4103-8F70-E0597D803B9C")]
    private sealed class AttachmentServices;

    [ComImport]
    [Guid("73DB1241-1E85-4581-8E4F-A81E1D0F8C57")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAttachmentExecute
    {
        [PreserveSig] int SetClientTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
        [PreserveSig] int SetClientGuid(ref Guid guid);
        [PreserveSig] int SetLocalPath([MarshalAs(UnmanagedType.LPWStr)] string path);
        [PreserveSig] int SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int SetSource([MarshalAs(UnmanagedType.LPWStr)] string source);
        [PreserveSig] int SetReferrer([MarshalAs(UnmanagedType.LPWStr)] string referrer);
        [PreserveSig] int CheckPolicy();
        [PreserveSig] int Prompt(nint parent, int prompt, out int action);
        [PreserveSig] int Save();
        [PreserveSig] int Execute(nint parent, [MarshalAs(UnmanagedType.LPWStr)] string verb, out nint process);
        [PreserveSig] int SaveWithUI(nint parent);
        [PreserveSig] int ClearClientState();
    }
}
