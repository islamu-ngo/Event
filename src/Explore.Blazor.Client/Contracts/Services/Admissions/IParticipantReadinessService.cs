using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.Admissions;

public interface IParticipantReadinessService
{
    Task<HalResourceOfParticipantReadinessDto?> GetAsync(
        Guid eventId,
        Guid orderId,
        Guid participantId,
        Guid assignmentId,
        string? guestCapability,
        CancellationToken cancellationToken);

    Task<HalResourceOfParticipantReadinessDto?> CompleteAsync(
        Guid eventId,
        Guid orderId,
        Guid participantId,
        Guid assignmentId,
        CancellationToken cancellationToken);

    Task<HalResourceOfParticipantReadinessDto?> ApproveAsync(
        Guid eventId,
        Guid orderId,
        Guid participantId,
        Guid assignmentId,
        CancellationToken cancellationToken);

    Task<HalResourceOfParticipantReadinessDto?> RevokeAsync(
        Guid eventId,
        Guid orderId,
        Guid participantId,
        Guid assignmentId,
        CancellationToken cancellationToken);
}
