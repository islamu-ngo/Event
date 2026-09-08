using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Admissions;
using Explore.Blazor.Client.Services.Http;

namespace Explore.Blazor.Client.Services.Admissions;

public sealed class ParticipantReadinessService(
    IBffClient bff) :
    IParticipantReadinessService
{
    public Task<HalResourceOfParticipantReadinessDto?>
        GetAsync(
            Guid eventId,
            Guid orderId,
            Guid participantId,
            Guid assignmentId,
            string? guestCapability,
            CancellationToken cancellationToken)
    {
        string path = Path(
            eventId,
            orderId,
            participantId,
            assignmentId);
        return bff
            .GetWithRegistrationOrderCapabilityAsync<
                HalResourceOfParticipantReadinessDto>(
                path,
                guestCapability,
                cancellationToken);
    }

    public Task<HalResourceOfParticipantReadinessDto?>
        CompleteAsync(
            Guid eventId,
            Guid orderId,
            Guid participantId,
            Guid assignmentId,
            CancellationToken cancellationToken) =>
        MutateAsync(
            eventId,
            orderId,
            participantId,
            assignmentId,
            "complete",
            cancellationToken);

    public Task<HalResourceOfParticipantReadinessDto?>
        ApproveAsync(
            Guid eventId,
            Guid orderId,
            Guid participantId,
            Guid assignmentId,
            CancellationToken cancellationToken) =>
        MutateAsync(
            eventId,
            orderId,
            participantId,
            assignmentId,
            "approve",
            cancellationToken);

    public Task<HalResourceOfParticipantReadinessDto?>
        RevokeAsync(
            Guid eventId,
            Guid orderId,
            Guid participantId,
            Guid assignmentId,
            CancellationToken cancellationToken) =>
        MutateAsync(
            eventId,
            orderId,
            participantId,
            assignmentId,
            "revoke",
            cancellationToken);

    private Task<HalResourceOfParticipantReadinessDto?>
        MutateAsync(
            Guid eventId,
            Guid orderId,
            Guid participantId,
            Guid assignmentId,
            string action,
            CancellationToken cancellationToken) =>
        bff.SendAsync<
            HalResourceOfParticipantReadinessDto>(
            HttpMethod.Post,
            $"{Path(
                eventId,
                orderId,
                participantId,
                assignmentId)}/{action}",
            cancellationToken);

    private static string Path(
        Guid eventId,
        Guid orderId,
        Guid participantId,
        Guid assignmentId) =>
        $"/bff/events/{eventId:D}/participant-readiness/" +
        $"registration-orders/{orderId:D}/participants/" +
        $"{participantId:D}/assignments/{assignmentId:D}";
}
