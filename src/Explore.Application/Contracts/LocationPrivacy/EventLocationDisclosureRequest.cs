namespace Explore.Application.Contracts.LocationPrivacy;

public sealed record EventLocationDisclosureRequest(
    Guid TenantId,
    Guid EventId,
    Guid EventLocationId,
    Guid? RoomId,
    Guid? RequesterUserId,
    EventLocationDisclosurePurpose Purpose);
