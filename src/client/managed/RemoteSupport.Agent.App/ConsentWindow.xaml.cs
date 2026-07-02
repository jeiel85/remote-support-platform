using System.Windows;
using RemoteSupport.Application;

namespace RemoteSupport.Agent.App;

public partial class ConsentWindow : Window
{
    private readonly ConsentViewModel viewModel = new();

    public ConsentWindow()
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    public event EventHandler<VerifiedConsentRequest>? Approved;
    public event EventHandler<VerifiedConsentRequest>? Denied;

    public void Present(VerifiedConsentRequest request) => viewModel.Present(request);

    private void ApproveClicked(object sender, RoutedEventArgs e)
    {
        if (viewModel.Request is { } request) Approved?.Invoke(this, request);
    }

    private void DenyClicked(object sender, RoutedEventArgs e)
    {
        if (viewModel.Request is { } request) Denied?.Invoke(this, request);
    }
}
