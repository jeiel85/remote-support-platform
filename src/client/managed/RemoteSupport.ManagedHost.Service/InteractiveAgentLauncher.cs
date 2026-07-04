using RemoteSupport.Ipc.V1;

namespace RemoteSupport.ManagedHost.Service;

/// <summary>Asks the interactive Agent in the target Windows session to present the
/// managed-session local consent/notification workflow and returns its decision.
/// The Service never renders consent UI itself (01-architecture/windows-process-model.md).</summary>
public interface IInteractiveAgentLauncher
{
    Task<ManagedSessionConsentResult?> RequestConsentAsync(ManagedSessionConsentRequest request,
        TimeSpan timeout, CancellationToken cancellationToken);
}
