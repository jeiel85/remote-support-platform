using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RemoteSupport.ManagedHost.Service;

/// <summary>Runs the managed-host command-channel cycle continuously with bounded backoff
/// on failure, matching the "reconnect uses exponential backoff with jitter and a 60-second
/// ceiling" behavior in 02-protocol/managed-host-command-channel.md.</summary>
public sealed class ManagedHostBackgroundService(ManagedHostOrchestrator orchestrator,
    ILogger<ManagedHostBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan MaximumBackoff = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan backoff = TimeSpan.FromSeconds(1);
        Random jitter = new();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await orchestrator.RunOnceAsync(stoppingToken).ConfigureAwait(false);
                backoff = TimeSpan.FromSeconds(1);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (exception is DeviceCredentialException or HttpRequestException or IOException)
            {
                Log.CycleFailed(logger, exception);
                TimeSpan delay = backoff + TimeSpan.FromMilliseconds(jitter.Next(0, 500));
                try { await Task.Delay(delay, stoppingToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
                backoff = backoff * 2 > MaximumBackoff ? MaximumBackoff : backoff * 2;
            }
        }
    }
}

internal static partial class Log
{
    [LoggerMessage(EventId = 20, Level = LogLevel.Warning, Message = "Managed-host cycle failed; backing off before retry.")]
    public static partial void CycleFailed(ILogger logger, Exception exception);
}
