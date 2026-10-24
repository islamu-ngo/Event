using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Acquires the closed anonymous calendar purpose on the selected native primary.
/// Provenance administrators are not the viewer, and an authenticated evidence
/// session, retained contribution or incomplete classification cannot grant access.
/// </summary>
public interface IModerationPublicEventCalendarRepository
{
    Task<T> ExecuteSerializedAsync<T>(
        Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken);

    Task<ModerationPublicEventCalendarAcquisition> AcquireAsync(
        ModerationPublicEventCalendarRequest request, CancellationToken cancellationToken);
}

public sealed record ModerationPublicEventCalendarRequest(Guid TenantId, Guid EventId);

public enum ModerationPublicEventCalendarOutcome { Allowed = 1, NotFound = 2, Unavailable = 3 }

public sealed record ModerationPublicEventCalendarAcquisition(
    ModerationPublicEventCalendarOutcome Outcome, IModerationPublicEventCalendarLease? Lease = null);

/// <summary>
/// Native entities remain under the original serializable, identity/content and
/// classification fences. Revalidation must cover the exact acquired generation,
/// complete unrestricted audience floor and source deadline through terminal use.
/// </summary>
public interface IModerationPublicEventCalendarLease : IAsyncDisposable
{
    Event Event { get; }
    EventSession PrimarySession { get; }
    DateTimeOffset ValidUntil { get; }
    CancellationToken ValidityCancellation { get; }
    Task<ModerationPublicEventCalendarOutcome> RevalidateAsync(CancellationToken cancellationToken);
}
