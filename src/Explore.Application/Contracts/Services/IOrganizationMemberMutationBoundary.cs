using Explore.Application.Responses;

namespace Explore.Application.Contracts.Services;

/// <summary>Owns current organization authority and its qualification invalidation through commit.</summary>
public interface IOrganizationMemberMutationBoundary
{
    Task<BaseCommandResponse<Guid>> ExecuteAddAsync(Guid tenantId, Guid organizationId, Guid memberId, string email, int roleId,
        Func<Guid, Guid, CancellationToken, Task<BaseCommandResponse<Guid>>> operation, CancellationToken cancellationToken);
    Task<BaseCommandResponse<Guid>> ExecuteRoleChangeAsync(Guid memberId, int roleId,
        Func<Guid, CancellationToken, Task<BaseCommandResponse<Guid>>> operation, CancellationToken cancellationToken);
    Task<BaseCommandResponse<Guid>> ExecuteRemovalAsync(Guid memberId, bool recipientDecline,
        Func<Guid, CancellationToken, Task<BaseCommandResponse<Guid>>> operation, CancellationToken cancellationToken);
}
