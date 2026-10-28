using Explore.Application.Responses;

namespace Explore.Application.Contracts.Services;

public sealed record OrganizationCreationIds(Guid OrganizationId, Guid ActorId, Guid ParticipationId, Guid MemberId)
{
    public override string ToString() => nameof(OrganizationCreationIds);
}

/// <summary>Owns initial administration and its actual creator/approval qualification through commit.</summary>
public interface IOrganizationCreationMutationBoundary
{
    Task<BaseCommandResponse<Guid>> ExecuteCreationAsync(Guid tenantId, OrganizationCreationIds ids,
        Func<Guid, bool, CancellationToken, Task<BaseCommandResponse<Guid>>> operation, CancellationToken cancellationToken);
}
