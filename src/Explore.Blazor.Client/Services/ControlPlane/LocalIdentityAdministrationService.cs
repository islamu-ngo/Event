
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.ControlPlane;
using Explore.Blazor.Client.Contracts.Services.ControlPlane;

namespace Explore.Blazor.Client.Services.ControlPlane;

public sealed class LocalIdentityAdministrationService(
    ILocalIdentityAdministrationClient client,
    IControlPlaneOverviewService overviewService)
{
    // This concrete facade retains dependencies only, never per-call authority or handover state.
    private readonly ILocalIdentityAdministrationClient _client = client;
    private readonly IControlPlaneOverviewService _overviewService = overviewService;

    public Task<HalResourceOfControlPlaneOverviewDto> GetCapabilitiesAsync(
        CancellationToken cancellationToken = default)
        => _overviewService.GetOverviewAsync(cancellationToken: cancellationToken);

    public async Task<HalCollectionResourceOfLocalIdentitySummary> GetIdentitiesAsync(
        int pageNumber = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        if (pageSize is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        }
        if (pageNumber < 1 || ((long)pageNumber - 1) * pageSize > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(pageNumber));
        }
        await RequireDiscoveryAsync(cancellationToken);
        return await _client.ListLocalIdentitiesAsync(
            pageNumber: pageNumber, pageSize: pageSize, cancellationToken: cancellationToken);
    }

    public async Task<HalResourceOfLocalCredentialIssueDto> CreateAsync(
        CreateLocalIdentityRequestDto request,
        HalCollectionResourceOfLocalIdentitySummary identities,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(identities);
        if (request.OperationId == Guid.Empty
            || !ControlPlaneHal.HasLink(identities._links, ControlPlaneLinkRelations.CreateLocalIdentity))
        {
            throw new InvalidOperationException("Local credential issuance is unavailable.");
        }
        var intent = new CreateLocalIdentityRequestDto
        {
            OperationId = request.OperationId,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName
        };
        await RequireDiscoveryAsync(cancellationToken);
        HalResourceOfLocalCredentialIssueDto result = await _client.CreateLocalIdentityAsync(
            body: intent, cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateIssue(result: result, operationId: intent.OperationId, localSubjectId: null);
        return result;
    }

    public async Task<HalResourceOfLocalCredentialIssueDto> ResetAsync(
        HalResourceOfLocalIdentitySummary identity,
        ResetLocalCredentialRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(request);
        Guid subjectId = identity.LocalSubjectId;
        if (subjectId == Guid.Empty || request.OperationId == Guid.Empty
            || request.ExpectedCurrentOperationId == Guid.Empty
            || request.ExpectedCurrentOperationConcurrencyStamp == Guid.Empty
            || request.OperationId == request.ExpectedCurrentOperationId
            || identity.CurrentOperationId != request.ExpectedCurrentOperationId
            || identity.CurrentOperationConcurrencyStamp != request.ExpectedCurrentOperationConcurrencyStamp
            || !ControlPlaneHal.HasLinkForResource(identity._links,
                ControlPlaneLinkRelations.IssueTemporaryCredential, subjectId))
        {
            throw new InvalidOperationException("Local credential issuance is unavailable.");
        }
        var intent = new ResetLocalCredentialRequestDto
        {
            OperationId = request.OperationId,
            ExpectedCurrentOperationId = request.ExpectedCurrentOperationId,
            ExpectedCurrentOperationConcurrencyStamp = request.ExpectedCurrentOperationConcurrencyStamp,
            Reason = request.Reason
        };
        await RequireDiscoveryAsync(cancellationToken);
        HalResourceOfLocalCredentialIssueDto result = await _client.ResetLocalCredentialAsync(
            userId: subjectId, body: intent, cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateIssue(result: result, operationId: intent.OperationId, localSubjectId: subjectId);
        return result;
    }

    public async Task<HalResourceOfLocalCredentialOperationStatus> GetOperationAsync(
        Guid operationId, CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty)
            throw new ArgumentException("An operation identifier is required.", nameof(operationId));
        await RequireDiscoveryAsync(cancellationToken);
        HalResourceOfLocalCredentialOperationStatus result = await _client.GetLocalCredentialOperationAsync(
            operationId: operationId, cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (result.Receipt?.OperationId != operationId)
            throw new InvalidOperationException("Local credential operation is unavailable.");
        return result;
    }

    public async Task<HalResourceOfLocalCredentialOperationStatus> ReconcileAsync(
        HalResourceOfLocalCredentialOperationStatus operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        Guid operationId = operation.Receipt?.OperationId ?? Guid.Empty;
        if (operationId == Guid.Empty || !ControlPlaneHal.HasLinkForResource(
            operation._links, ControlPlaneLinkRelations.Reconcile, operationId))
        {
            throw new InvalidOperationException("Local credential reconciliation is unavailable.");
        }
        await RequireDiscoveryAsync(cancellationToken);
        HalResourceOfLocalCredentialOperationStatus result = await _client.ReconcileLocalCredentialOperationAsync(
            operationId: operationId, cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (result.Receipt?.OperationId != operationId)
            throw new InvalidOperationException("Local credential operation is unavailable.");
        return result;
    }

    private async Task RequireDiscoveryAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        HalResourceOfControlPlaneOverviewDto capabilities = await GetCapabilitiesAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ControlPlaneHal.HasLink(capabilities._links, ControlPlaneLinkRelations.LocalIdentities))
            throw new InvalidOperationException("Local identity administration is unavailable.");
    }

    private static void ValidateIssue(HalResourceOfLocalCredentialIssueDto result, Guid operationId, Guid? localSubjectId)
    {
        if (result.Operation?.Receipt is not { } receipt || receipt.OperationId != operationId
            || receipt.LocalSubjectId == Guid.Empty
            || (localSubjectId.HasValue && receipt.LocalSubjectId != localSubjectId.Value)
            || result.Outcome is not (LocalCredentialIssueOutcome.Issued or LocalCredentialIssueOutcome.Replayed)
            || (result.Outcome == LocalCredentialIssueOutcome.Issued && string.IsNullOrWhiteSpace(result.TemporaryPassword))
            || (result.Outcome == LocalCredentialIssueOutcome.Replayed && result.TemporaryPassword is not null))
        {
            throw new InvalidOperationException("Local credential response is unavailable.");
        }
    }
}
