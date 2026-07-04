using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RemoteSupport.ManagedHost.Service;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "RemoteSupportManagedHost");
builder.Logging.AddEventLog(settings => settings.SourceName = "RemoteSupportManagedHost");

ManagedHostOptions options = builder.Configuration.GetSection("ManagedHost").Get<ManagedHostOptions>()
    ?? throw new InvalidOperationException("ManagedHost configuration section is required.");
options.Validate();

builder.Services.AddHttpClient<DeviceCredentialClient>(client => client.BaseAddress = new Uri(options.ControlPlaneBaseUrl))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(10) });
builder.Services.AddSingleton(_ => CngDeviceIdentityKey.OpenOrCreate(options.DeviceKeyName));
builder.Services.AddSingleton(provider => new ManagedHostDeviceState(options.DeviceId,
    provider.GetRequiredService<CngDeviceIdentityKey>(), options.ActiveKeyVersion, ServiceVersion.Current,
    Environment.OSVersion.VersionString));
builder.Services.AddSingleton<DeviceCredentialClient>(provider =>
    new DeviceCredentialClient(provider.GetRequiredService<HttpClient>(), options.DeviceId));
builder.Services.AddSingleton<IInteractiveAgentLauncher>(provider =>
    new WtsInteractiveAgentLauncher(options.AgentExecutablePath, options.InstallationId,
        provider.GetRequiredService<ILogger<WtsInteractiveAgentLauncher>>()));
builder.Services.AddSingleton(provider => new ManagedHostOrchestrator(
    provider.GetRequiredService<DeviceCredentialClient>(), provider.GetRequiredService<IInteractiveAgentLauncher>(),
    provider.GetRequiredService<ManagedHostDeviceState>(), provider.GetRequiredService<ILogger<ManagedHostOrchestrator>>()));
builder.Services.AddHostedService<ManagedHostBackgroundService>();

IHost host = builder.Build();
await host.RunAsync();

namespace RemoteSupport.ManagedHost.Service
{
    public sealed class ManagedHostOptions
    {
        public string ControlPlaneBaseUrl { get; set; } = string.Empty;
        public Guid DeviceId { get; set; }
        public Guid InstallationId { get; set; }
        public int ActiveKeyVersion { get; set; } = 1;
        public string DeviceKeyName { get; set; } = "RemoteSupportManagedHostDeviceKey";
        public string AgentExecutablePath { get; set; } = string.Empty;

        public void Validate()
        {
            if (!Uri.TryCreate(ControlPlaneBaseUrl, UriKind.Absolute, out Uri? uri) || uri.Scheme != "https")
                throw new InvalidOperationException("ManagedHost:ControlPlaneBaseUrl must be an absolute https URL.");
            if (DeviceId == Guid.Empty) throw new InvalidOperationException("ManagedHost:DeviceId is required.");
            if (InstallationId == Guid.Empty) throw new InvalidOperationException("ManagedHost:InstallationId is required.");
            if (ActiveKeyVersion < 1) throw new InvalidOperationException("ManagedHost:ActiveKeyVersion must be at least 1.");
            if (string.IsNullOrWhiteSpace(AgentExecutablePath))
                throw new InvalidOperationException("ManagedHost:AgentExecutablePath is required.");
        }
    }
}
