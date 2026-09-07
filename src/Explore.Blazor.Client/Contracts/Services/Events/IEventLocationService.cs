using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.Events;

/// <summary>
/// One client-side entry point per disclosure purpose. Public, attendee, and management reads are
/// deliberately separate operations with separate response shapes: the server decides what each
/// audience may see, and the client never widens a narrower projection into a richer one.
/// </summary>
public interface IEventLocationService
{
    /// <summary>Anonymous public disclosures for a published public event.</summary>
    Task<IReadOnlyList<EventLocationPublicDto>> GetPublicAsync(
        Guid eventId,
        CancellationToken cancellationToken = default);

    /// <summary>Registration-scoped disclosures for the signed-in requester.</summary>
    Task<IReadOnlyList<EventLocationAttendeeDto>> GetMyAccessAsync(
        Guid eventId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Management detail as a HAL resource. The resource is returned intact — never flattened — because
    /// its <c>_links</c> are the sole authority for whether the editor may offer mutations.
    /// </summary>
    Task<HalResourceOfEventLocationManagementDto?> GetManagementAsync(
        Guid eventId,
        Guid eventLocationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every EventLocation attached to the event, each carrying its own affordance links.
    /// </summary>
    Task<IReadOnlyList<HalResourceOfEventLocationManagementDto>> GetManagementListAsync(
        Guid eventId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// EventLocations still flagged for privacy remediation, each carrying its own affordance links so
    /// one unremediable row in the queue cannot borrow another row's permission.
    /// </summary>
    Task<IReadOnlyList<HalResourceOfEventLocationManagementDto>> GetReviewQueueAsync(
        Guid eventId,
        CancellationToken cancellationToken = default);

    Task<BaseCommandResponseOfGuid> UpdateDisclosureAsync(
        Guid eventId,
        Guid eventLocationId,
        UpdateEventLocationDisclosureDto request,
        CancellationToken cancellationToken = default);

    Task<BaseCommandResponseOfGuid> ConfirmRemediationAsync(
        Guid eventId,
        Guid eventLocationId,
        ConfirmEventLocationRemediationDto request,
        CancellationToken cancellationToken = default);
}
