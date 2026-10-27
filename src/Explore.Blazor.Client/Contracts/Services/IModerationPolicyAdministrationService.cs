using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Services.Moderation;

namespace Explore.Blazor.Client.Contracts.Services;

public interface IModerationPolicyAdministrationService
{
    Task<ModerationPolicyAdministrationReadResult> ReadAsync(
        ModerationPolicyAdministrationScope scope, CancellationToken cancellationToken = default);

    Task<ModerationPolicyAdministrationOperationResult> CreateDraftAsync(
        ModerationPolicyAdministrationScope scope, ModerationPolicyProposedState proposed,
        Guid operationId, CancellationToken cancellationToken = default);

    Task<ModerationPolicyAdministrationOperationResult> CreatePreparationAsync(
        ModerationPolicyAdministrationScope scope, ModerationPolicyProposedState proposed,
        Guid operationId, DateTimeOffset validUntil, CancellationToken cancellationToken = default);

    Task<ModerationPolicyAdministrationSubmissionResult> SubmitAsync(
        ModerationPolicyAdministrationOperation operation, CancellationToken cancellationToken = default);
}
