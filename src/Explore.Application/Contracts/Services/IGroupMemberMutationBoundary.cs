using Explore.Application.Responses;

namespace Explore.Application.Contracts.Services;

/// <summary>Commits ordinary group membership changes with current native qualification.</summary>
public interface IGroupMemberMutationBoundary
{
    Task<BaseCommandResponse<Guid>> ExecuteAddAsync(Guid tenantId, Guid groupId, Guid memberId, string email, int roleId,
        Func<Guid, Guid, CancellationToken, Task<BaseCommandResponse<Guid>>> operation, CancellationToken cancellationToken);
    Task<BaseCommandResponse<Guid>> ExecuteRoleChangeAsync(Guid memberId, int roleId,
        Func<Guid, CancellationToken, Task<BaseCommandResponse<Guid>>> operation, CancellationToken cancellationToken);
    Task<BaseCommandResponse<Guid>> ExecuteRemovalAsync(Guid memberId,
        Func<Guid, CancellationToken, Task<BaseCommandResponse<Guid>>> operation, CancellationToken cancellationToken);
}
