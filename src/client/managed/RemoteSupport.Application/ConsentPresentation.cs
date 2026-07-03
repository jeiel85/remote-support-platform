using System.ComponentModel;

namespace RemoteSupport.Application;

public sealed record VerifiedConsentRequest(
    Guid SessionId,
    Guid ConsentRequestId,
    string OperatorDisplayName,
    string TenantDisplayName,
    bool VerifiedTenant,
    IReadOnlyList<string> RequestedScopes,
    DateTimeOffset ExpiresAt,
    long StateVersion);

public sealed class ConsentViewModel : INotifyPropertyChanged
{
    private VerifiedConsentRequest? request;
    private IReadOnlyList<ConsentScopeChoice> scopeChoices = [];

    public string OperatorDisplayName => request?.OperatorDisplayName ?? "Waiting for an authenticated operator";
    public string TenantDisplayName => request?.TenantDisplayName ?? "";
    public string VerificationText => request is null ? "No active request" :
        request.VerifiedTenant ? "Organization identity verified" : "Organization identity not verified";
    public IReadOnlyList<string> RequestedScopeLabels => request?.RequestedScopes.Select(ScopeLabel).ToArray() ?? [];
    public IReadOnlyList<ConsentScopeChoice> ScopeChoices => scopeChoices;
    public IReadOnlyList<string> SelectedScopes => scopeChoices.Where(choice => choice.IsSelected).Select(choice => choice.Scope).ToArray();
    public string ExpiryText => request is null ? "" : $"Expires {request.ExpiresAt.LocalDateTime:g}";
    public VerifiedConsentRequest? Request => request;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Present(VerifiedConsentRequest value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value.OperatorDisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(value.TenantDisplayName);
        if (!value.VerifiedTenant) throw new ArgumentException("Tenant identity must be verified.", nameof(value));
        if (value.RequestedScopes.Count == 0 || value.RequestedScopes.Count != value.RequestedScopes.Distinct(StringComparer.Ordinal).Count())
            throw new ArgumentException("Consent request scopes must be non-empty and unique.", nameof(value));
        _ = value.RequestedScopes.Select(ScopeLabel).ToArray();
        request = value;
        scopeChoices = value.RequestedScopes.Select(scope => new ConsentScopeChoice(scope, scope == "VIEW_SCREEN")).ToArray();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    private static string ScopeLabel(string scope) => scope switch
    {
        "VIEW_SCREEN" => "View your screen",
        "CONTROL_POINTER" => "Control the pointer",
        "CONTROL_KEYBOARD" => "Use the keyboard",
        "SYNC_CLIPBOARD_TEXT_HOST_TO_OPERATOR" => "Read shared clipboard text",
        "SYNC_CLIPBOARD_TEXT_OPERATOR_TO_HOST" => "Send clipboard text",
        "TRANSFER_FILE_HOST_TO_OPERATOR" => "Receive files from this computer",
        "TRANSFER_FILE_OPERATOR_TO_HOST" => "Send files to this computer",
        "CHAT" => "Exchange chat messages",
        "SWITCH_MONITOR" => "Switch shared monitors",
        "REQUEST_REBOOT" => "Request a reboot",
        "RECONNECT_AFTER_REBOOT" => "Reconnect after reboot",
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown consent scope."),
    };
}

public sealed class ConsentScopeChoice(string scope, bool selected) : INotifyPropertyChanged
{
    private bool isSelected = selected;
    public string Scope { get; } = scope;
    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected == value) return;
            isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}
