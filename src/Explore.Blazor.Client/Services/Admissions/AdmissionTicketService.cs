using System.Net;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Admissions;
using Explore.Blazor.Client.Services.Http;

namespace Explore.Blazor.Client.Services.Admissions;

public sealed class AdmissionTicketService(
    IAdmissionTicketClient ticketClient,
    IAdmissionTicketRecoveryClient recoveryClient,
    IAdmissionRecoveryBffClient recoveryBffClient,
    ILogger<AdmissionTicketService> logger) : IAdmissionTicketService
{
    public async Task<IReadOnlyList<HalResourceOfAdmissionTicketDto>> GetCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            HalCollectionResourceOfAdmissionTicketDto collection =
                await ticketClient.GetCurrentAdmissionTicketsAsync(
                    cancellationToken: cancellationToken);
            return collection._embedded?.Items is { Count: > 0 } items
                ? [.. items]
                : [];
        }
        catch (ApiException exception)
        {
            LogApiFailure("load ticket list", exception.StatusCode);
            return [];
        }
    }

    public async Task<HalResourceOfAdmissionTicketDto?> GetAsync(
        Guid ticketId,
        CancellationToken cancellationToken = default)
    {
        if (ticketId == Guid.Empty)
        {
            return null;
        }

        try
        {
            return await ticketClient.GetCurrentAdmissionTicketAsync(
                ticketId,
                cancellationToken: cancellationToken);
        }
        catch (ApiException exception)
            when (exception.StatusCode is (int)HttpStatusCode.Forbidden
                or (int)HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (ApiException exception)
        {
            LogApiFailure("load ticket detail", exception.StatusCode);
            return null;
        }
    }

    public async Task<AdmissionTicketQrDeliveryDto?> ReissueQrAsync(
        HalResourceOfAdmissionTicketDto ticket,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        if (!HasExactPostLink(ticket, "qr-code", $"/api/tickets/{ticket.TicketId:D}/qr"))
        {
            return null;
        }

        try
        {
            return await ticketClient.ReissueCurrentAdmissionTicketQrAsync(
                ticket.TicketId,
                cancellationToken: cancellationToken);
        }
        catch (ApiException exception)
        {
            LogApiFailure("reissue ticket QR", exception.StatusCode);
            return null;
        }
    }

    public async Task<AdmissionTicketPrintDeliveryDto?> ReissuePrintAsync(
        HalResourceOfAdmissionTicketDto ticket,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        if (!HasExactPostLink(ticket, "print", $"/api/tickets/{ticket.TicketId:D}/print"))
        {
            return null;
        }

        try
        {
            return await ticketClient.ReissueCurrentAdmissionTicketPrintAsync(
                ticket.TicketId,
                cancellationToken: cancellationToken);
        }
        catch (ApiException exception)
        {
            LogApiFailure("reissue printable ticket", exception.StatusCode);
            return null;
        }
    }

    public async Task<bool> RequestRecoveryAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        try
        {
            AdmissionTicketRecoveryRequestResultDto result =
                await recoveryClient.RequestAdmissionTicketRecoveryAsync(
                    Guid.CreateVersion7().ToString("D"),
                    body: new RequestAdmissionTicketRecoveryCommand { Email = email },
                    cancellationToken: cancellationToken);
            return result.Accepted && result.Success;
        }
        catch (ApiException exception)
        {
            LogApiFailure("request ticket recovery", exception.StatusCode);
            return false;
        }
    }

    public async Task<AdmissionRecoveryUiResult> ConsumeRecoveryAsync(
        string capability,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(capability) || capability.Length > 256)
        {
            return new AdmissionRecoveryUiResult(AdmissionRecoveryUiOutcome.Invalid);
        }

        try
        {
            ApiResult<AdmissionTicketRecoveryDeliveryDto> result =
                await recoveryBffClient.ConsumeAsync(capability, cancellationToken);
            if (result.StatusCode == HttpStatusCode.NotFound)
            {
                return new AdmissionRecoveryUiResult(AdmissionRecoveryUiOutcome.Invalid);
            }
            if (result.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return new AdmissionRecoveryUiResult(AdmissionRecoveryUiOutcome.RateLimited);
            }
            if (!result.IsSuccess || result.Value is null)
            {
                return new AdmissionRecoveryUiResult(AdmissionRecoveryUiOutcome.Unavailable);
            }

            return new AdmissionRecoveryUiResult(
                AdmissionRecoveryUiOutcome.Consumed,
                result.Value);
        }
        catch (HttpRequestException)
        {
            logger.LogError("Admission recovery BFF bridge was unavailable.");
            return new AdmissionRecoveryUiResult(AdmissionRecoveryUiOutcome.Unavailable);
        }
    }

    private static bool HasExactPostLink(
        HalResourceOfAdmissionTicketDto ticket,
        string relation,
        string expectedPath) =>
        ticket._links?.TryGetValue(relation, out HalLink? link) == true &&
        string.Equals(link.Method, HttpMethod.Post.Method, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(link.Href, expectedPath, StringComparison.Ordinal);

    private void LogApiFailure(string operation, int statusCode) =>
        logger.LogWarning(
            "Unable to {Operation}; downstream status {StatusCode}",
            operation,
            statusCode);

}
