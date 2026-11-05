namespace Explore.Application.Contracts.Services;

public enum VisitorTenantReadAccessDecision
{
    Allowed,
    AuthenticationRequired,
    Unavailable,
    NotFound
}

/// <summary>Fresh tenant read admission; an allowed decision does not replace resource authorization.</summary>
public interface IVisitorTenantReadAccessPolicy
{
    Task<VisitorTenantReadAccessDecision> CheckAsync(Guid? tenantId, CancellationToken cancellationToken = default);

    /// <summary>Rejects native tenant-content reads before any payload or cached projection is retrieved.</summary>
    Task RequireAsync(Guid? tenantId, CancellationToken cancellationToken = default);

    /// <summary>Uses anonymous authority even when the caller is an authenticated publisher or worker.</summary>
    Task<VisitorTenantReadAccessDecision> CheckPublicAsync(Guid? tenantId, CancellationToken cancellationToken = default);
}
