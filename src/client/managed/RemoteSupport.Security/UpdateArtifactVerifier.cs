using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace RemoteSupport.Security;

public sealed class UpdateArtifactException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public interface IAuthenticodeSignatureVerifier
{
    ValueTask<string> VerifyPublisherAsync(string artifactPath, CancellationToken cancellationToken);
}

public sealed class UpdateArtifactVerifier(IAuthenticodeSignatureVerifier? signatureVerifier = null)
{
    private readonly IAuthenticodeSignatureVerifier signatureVerifier =
        signatureVerifier ?? new WindowsAuthenticodeSignatureVerifier();

    public async Task VerifyAsync(VerifiedUpdateManifest manifest, string artifactPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        string path = Path.GetFullPath(artifactPath);
        FileInfo file = new(path);
        if (!file.Exists || file.Length != manifest.Artifact.Size)
            throw Invalid("UPDATE_ARTIFACT_SIZE_INVALID", "Downloaded update size does not match signed metadata.");

        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] actual = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        byte[] expected;
        try { expected = Convert.FromHexString(manifest.Artifact.Sha256); }
        catch (FormatException) { throw Invalid("UPDATE_ARTIFACT_HASH_INVALID", "Signed artifact hash is malformed."); }
        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
            throw Invalid("UPDATE_ARTIFACT_HASH_INVALID", "Downloaded update hash does not match signed metadata.");

        string signer = Normalize(await signatureVerifier.VerifyPublisherAsync(path, cancellationToken).ConfigureAwait(false));
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(signer),
            Convert.FromHexString(Normalize(manifest.Artifact.AuthenticodeSignerThumbprint))))
            throw Invalid("UPDATE_ARTIFACT_SIGNER_INVALID", "Artifact publisher does not match signed metadata.");
    }

    private static string Normalize(string value)
    {
        string normalized = value.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        if (normalized.Length is < 40 or > 128 || normalized.Length % 2 != 0 || !normalized.All(Uri.IsHexDigit))
            throw Invalid("UPDATE_ARTIFACT_SIGNER_INVALID", "Artifact publisher thumbprint is invalid.");
        return normalized;
    }

    private static UpdateArtifactException Invalid(string code, string message) => new(code, message);
}

public sealed class WindowsAuthenticodeSignatureVerifier : IAuthenticodeSignatureVerifier
{
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    public ValueTask<string> VerifyPublisherAsync(string artifactPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Authenticode verification requires Windows.");

        WinTrustFileInfo file = new(artifactPath);
        nint filePointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(file, filePointer, false);
            WinTrustData data = WinTrustData.ForFile(filePointer);
            int result = WinVerifyTrust(0, GenericVerifyV2, ref data);
            data.StateAction = 2;
            _ = WinVerifyTrust(0, GenericVerifyV2, ref data);
            if (result != 0)
                throw new UpdateArtifactException("UPDATE_ARTIFACT_SIGNATURE_INVALID",
                    $"Authenticode trust validation failed with 0x{result:X8}.");

#pragma warning disable SYSLIB0057 // WinTrust validated the PE; this API extracts its embedded signer certificate.
            using X509Certificate signer = X509Certificate.CreateFromSignedFile(artifactPath);
#pragma warning restore SYSLIB0057
            using X509Certificate2 certificate = X509CertificateLoader.LoadCertificate(signer.GetRawCertData());
            return ValueTask.FromResult(certificate.Thumbprint);
        }
        catch (CryptographicException exception)
        {
            throw new UpdateArtifactException("UPDATE_ARTIFACT_SIGNATURE_INVALID",
                $"Authenticode certificate could not be read: {exception.GetType().Name}.");
        }
        finally
        {
            Marshal.DestroyStructure<WinTrustFileInfo>(filePointer);
            Marshal.FreeHGlobal(filePointer);
        }
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int WinVerifyTrust(nint window, [MarshalAs(UnmanagedType.LPStruct)] Guid action,
        ref WinTrustData data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint StructSize;
        [MarshalAs(UnmanagedType.LPWStr)] public string FilePath;
        public nint FileHandle;
        public nint KnownSubject;

        public WinTrustFileInfo(string path)
        {
            StructSize = (uint)Marshal.SizeOf<WinTrustFileInfo>();
            FilePath = path;
            FileHandle = 0;
            KnownSubject = 0;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public uint StructSize;
        public nint PolicyCallbackData;
        public nint SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public nint FileInfo;
        public uint StateAction;
        public nint StateData;
        [MarshalAs(UnmanagedType.LPWStr)] public string? UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
        public nint SignatureSettings;

        public static WinTrustData ForFile(nint fileInfo) => new()
        {
            StructSize = (uint)Marshal.SizeOf<WinTrustData>(),
            UiChoice = 2,
            RevocationChecks = 1,
            UnionChoice = 1,
            FileInfo = fileInfo,
            StateAction = 1,
            ProviderFlags = 0x00000010 | 0x00000080,
        };
    }
}
