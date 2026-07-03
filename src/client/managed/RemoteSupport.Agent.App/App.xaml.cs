namespace RemoteSupport.Agent.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        string culture = System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("ko", StringComparison.OrdinalIgnoreCase)
            ? "ko-KR" : "en-US";
        Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary
        {
            Source = new Uri($"Resources/Strings.{culture}.xaml", UriKind.Relative),
        });
        base.OnStartup(e);
        if (e.Args.Contains("--smoke-test", StringComparer.Ordinal)) _ = ShutdownAfterSmokeTestAsync();
    }

    private async Task ShutdownAfterSmokeTestAsync()
    {
        await Task.Delay(1000).ConfigureAwait(false);
        await Dispatcher.InvokeAsync(Shutdown);
    }
}
