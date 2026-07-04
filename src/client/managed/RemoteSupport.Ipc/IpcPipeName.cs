namespace RemoteSupport.Ipc;

/// <summary>
/// Named-pipe path convention per 01-architecture/windows-process-model.md:
/// "Named pipe path includes installation ID and protocol version."
/// </summary>
public static class IpcPipeName
{
    public static string Build(Guid installationId, uint protocolMajor) =>
        $"RemoteSupport.Ipc.{installationId:N}.v{protocolMajor}";
}
