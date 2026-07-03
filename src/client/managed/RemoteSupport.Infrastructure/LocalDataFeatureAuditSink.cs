using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RemoteSupport.Application;

namespace RemoteSupport.Infrastructure;

public sealed class LocalDataFeatureAuditSink : IDataFeatureAuditSink, IDisposable
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly string path;
    private readonly string product;
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool disposed;

    public LocalDataFeatureAuditSink(string product, string? root = null)
    {
        if (product is not ("agent" or "operator")) throw new ArgumentOutOfRangeException(nameof(product));
        this.product = product;
        string auditRoot = Path.GetFullPath(root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RemoteSupport", "audit"));
        Directory.CreateDirectory(auditRoot);
        path = Path.Combine(auditRoot, product + "-data-events.jsonl");
    }

    public async ValueTask WriteAsync(DataFeatureAuditRecord record, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(record);
        byte[] line = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            product,
            record.EventType,
            record.SessionId,
            record.TransferId,
            record.Direction,
            normalizedNameSha256 = record.NormalizedName is null ? null : Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(record.NormalizedName))).ToLowerInvariant(),
            record.Size,
            record.ResultCode,
            record.OccurredAt,
        }, Json);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using FileStream stream = new(path, FileMode.Append, FileAccess.Write, FileShare.Read,
                16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await stream.WriteAsync(line, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    public void Dispose()
    {
        if (disposed) return;
        gate.Dispose();
        disposed = true;
        GC.SuppressFinalize(this);
    }
}
