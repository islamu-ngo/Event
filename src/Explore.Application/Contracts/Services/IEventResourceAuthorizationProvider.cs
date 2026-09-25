namespace Explore.Application.Contracts.Services;

/// <summary>Projects only the supplied closed snapshot. No authority enrichment, wall clock sampling, or grant caching is permitted.</summary>
public interface IEventResourceAuthorizationProvider
{
    Task<EventResourceProviderDecision> CheckAsync(EventResourceProviderInput input, CancellationToken cancellationToken);

    /// <summary>Checks frozen inputs in bounded principal/route groups with exact positional response binding.</summary>
    Task<IReadOnlyList<EventResourceProviderDecision>> CheckBatchAsync(
        IReadOnlyList<EventResourceProviderInput> inputs, CancellationToken cancellationToken);
}

public enum EventResourceProviderDecision { Deny, Allow, Unavailable }

public sealed record EventResourceProviderPrincipal(
    Guid UserId, Guid TenantId, bool IsTenantMember, bool ControlsOrganizer,
    bool HasEventUpdate, bool HasEventPublish, bool CanModerate);

public sealed record EventResourceProviderResource(
    Guid Id, Guid TenantId, Guid EventId, Guid? SessionId,
    int PublicationStateId, int DisclosureModeId, int KindId, int DeliveryTypeId,
    bool DiscloseMetadata, bool DisclosePrivateMetadata, bool CanAccess,
    bool ManagementCeiling, bool PublicationCeiling, bool IsCreation, bool DomainAllowed);

public sealed record EventResourceProviderInput(
    EventResourceProviderSnapshot Route,
    EventResourceProviderPrincipal Principal,
    EventResourceProviderResource Resource,
    string Action,
    EventResourceParentModeration? ParentModeration = null);
