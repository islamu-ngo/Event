using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Completion is callable only inside the real location producer's private admission.</summary>
public interface IMixedLocationPrivacyGovernanceStore
{
    DateTime? MixedOccurredAt { get; }
    Task CompleteMixedAsync(string key, Guid? tenantId, Guid actorUserId, CancellationToken cancellationToken);
}

/// <summary>Transaction-enlisted policy facts and fixed governance corrections, not private entity access.</summary>
public interface ILocationPrivacyGovernanceStore
{
    Task<IReadOnlyList<LocationPrivacyGovernanceOverride>> ReadOverridesAsync(
        string key, Guid? tenantId, CancellationToken cancellationToken);

    Task<IReadOnlyList<EventLocationGovernanceState>> ReadActiveProjectionsAsync(
        Guid? tenantId, CancellationToken cancellationToken);

    Task ApplyCorrectionsAsync(
        string key, Guid? tenantId, IReadOnlyList<LocationPrivacyGovernanceCorrection> corrections,
        Guid actorUserId, DateTime changedAtUtc, CancellationToken cancellationToken);
}

public sealed record LocationPrivacyGovernanceOverride(Guid TenantId, string StoredValue);

/// <summary>The validated C# policy transition and its corresponding durable effect.</summary>
public sealed record LocationPrivacyGovernanceCorrection(
    EventLocationGovernanceState Before, EventLocationDisclosureAudit Audit, OutboxMessage Message);
