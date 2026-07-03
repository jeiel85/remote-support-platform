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
    public IReadOnlyList<string> SelectedScopes => viewModel.SelectedScopes;

    public void Present(VerifiedConsentRequest request)
    {
        viewModel.Present(request);
        VerifiedStateText.Text = System.Windows.Application.Current.TryFindResource("VerifiedTenant") as string ?? "Verified";
        string format = System.Windows.Application.Current.TryFindResource("ExpiresFormat") as string ?? "{0:g}";
        ExpiryStateText.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture, format, request.ExpiresAt.LocalDateTime);
    }

    private void ApproveClicked(object sender, RoutedEventArgs e)
    {
        if (viewModel.Request is { } request && SelectedScopes.Count > 0)
        {
            Approved?.Invoke(this, request);
            DialogResult = true;
        }
    }

    private void DenyClicked(object sender, RoutedEventArgs e)
    {
        if (viewModel.Request is { } request) Denied?.Invoke(this, request);
        DialogResult = false;
    }
}
