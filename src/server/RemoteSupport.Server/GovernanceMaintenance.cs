using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RemoteSupport.Server;

internal sealed class GovernanceExportStore
{
    private readonly string directory;

    public GovernanceExportStore(GovernanceOptions options, IWebHostEnvironment environment)
    {
        directory = Path.GetFullPath(options.ExportDirectory, environment.ContentRootPath);
        Directory.CreateDirectory(directory);
    }

    public string Write(Guid tenantId, Guid requestId, string format, byte[] content)
    {
        string extension = format == "JSONL" ? ".jsonl" : ".zip";
        string key = $"{tenantId:D}/{requestId:D}{extension}";
        string path = Resolve(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        File.WriteAllBytes(temporary, content);
        File.Move(temporary, path, true);
        return key;
    }

    public byte[] Read(string key) => File.ReadAllBytes(Resolve(key));

    public void Delete(string key)
    {
        string path = Resolve(key);
        if (File.Exists(path)) File.Delete(path);
    }

    private string Resolve(string key)
    {
        string path = Path.GetFullPath(Path.Combine(directory, key.Replace('/', Path.DirectorySeparatorChar)));
        string root = directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Export object key escaped the export directory.");
        return path;
    }
}

internal sealed class GovernanceMaintenanceService(IGovernanceStore store, GovernanceExportStore exportStore,
    ISystemClock clock, ILogger<GovernanceMaintenanceService> logger) : BackgroundService
{
    private static readonly Action<ILogger, Exception?> MaintenanceFailed =
        LoggerMessage.Define(LogLevel.Error, new EventId(1001, "GovernanceMaintenanceFailed"),
            "Governance maintenance cycle failed.");
    private static readonly JsonSerializerOptions ExportJsonOptions = new() { WriteIndented = true };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromMinutes(5));
        do
        {
            try { RunOnce(); }
            catch (Exception exception) { MaintenanceFailed(logger, exception); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal void RunOnce()
    {
        DateTimeOffset now = clock.UtcNow;
        foreach (Guid tenantId in store.ListTenantIdsForMaintenance())
        {
            store.Execute(tenantId, tenant =>
            {
                ExpireInvitations(tenant, now);
                ProcessExports(tenant, now);
                ProcessClosure(tenant, now);
                ApplyRetention(tenant, now);
                return true;
            });
        }
    }

    private void ProcessExports(TenantAggregate tenant, DateTimeOffset now)
    {
        foreach (DataExportRecord record in tenant.DataExports.Values.ToArray())
        {
            if (record.State == "QUEUED")
            {
                try
                {
                    byte[] content = BuildExport(tenant, record.Format, now);
                    string objectKey = exportStore.Write(tenant.Tenant.Id, record.Id, record.Format, content);
                    string hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
                    tenant.DataExports[record.Id] = record with
                    {
                        State = "READY",
                        CompletedAt = now,
                        DownloadExpiresAt = now.AddHours(24),
                        ObjectKey = objectKey,
                        ObjectSha256 = hash,
                    };
                    GovernanceAudit.Append(tenant, SystemActor(), "PRIVACY", "DATA_EXPORT_COMPLETED", "SUCCEEDED",
                        "DATA_EXPORT", record.Id.ToString("D"), new { sha256 = hash, expiresAt = now.AddHours(24) }, now);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
                {
                    tenant.DataExports[record.Id] = record with
                    {
                        State = "FAILED",
                        CompletedAt = now,
                        FailureCode = "EXPORT_STORAGE_FAILED",
                    };
                    GovernanceAudit.Append(tenant, SystemActor(), "PRIVACY", "DATA_EXPORT_COMPLETED", "FAILED",
                        "DATA_EXPORT", record.Id.ToString("D"), new { errorCode = "EXPORT_STORAGE_FAILED" }, now);
                }
            }
            else if (record.State == "READY" && record.DownloadExpiresAt <= now)
            {
                if (record.ObjectKey is not null) exportStore.Delete(record.ObjectKey);
                tenant.DataExports[record.Id] = record with { State = "EXPIRED" };
                GovernanceAudit.Append(tenant, SystemActor(), "PRIVACY", "DATA_EXPORT_EXPIRED", "SUCCEEDED",
                    "DATA_EXPORT", record.Id.ToString("D"), new { }, now);
            }
        }
    }

    private static void ProcessClosure(TenantAggregate tenant, DateTimeOffset now)
    {
        foreach (ClosureRecord record in tenant.ClosureRequests.Values.Where(value =>
                     value.State == "COOLING_OFF" && value.EffectiveAt <= now).ToArray())
        {
            tenant.Tenant = tenant.Tenant with
            {
                Status = "CLOSED",
                AuthorizationVersion = tenant.Tenant.AuthorizationVersion + 1,
                UpdatedAt = now,
                ClosedAt = now,
            };
            foreach ((Guid id, MembershipRecord member) in tenant.Memberships.ToArray())
                tenant.Memberships[id] = member with
                {
                    Status = "REMOVED",
                    PrivilegeVersion = member.PrivilegeVersion + 1,
                    UpdatedAt = now,
                    RemovedAt = now,
                };
            foreach ((Guid id, DeviceRecord device) in tenant.Devices.ToArray())
                tenant.Devices[id] = device with
                {
                    Status = "REVOKED",
                    AuthorizationVersion = device.AuthorizationVersion + 1,
                    UpdatedAt = now,
                    RevokedAt = now,
                };
            tenant.ClosureRequests[record.Id] = record with
            {
                State = "COMPLETED",
                CompletedAt = now,
                StateVersion = record.StateVersion + 1,
            };
            GovernanceAudit.Append(tenant, SystemActor(), "PRIVACY", "TENANT_CLOSURE_COMPLETED", "SUCCEEDED",
                "TENANT", tenant.Tenant.Id.ToString("D"), new
                {
                    membershipsRevoked = tenant.Memberships.Count,
                    devicesRevoked = tenant.Devices.Count
                }, now);
        }
    }

    private static void ExpireInvitations(TenantAggregate tenant, DateTimeOffset now)
    {
        foreach ((Guid id, InvitationRecord invitation) in tenant.Invitations.ToArray())
            if (invitation.Status == "PENDING" && invitation.ExpiresAt <= now)
                tenant.Invitations[id] = invitation with { Status = "EXPIRED" };
    }

    private static void ApplyRetention(TenantAggregate tenant, DateTimeOffset now)
    {
        AuditVerificationContract verification = GovernanceAudit.Verify(tenant, now);
        if (!verification.Valid) return;
        DateTimeOffset cutoff = now.AddDays(-tenant.Settings.RetentionDays);
        GovernanceAuditRecord[] purged = tenant.AuditEvents.TakeWhile(value => value.OccurredAt < cutoff).ToArray();
        if (purged.Length == 0) return;
        GovernanceAuditRecord checkpoint = purged[^1];
        tenant.AuditEvents.RemoveRange(0, purged.Length);
        tenant.AuditCheckpoint = new AuditCheckpointRecord(checkpoint.Sequence, checkpoint.EventHash, now);
    }

    private static byte[] BuildExport(TenantAggregate tenant, string format, DateTimeOffset generatedAt)
    {
        object snapshot = new
        {
            generatedAt,
            tenant = new
            {
                tenant.Tenant.Id,
                tenant.Tenant.Name,
                tenant.Tenant.Slug,
                tenant.Tenant.Status,
                tenant.Tenant.PlanCode,
                tenant.Tenant.DataRegion,
                tenant.Tenant.CreatedAt
            },
            settings = new
            {
                tenant.Settings.Version,
                tenant.Settings.RetentionDays,
                tenant.Settings.AllowedFeatures,
                tenant.Settings.FileSizeLimitBytes,
                recordingEnabled = false
            },
            memberships = tenant.Memberships.Values.Select(value => new
            {
                value.UserId,
                value.DisplayName,
                value.Email,
                value.Roles,
                value.Status,
                value.CreatedAt
            }),
            devices = tenant.Devices.Values.Select(value => new
            {
                value.Id,
                value.DisplayName,
                value.Status,
                value.Architecture,
                value.OsVersion,
                value.AppVersion,
                value.EnrolledAt,
                value.LastSeenAt
            }),
            policies = tenant.Policies.Values.Select(value => new
            {
                value.Id,
                value.Name,
                value.Status,
                value.ActiveVersion,
                value.CreatedAt
            }),
            audit = tenant.AuditEvents.Select(GovernanceAudit.ToContract),
        };
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(snapshot, ExportJsonOptions);
        if (format == "JSONL") return json;
        using MemoryStream output = new();
        using (ZipArchive archive = new(output, ZipArchiveMode.Create, true))
        {
            ZipArchiveEntry entry = archive.CreateEntry("tenant-export.json", CompressionLevel.SmallestSize);
            using Stream stream = entry.Open();
            stream.Write(json);
        }
        return output.ToArray();
    }

    private static TenantActor SystemActor() => new(Guid.Empty, "governance-maintenance", "Governance Maintenance",
        null, "SYSTEM", null, [], null);
}
