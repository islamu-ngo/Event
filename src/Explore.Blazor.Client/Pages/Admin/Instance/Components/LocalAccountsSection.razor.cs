
using System.ComponentModel.DataAnnotations;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.ControlPlane;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Services.ControlPlane;
using Microsoft.AspNetCore.Components;

namespace Explore.Blazor.Client.Pages.Admin.Instance.Components;

public partial class LocalAccountsSection : IDisposable
{
    [Inject] private LocalIdentityAdministrationService Administration { get; set; } = default!;
    [Inject] private ITranslationService Translations { get; set; } = default!;
    [Inject] private IAccessibilityFocusService FocusService { get; set; } = default!;

    private enum FormMode { None = 1, Create = 2, Reset = 3 }
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _actionCancellation;
    private int _generation;
    private bool _disposed;
    private bool _loading = true;
    private bool _identitiesStale = true;
    private bool _busy;
    private bool _validationAttempted;
    private bool _operationIdInvalid;
    private readonly Dictionary<Guid, Guid?> _pendingOperations = [];
    private bool HasPendingOperations => _pendingOperations.Count != 0;
    private bool HasPendingReset(Guid subjectId) => _pendingOperations.ContainsValue(subjectId);
    private FormMode _mode = FormMode.None;
    private HalResourceOfControlPlaneOverviewDto? _capabilities;
    private HalCollectionResourceOfLocalIdentitySummary? _identities;
    private HalResourceOfLocalIdentitySummary? _selectedIdentity;
    private HalResourceOfLocalCredentialOperationStatus? _operation;
    private string _email = string.Empty;
    private string _firstName = string.Empty;
    private string _lastName = string.Empty;
    private string _reason = string.Empty;
    private string _operationInput = string.Empty;
    private string? _temporaryPassword;
    private string? _errorMessage;
    private string? _notice;
    private string? _focusId;

    private string T(string key, string fallback) => Translations.T($"instance.localAccounts.{key}", fallback);
    private string CredentialStateLabel(LocalCredentialState? state) => state switch
    {
        LocalCredentialState.ProvisioningPending => T("state.pending", "Provisioning pending"),
        LocalCredentialState.ChangeRequired => T("state.changeRequired", "Password replacement required"),
        LocalCredentialState.Ready => T("state.ready", "Ready"),
        _ => T("state.unavailable", "Credential status unavailable")
    };
    private string OperationStageLabel(LocalCredentialOperationStage stage) => stage switch
    {
        LocalCredentialOperationStage.ProvisioningPending => T("stage.pending", "Provisioning pending"),
        LocalCredentialOperationStage.ChangeRequired => T("stage.changeRequired", "Password replacement required"),
        LocalCredentialOperationStage.Replaced => T("stage.replaced", "Password replaced"),
        LocalCredentialOperationStage.Superseded => T("stage.superseded", "Superseded"),
        LocalCredentialOperationStage.Abandoned => T("stage.abandoned", "Abandoned"),
        _ => T("stage.unavailable", "Operation stage unavailable")
    };
    private bool IsAvailable => ControlPlaneHal.HasLink(_capabilities?._links, ControlPlaneLinkRelations.LocalIdentities);
    private bool CanCreate => ControlPlaneHal.HasLink(_identities?._links, ControlPlaneLinkRelations.CreateLocalIdentity);
    private IEnumerable<HalResourceOfLocalIdentitySummary> Identities => _identities?._embedded?.Items ?? [];
    private bool EmailInvalid => string.IsNullOrWhiteSpace(_email) || _email.Length > 256 || !new EmailAddressAttribute().IsValid(_email);
    private bool FirstNameInvalid => string.IsNullOrWhiteSpace(_firstName) || _firstName.Length > 200;
    private bool LastNameInvalid => _lastName.Length > 200;
    private bool ReasonInvalid => string.IsNullOrWhiteSpace(_reason) || _reason.Length > 1000;
    private bool CanReconcile => _operation?.Receipt is { OperationId: var id } && id != Guid.Empty
        && ControlPlaneHal.HasLinkForResource(_operation._links, ControlPlaneLinkRelations.Reconcile, id);
    private static bool CanReset(HalResourceOfLocalIdentitySummary identity) => identity.LocalSubjectId != Guid.Empty
        && ControlPlaneHal.HasLinkForResource(identity._links, ControlPlaneLinkRelations.IssueTemporaryCredential, identity.LocalSubjectId);

    protected override async Task OnInitializedAsync()
    {
        Translations.OnLanguageChanged += OnLanguageChanged;
        await LoadAsync(pageNumber: 1);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_disposed && _focusId is { } focusId)
        {
            _focusId = null;
            await FocusService.FocusByIdAsync(focusId);
        }
    }

    private async Task LoadAsync(int pageNumber)
    {
        CancelInteraction();
        int generation = BeginRequest();
        CancellationToken cancellationToken = _actionCancellation!.Token;
        _loading = true;
        try
        {
            var capabilities = await Administration.GetCapabilitiesAsync(cancellationToken);
            if (!IsCurrent(generation)) return;
            _capabilities = capabilities;
            _identities = null;
            if (!IsAvailable) return;
            var identities = await Administration.GetIdentitiesAsync(pageNumber: pageNumber, pageSize: 20,
                cancellationToken: cancellationToken);
            if (!IsCurrent(generation)) return;
            _identities = identities;
            _identitiesStale = false;
        }
        catch (Exception) when (!IsCurrent(generation)) { }
        catch (Exception)
        {
            _capabilities = null;
            _identities = null;
            _errorMessage = T("error.load", "Local account availability could not be checked.");
        }
        finally
        {
            if (IsCurrent(generation)) { _loading = false; _busy = false; }
        }
    }

    private void OpenCreate()
    {
        if (_disposed || !IsAvailable || !CanCreate || _busy || HasPendingOperations) return;
        CancelInteraction();
        _mode = FormMode.Create;
        _focusId = "local-account-email";
    }

    private void OpenReset(HalResourceOfLocalIdentitySummary identity)
    {
        if (_disposed || !IsAvailable || !CanReset(identity) || _busy || _identitiesStale
            || HasPendingReset(identity.LocalSubjectId)) return;
        CancelInteraction();
        _selectedIdentity = identity;
        _mode = FormMode.Reset;
        _focusId = "local-reset-reason";
    }

    private async Task SubmitCreateAsync()
    {
        if (_disposed || _busy || _mode != FormMode.Create || !IsAvailable || !CanCreate || _identities is null) return;
        _validationAttempted = true;
        if (EmailInvalid || FirstNameInvalid || LastNameInvalid) return;
        var request = new CreateLocalIdentityRequestDto
        {
            OperationId = Guid.CreateVersion7(),
            Email = _email,
            FirstName = _firstName,
            LastName = _lastName
        };
        int generation = BeginIssuance(operationId: request.OperationId, localSubjectId: null);
        try
        {
            var result = await Administration.CreateAsync(request: request, identities: _identities,
                cancellationToken: _actionCancellation!.Token);
            if (!IsCurrent(generation)) return;
            AcceptIssue(result);
            await RefreshIdentitiesAsync(generation);
        }
        catch (Exception) when (!IsCurrent(generation)) { }
        catch (Exception exception) { HandleIssuanceFailure(exception, request.OperationId); }
        finally { if (IsCurrent(generation)) _busy = false; }
    }

    private async Task SubmitResetAsync()
    {
        if (_disposed || _busy || _identitiesStale || _mode != FormMode.Reset || !IsAvailable || _selectedIdentity is not { } identity
            || !CanReset(identity)) return;
        _validationAttempted = true;
        if (ReasonInvalid) return;
        if (identity.CurrentOperationId is not { } previousOperationId || previousOperationId == Guid.Empty
            || identity.CurrentOperationConcurrencyStamp is not { } previousStamp || previousStamp == Guid.Empty)
        {
            _errorMessage = T("error.changed", "The account changed. Reload its current status before trying again.");
            return;
        }
        var request = new ResetLocalCredentialRequestDto
        {
            OperationId = Guid.CreateVersion7(),
            ExpectedCurrentOperationId = previousOperationId,
            ExpectedCurrentOperationConcurrencyStamp = previousStamp,
            Reason = _reason
        };
        int generation = BeginIssuance(operationId: request.OperationId, localSubjectId: identity.LocalSubjectId);
        try
        {
            var result = await Administration.ResetAsync(identity: identity, request: request,
                cancellationToken: _actionCancellation!.Token);
            if (!IsCurrent(generation)) return;
            AcceptIssue(result);
            await RefreshIdentitiesAsync(generation);
        }
        catch (Exception) when (!IsCurrent(generation)) { }
        catch (Exception exception) { HandleIssuanceFailure(exception, request.OperationId); }
        finally { if (IsCurrent(generation)) _busy = false; }
    }

    private int BeginIssuance(Guid operationId, Guid? localSubjectId)
    {
        int generation = BeginRequest();
        _operationInput = operationId.ToString("D");
        _pendingOperations.Add(operationId, localSubjectId);
        _operation = null;
        return generation;
    }

    private void AcceptIssue(HalResourceOfLocalCredentialIssueDto result)
    {
        ClearForm();
        ResolveOperation(result.Operation!.Receipt);
        _temporaryPassword = result.Outcome == LocalCredentialIssueOutcome.Issued ? result.TemporaryPassword : null;
        _notice = _temporaryPassword is null
            ? T("replayed", "This operation already completed. Its temporary password cannot be shown again. Check its status before an explicit new reset.")
            : null;
        _focusId = _temporaryPassword is null ? "local-operation-id" : "local-handover-heading";
    }

    private void HandleIssuanceFailure(Exception exception, Guid operationId)
    {
        ClearForm();
        // Validation, authentication and rate limiting reject before mutation. In contrast,
        // creation can return 403/404/409 after committing Identity and attempting reconciliation.
        if (exception is ApiException { StatusCode: 400 or 401 or 429 })
            _pendingOperations.Remove(operationId);
        _errorMessage = SafeError(exception);
    }

    private void ResolveOperation(LocalCredentialOperationReceipt receipt)
    {
        Guid operationId = receipt.OperationId;
        if (receipt.Stage is LocalCredentialOperationStage.ChangeRequired or LocalCredentialOperationStage.Replaced
            or LocalCredentialOperationStage.Superseded or LocalCredentialOperationStage.Abandoned)
            _pendingOperations.Remove(operationId);
        else if (_pendingOperations.ContainsKey(operationId))
            _pendingOperations[operationId] = receipt.LocalSubjectId;
    }

    private async Task RefreshIdentitiesAsync(int generation)
    {
        // Failure or dismissal must not make the pre-operation predecessor metadata actionable.
        _identitiesStale = true;
        try
        {
            var identities = await Administration.GetIdentitiesAsync(pageNumber: _identities?.PageNumber ?? 1,
                pageSize: _identities?.PageSize ?? 20, cancellationToken: _actionCancellation!.Token);
            if (!IsCurrent(generation)) return;
            _identities = identities;
            _identitiesStale = false;
        }
        catch (Exception) when (!IsCurrent(generation)) { }
        catch (Exception)
        {
            _errorMessage = T("error.refresh", "The operation was confirmed, but account details could not be refreshed. Check the operation again before another reset.");
        }
    }

    private async Task CheckOperationAsync()
    {
        if (_disposed || _busy || !IsAvailable) return;
        _operationIdInvalid = !Guid.TryParse(_operationInput, out Guid operationId) || operationId == Guid.Empty;
        if (_operationIdInvalid) return;
        CancelInteraction();
        int generation = BeginRequest();
        _operation = null;
        try
        {
            var operation = await Administration.GetOperationAsync(operationId: operationId,
                cancellationToken: _actionCancellation!.Token);
            if (!IsCurrent(generation)) return;
            _operation = operation;
            ResolveOperation(operation.Receipt);
            await RefreshIdentitiesAsync(generation);
        }
        catch (Exception) when (!IsCurrent(generation)) { }
        catch (Exception exception) { _errorMessage = SafeError(exception); }
        finally { if (IsCurrent(generation)) _busy = false; }
    }

    private async Task ReconcileAsync()
    {
        if (_disposed || _busy || !IsAvailable || !CanReconcile || _operation is not { } current) return;
        int generation = BeginRequest();
        try
        {
            var operation = await Administration.ReconcileAsync(operation: current,
                cancellationToken: _actionCancellation!.Token);
            if (!IsCurrent(generation)) return;
            _operation = operation;
            ResolveOperation(operation.Receipt);
            _notice = T("reconciled", "Operation status refreshed. Reconciliation does not disclose a temporary password.");
            await RefreshIdentitiesAsync(generation);
        }
        catch (Exception) when (!IsCurrent(generation)) { }
        catch (Exception exception) { _errorMessage = SafeError(exception); }
        finally { if (IsCurrent(generation)) _busy = false; }
    }

    private string SafeError(Exception exception) => exception switch
    {
        ApiException { StatusCode: 409 } => T("error.conflict", "The credential operation changed. Check its status before an explicit new reset."),
        ApiException { StatusCode: 401 or 403 } => T("error.authority", "Local account administration is no longer available. Refresh your session."),
        ApiException { StatusCode: 400 } => T("error.validation", "The request could not be accepted. Check the entered values."),
        ApiException { StatusCode: 404 } => T("error.missing", "The requested Local account or operation is unavailable."),
        ApiException { StatusCode: 429 } => T("error.rate", "Too many requests. Wait before checking the operation again."),
        _ => T("error.uncertain", "The operation could not be confirmed. Keep its operation ID and check its status; do not repeat issuance.")
    };

    private int BeginRequest()
    {
        InvalidateRequest();
        _actionCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _temporaryPassword = null;
        _errorMessage = null;
        _notice = null;
        _operationIdInvalid = false;
        _busy = true;
        return _generation;
    }

    private bool IsCurrent(int generation) => !_disposed && generation == _generation
        && _actionCancellation?.IsCancellationRequested == false;

    private void InvalidateRequest()
    {
        ++_generation;
        _actionCancellation?.Cancel();
        _actionCancellation?.Dispose();
        _actionCancellation = null;
    }

    private void ClearForm()
    {
        _mode = FormMode.None;
        _selectedIdentity = null;
        _email = _firstName = _lastName = _reason = string.Empty;
        _validationAttempted = false;
    }

    private void CancelInteraction()
    {
        InvalidateRequest();
        ClearForm();
        _temporaryPassword = null;
        _errorMessage = null;
        _notice = null;
        _busy = false;
        _operationIdInvalid = false;
        _focusId = "local-accounts-heading";
    }

    private void DismissCredential()
    {
        CancelInteraction();
        _focusId = "local-accounts-heading";
    }

    private void OnLanguageChanged(string languageCode)
    {
        if (!_disposed) _ = InvokeAsync(StateHasChanged);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Translations.OnLanguageChanged -= OnLanguageChanged;
        CancelInteraction();
        _operation = null;
        _identities = null;
        _capabilities = null;
        _operationInput = string.Empty;
        _pendingOperations.Clear();
        _lifetime.Cancel();
        _lifetime.Dispose();
        GC.SuppressFinalize(this);
    }
}
