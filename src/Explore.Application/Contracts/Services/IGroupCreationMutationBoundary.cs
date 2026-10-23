using Explore.Application.Responses;

namespace Explore.Application.Contracts.Services;

public sealed record GroupCreationIds(Guid GroupId, Guid ActorId, Guid ParticipationId, Guid MemberId)
{
    public override string ToString() => nameof(GroupCreationIds);
}

/// <summary>Owns initial Group administration, privacy and qualification invalidation through commit.</summary>
public interface IGroupCreationMutationBoundary
{
    Task<BaseCommandResponse<Guid>> ExecuteCreationAsync(Guid tenantId, GroupCreationIds ids,
        Func<Guid, CancellationToken, Task<BaseCommandResponse<Guid>>> operation, CancellationToken cancellationToken);
}
