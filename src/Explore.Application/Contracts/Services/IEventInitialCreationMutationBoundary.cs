using Explore.Application.Responses;
using Explore.Domain.Enums;

namespace Explore.Application.Contracts.Services;

/// <summary>Original invocation coordinates for native Event creation, not a reusable authorization grant.</summary>
public sealed record EventInitialCreation(Guid TenantId, Guid EventId, Guid OwnerAssignmentId,
    Guid CreatorUserId, Guid PublisherActorId, Guid? OrganizationId, Guid? GroupId,
    bool CommunitySubmission, DateTime CreatedAtUtc, IReadOnlyList<Guid> ImageIds, EventStatusEnum RequestedStatus);

/// <summary>Commits the new Event graph and initial owner under original publisher and privacy authority.</summary>
public interface IEventInitialCreationMutationBoundary
{
    Task<BaseCommandResponse<Guid>> ExecuteCreationAsync(EventInitialCreation creation,
        Func<Guid, CancellationToken, Task<bool>> qualifyPublisher,
        Func<Guid, CancellationToken, Task<BaseCommandResponse<Guid>>> operation,
        CancellationToken cancellationToken);
}
