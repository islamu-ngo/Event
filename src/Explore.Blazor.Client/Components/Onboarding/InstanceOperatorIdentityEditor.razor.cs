namespace Explore.Blazor.Client.Components.Onboarding;

using Explore.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;

public partial class InstanceOperatorIdentityEditor
{
    [Parameter, EditorRequired]
    public InstanceOperatorIdentityAdminModel Model { get; set; } = null!;

    [Parameter]
    public EventCallback<InstanceOperatorIdentityAdminModel> ModelChanged { get; set; }

    [Parameter]
    public bool IsSingleTenantMode { get; set; }

    [Parameter]
    public bool ShowCopyToDirectoryIdentity { get; set; } = true;

    [Parameter]
    public bool ShowSaveButton { get; set; } = true;

    [Parameter]
    public bool AllowIncompleteDraft { get; set; } = true;

    [Parameter]
    public bool Disabled { get; set; }

    [Parameter]
    public bool ReadOnly { get; set; }

    [Parameter]
    public EventCallback<InstanceOperatorIdentityAdminModel> OnCopyToDirectoryIdentity { get; set; }

    [Parameter]
    public EventCallback<InstanceOperatorIdentityAdminModel> OnSaved { get; set; }

    private bool _saving;
    private string? _statusMessage;
    private string? _errorMessage;
    private InstanceOperatorIdentityAdminModel? _authoritativeConflict;
    private readonly Dictionary<string, string> _validationErrors = new(StringComparer.Ordinal);
    private bool IsReadOnly => Disabled || ReadOnly || _saving || !Model.CanEdit;

    public async Task<bool> SaveDraftAsync()
    {
        if (_saving || !Model.CanEdit)
        {
            return false;
        }

        _validationErrors.Clear();

        if (!AllowIncompleteDraft && !Validate())
        {
            await Focus.FocusAsync($"#{FirstInvalidId()}");
            return false;
        }

        _saving = true;
        _statusMessage = T("operator.saving", "Saving operator identity...");
        _errorMessage = null;
        _authoritativeConflict = null;

        try
        {
            InstanceOperatorIdentitySaveResult result = await IdentityService.SaveAsync(Model);
            if (result.Success && result.Model is not null)
            {
                Model.Apply(result.Model);
                await ModelChanged.InvokeAsync(Model);
                _statusMessage = T("operator.saved", "Operator identity saved.");
                await OnSaved.InvokeAsync(Model);
                return true;
            }

            if (result.IsConcurrencyConflict && result.Model is not null)
            {
                _authoritativeConflict = result.Model;
                if (result.Model.Revision.HasValue)
                {
                    Model.Revision = result.Model.Revision;
                }
                _errorMessage = T("operator.conflictMessage", "Another administrator modified these settings concurrently.");
                await Focus.FocusAsync("#operator-conflict-alert");
                return false;
            }

            _errorMessage = !string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? result.ErrorMessage
                : T("operator.saveFailed", "Failed to save operator identity.");

            if (result.ValidationErrors.Count > 0)
            {
                foreach (var (k, v) in result.ValidationErrors)
                {
                    _validationErrors[k] = v;
                }
            }

            return false;
        }
        finally
        {
            _saving = false;
        }
    }

    private async Task CopyToDirectoryIdentityAsync()
    {
        if (OnCopyToDirectoryIdentity.HasDelegate)
        {
            await OnCopyToDirectoryIdentity.InvokeAsync(Model);
        }
    }

    private bool Validate()
    {
        _validationErrors.Clear();

        if (string.IsNullOrWhiteSpace(Model.PublicName))
            _validationErrors[nameof(Model.PublicName)] = T("operator.err.publicName", "Public name is required.");
        if (string.IsNullOrWhiteSpace(Model.LegalName))
            _validationErrors[nameof(Model.LegalName)] = T("operator.err.legalName", "Legal name is required.");
        if (string.IsNullOrWhiteSpace(Model.OperatorKindCode))
            _validationErrors[nameof(Model.OperatorKindCode)] = T("operator.err.kindCode", "Operator kind code is required.");
        if (string.IsNullOrWhiteSpace(Model.JurisdictionCountryCode))
            _validationErrors[nameof(Model.JurisdictionCountryCode)] = T("operator.err.countryCode", "Jurisdiction country code is required.");
        if (string.IsNullOrWhiteSpace(Model.PublicContactEmail))
            _validationErrors[nameof(Model.PublicContactEmail)] = T("operator.err.email", "Public contact email is required.");
        if (string.IsNullOrWhiteSpace(Model.WebsiteUrl))
            _validationErrors[nameof(Model.WebsiteUrl)] = T("operator.err.website", "Website URL is required.");
        if (string.IsNullOrWhiteSpace(Model.OfficialOrigin))
            _validationErrors[nameof(Model.OfficialOrigin)] = T("operator.err.origin", "Official origin is required.");
        if (string.IsNullOrWhiteSpace(Model.LegalNoticeUrl))
            _validationErrors[nameof(Model.LegalNoticeUrl)] = T("operator.err.legalNotice", "Legal notice URL is required.");
        if (string.IsNullOrWhiteSpace(Model.PrivacyUrl))
            _validationErrors[nameof(Model.PrivacyUrl)] = T("operator.err.privacy", "Privacy URL is required.");

        return _validationErrors.Count == 0;
    }

    private string FirstInvalidId()
    {
        if (_validationErrors.ContainsKey(nameof(Model.PublicName))) return "instance-operator-public-name";
        if (_validationErrors.ContainsKey(nameof(Model.LegalName))) return "instance-operator-legal-name";
        if (_validationErrors.ContainsKey(nameof(Model.OperatorKindCode))) return "instance-operator-kind-code";
        if (_validationErrors.ContainsKey(nameof(Model.JurisdictionCountryCode))) return "instance-operator-jurisdiction-country-code";
        if (_validationErrors.ContainsKey(nameof(Model.PublicContactEmail))) return "instance-operator-public-contact-email";
        if (_validationErrors.ContainsKey(nameof(Model.WebsiteUrl))) return "instance-operator-website-url";
        if (_validationErrors.ContainsKey(nameof(Model.OfficialOrigin))) return "instance-operator-official-origin";
        if (_validationErrors.ContainsKey(nameof(Model.LegalNoticeUrl))) return "instance-operator-legal-notice-url";
        if (_validationErrors.ContainsKey(nameof(Model.PrivacyUrl))) return "instance-operator-privacy-url";
        return "instance-operator-public-name";
    }

    private bool HasError(string field) => _validationErrors.ContainsKey(field);
    private string? ErrorFor(string field) => _validationErrors.GetValueOrDefault(field);
    private string AriaInvalid(string field) => HasError(field) ? "true" : "false";

    private string T(string key, string fallback) =>
        Translation.T(key, fallback);

    private string MessageFor(InstanceOperatorIdentityAdminMessageCode code) => code switch
    {
        InstanceOperatorIdentityAdminMessageCode.NotInitialized =>
            T("operator.notInit", "Operator identity has not been initialized."),
        InstanceOperatorIdentityAdminMessageCode.LoadFailed =>
            T("operator.loadFail", "Could not load operator identity. Refresh and try again."),
        InstanceOperatorIdentityAdminMessageCode.EditUnavailable =>
            T("operator.noEdit", "You do not have permission to edit operator identity."),
        _ => string.Empty
    };
}
