using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services;

public sealed record StudioAttendeeOrder(
    HalResourceOfRegistrationOrderDto Order,
    HalResourceOfRegistrationOrderParticipantsDto Participants);
