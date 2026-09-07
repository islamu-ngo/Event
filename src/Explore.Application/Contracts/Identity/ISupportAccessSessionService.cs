namespace Explore.Application.Contracts.Identity;

public interface ISupportAccessSessionService
{
    Task<ISupportAccessContext> GetCurrentAsync(CancellationToken cancellationToken = default);

    Task<ISupportAccessContext> ValidateForwardedSessionAsync(
        Guid sessionId,
        Guid actorUserId,
        Guid? resolvedTenantId,
        CancellationToken cancellationToken = default);
}
