using System.Collections.Immutable;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Contracts.Services;

public interface IEventLocationRegistrationAccessService
{
    EventLocationRegistrationAccess Resolve(EventLocationRegistrationAccessRequest request);

    IReadOnlyDictionary<Guid, EventLocationRegistrationAccess> ResolveMany(
        Guid tenantId,
        Guid eventId,
        Guid userId,
        DateTimeOffset asOfUtc,
        IReadOnlyCollection<Guid> requestedEventLocationIds,
        IReadOnlyCollection<EventRegistration> registrations);
}

public sealed record EventLocationRegistrationAccessRequest(
    Guid RequestedEventLocationId,
    DateTimeOffset AsOfUtc,
    EventLocationRegistrationOrderFact Order,
    ImmutableArray<EventLocationRegistrationCoverageFact> Coverage);

public sealed record EventLocationRegistrationOrderFact(
    Guid OrderId,
    Guid EventId,
    int OrderStatusId,
    bool IsDeleted,
    DateTimeOffset? ExpiresAtUtc);

public sealed record EventLocationRegistrationCoverageFact(
    Guid OrderId,
    Guid EventId,
    Guid? EventDayId,
    Guid EventSessionId,
    Guid EventLocationId,
    int? ApprovalStatusId,
    int? RegistrationModeId,
    bool IsDeleted,
    DateTimeOffset? ExpiresAtUtc);
