using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Admissions;
using Explore.Blazor.Client.Services.Http;
using System.Text.Json;

namespace Explore.Blazor.Client.Services.Admissions;

public sealed class TicketTransferService(
    IBffClient bff) :
    ITicketTransferService
{
    public Task<HalResourceOfTicketTransferDto?> GetAsync(
        Guid eventId,
        Guid admissionTicketId,
        Guid transferId,
        string? capability,
        CancellationToken cancellationToken) =>
        SafeAsync(() =>
            bff.GetWithTicketTransferCapabilityAsync<
                HalResourceOfTicketTransferDto>(
                    Path(
                        eventId,
                        admissionTicketId,
                        transferId),
                    capability,
                    cancellationToken));

    public Task<TicketTransferOfferResponse?> OfferAsync(
        Guid eventId,
        Guid admissionTicketId,
        CancellationToken cancellationToken) =>
        SafeAsync(() =>
            bff.SendAsync<TicketTransferOfferResponse>(
                HttpMethod.Post,
                RootPath(
                    eventId,
                    admissionTicketId),
                cancellationToken));

    public Task<TicketTransferCredentialResponse?> AcceptAsync(
        Guid eventId,
        Guid admissionTicketId,
        Guid transferId,
        Guid recipientParticipantId,
        string? capability,
        CancellationToken cancellationToken) =>
        SafeAsync(() =>
            bff.SendWithTicketTransferCapabilityAsync<
                AcceptTicketTransferRequest,
                TicketTransferCredentialResponse>(
                    HttpMethod.Post,
                    $"{Path(
                        eventId,
                        admissionTicketId,
                        transferId)}/accept",
                    new AcceptTicketTransferRequest
                    {
                        RecipientParticipantId =
                            recipientParticipantId,
                    },
                    capability,
                    cancellationToken));

    public Task<HalResourceOfTicketTransferDto?> CancelAsync(
        Guid eventId,
        Guid admissionTicketId,
        Guid transferId,
        CancellationToken cancellationToken) =>
        SafeAsync(() =>
            bff.SendAsync<
                HalResourceOfTicketTransferDto>(
                    HttpMethod.Delete,
                    Path(
                        eventId,
                        admissionTicketId,
                        transferId),
                    cancellationToken));

    public Task<TicketTransferCredentialResponse?> CorrectAsync(
        Guid eventId,
        Guid admissionTicketId,
        Guid transferId,
        CancellationToken cancellationToken) =>
        SafeAsync(() =>
            bff.SendAsync<
                TicketTransferCredentialResponse>(
                    HttpMethod.Post,
                    $"{Path(
                        eventId,
                        admissionTicketId,
                        transferId)}/correction",
                    cancellationToken));

    public Task<TicketTransferCredentialResponse?> ReissueAsync(
        Guid eventId,
        Guid admissionTicketId,
        Guid transferId,
        CancellationToken cancellationToken) =>
        SafeAsync(() =>
            bff.SendAsync<
                TicketTransferCredentialResponse>(
                    HttpMethod.Post,
                    $"{Path(
                        eventId,
                        admissionTicketId,
                        transferId)}/reissue",
                    cancellationToken));

    private static async Task<T?> SafeAsync<T>(
        Func<Task<T?>> operation)
        where T : class
    {
        try
        {
            return await operation();
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static string RootPath(
        Guid eventId,
        Guid admissionTicketId) =>
        $"/bff/events/{eventId:D}/admission-tickets/" +
        $"{admissionTicketId:D}/transfers";

    private static string Path(
        Guid eventId,
        Guid admissionTicketId,
        Guid transferId) =>
        $"{RootPath(eventId, admissionTicketId)}/" +
        $"{transferId:D}";
}
