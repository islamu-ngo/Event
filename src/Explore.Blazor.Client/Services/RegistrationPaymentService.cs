using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Helpers;
using Explore.Blazor.Client.Services.Http;

namespace Explore.Blazor.Client.Services;

public sealed class RegistrationPaymentService(
    IAuthenticatedRegistrationOrderPaymentClient authenticatedPaymentClient,
    IGuestRegistrationOrderPaymentClient guestPaymentClient,
    IStudioRegistrationOrderPaymentClient studioPaymentClient,
    IRefundCampaignClient refundCampaignClient,
    IBffClient bffClient,
    ILogger<RegistrationPaymentService> logger) : IRegistrationPaymentService
{
    public Task<PaidOrderAcceptanceDisclosureDto?> GetCurrentPaymentAcceptanceAsync(
        Guid eventId, Guid orderId, HalResourceOfRegistrationOrderDto order, CancellationToken cancellationToken = default) =>
        HasLink(order._links, "payment-acceptance")
            ? ExecuteAsync(() => authenticatedPaymentClient.GetAuthenticatedPaidOrderAcceptanceAsync(eventId, orderId, cancellationToken: cancellationToken))
            : Task.FromResult<PaidOrderAcceptanceDisclosureDto?>(null);

    public Task<HalResourceOfRegistrationPaymentDto?> StartCurrentPaymentAsync(
        Guid eventId, Guid orderId, HalResourceOfRegistrationOrderDto order, string disclosureRevision, CancellationToken cancellationToken = default) =>
        HasLink(order._links, "start-payment") && !string.IsNullOrWhiteSpace(disclosureRevision)
            ? ExecuteAsync(() => authenticatedPaymentClient.StartAuthenticatedRegistrationPaymentAsync(
                eventId, orderId, NewIdempotencyKey(), body: new PaidOrderAcceptanceAcknowledgementDto
                {
                    DisclosureRevision = disclosureRevision,
                    Acknowledged = true
                }, cancellationToken: cancellationToken))
            : Task.FromResult<HalResourceOfRegistrationPaymentDto?>(null);

    public Task<HalResourceOfRegistrationPaymentDto?> GetCurrentPaymentAsync(
        Guid eventId, Guid orderId, HalResourceOfRegistrationOrderDto order, CancellationToken cancellationToken = default) =>
        HasLink(order._links, "payment-status")
            ? ExecuteAsync(() => authenticatedPaymentClient.GetAuthenticatedRegistrationPaymentAsync(eventId, orderId, cancellationToken: cancellationToken))
            : Task.FromResult<HalResourceOfRegistrationPaymentDto?>(null);

    public Task<HalResourceOfRegistrationPaymentDto?> RefreshCurrentPaymentAsync(
        Guid eventId, Guid orderId, HalResourceOfRegistrationPaymentDto payment, CancellationToken cancellationToken = default) =>
        HasLink(payment._links, "payment-status")
            ? ExecuteAsync(() => authenticatedPaymentClient.GetAuthenticatedRegistrationPaymentAsync(eventId, orderId, cancellationToken: cancellationToken))
            : Task.FromResult<HalResourceOfRegistrationPaymentDto?>(null);

    public Task<HalResourceOfRegistrationPaymentDto?> RetryCurrentPaymentAsync(
        Guid eventId, Guid orderId, HalResourceOfRegistrationPaymentDto payment, CancellationToken cancellationToken = default) =>
        HasLink(payment._links, "retry-payment")
            ? ExecuteAsync(() => authenticatedPaymentClient.RetryAuthenticatedRegistrationPaymentAsync(
                eventId, orderId, NewIdempotencyKey(), cancellationToken: cancellationToken))
            : Task.FromResult<HalResourceOfRegistrationPaymentDto?>(null);

    public async Task<HalResourceOfRegistrationPaymentDto?> RequestCurrentRefundAsync(
        Guid eventId,
        Guid orderId,
        HalResourceOfRegistrationPaymentDto payment,
        CancellationToken cancellationToken = default)
    {
        if (!HasLink(payment._links, "request-refund"))
        {
            return null;
        }

        HalResourceOfRegistrationRefundDto? requested = await ExecuteAsync(() =>
            authenticatedPaymentClient.RequestAuthenticatedRegistrationRefundAsync(
                eventId,
                orderId,
                NewIdempotencyKey(),
                new RegistrationRefundRequestDto { ReasonCode = "event_cancelled" },
                cancellationToken: cancellationToken));
        return requested is null
            ? null
            : await ExecuteAsync(() => authenticatedPaymentClient.GetAuthenticatedRegistrationPaymentAsync(
                eventId, orderId, cancellationToken: cancellationToken));
    }

    public async Task<string?> IssueCurrentPaymentCheckoutTicketAsync(
        string path,
        CancellationToken cancellationToken = default) =>
        (await bffClient.IssueRegistrationPaymentCheckoutTicketAsync(path, null, cancellationToken))?.CheckoutPath;

    public async Task<HalResourceOfRegistrationPaymentDto?> RespondCurrentMaterialChangeAsync(
        Guid eventId,
        Guid orderId,
        HalResourceOfRegistrationPaymentDto payment,
        Guid campaignId,
        string choiceCode,
        CancellationToken cancellationToken = default)
    {
        if (!HasLink(payment._links, "respond-material-change") ||
            choiceCode is not ("accept_new_terms" or "request_refund"))
        {
            return null;
        }

        HalResourceOfRegistrationMaterialChangeChoiceDto? response = await ExecuteAsync(() =>
            authenticatedPaymentClient.RespondAuthenticatedRegistrationMaterialChangeAsync(
                eventId,
                orderId,
                NewIdempotencyKey(),
                new RegistrationMaterialChangeChoiceRequestDto
                {
                    CampaignId = campaignId,
                    ChoiceCode = choiceCode
                },
                cancellationToken: cancellationToken));
        return response is null
            ? null
            : await ExecuteAsync(() => authenticatedPaymentClient.GetAuthenticatedRegistrationPaymentAsync(
                eventId, orderId, cancellationToken: cancellationToken));
    }

    public Task<PaidOrderAcceptanceDisclosureDto?> GetGuestPaymentAcceptanceAsync(
        Guid eventId,
        Guid orderId,
        GuestRegistrationOrderCapability capability,
        HalResourceOfGuestRegistrationOrderDto order,
        CancellationToken cancellationToken = default) =>
        HasLink(order._links, "payment-acceptance")
            ? ExecuteAsync(() => guestPaymentClient.GetGuestPaidOrderAcceptanceAsync(
                eventId, orderId, capability.Value, cancellationToken: cancellationToken))
            : Task.FromResult<PaidOrderAcceptanceDisclosureDto?>(null);

    public Task<HalResourceOfRegistrationPaymentDto?> StartGuestPaymentAsync(
        Guid eventId,
        Guid orderId,
        GuestRegistrationOrderCapability capability,
        HalResourceOfGuestRegistrationOrderDto order,
        string disclosureRevision,
        CancellationToken cancellationToken = default) =>
        HasLink(order._links, "start-payment") && !string.IsNullOrWhiteSpace(disclosureRevision)
            ? ExecuteAsync(() => guestPaymentClient.StartGuestRegistrationPaymentAsync(
                eventId, orderId, NewIdempotencyKey(), capability.Value, body: new PaidOrderAcceptanceAcknowledgementDto
                {
                    DisclosureRevision = disclosureRevision,
                    Acknowledged = true
                }, cancellationToken: cancellationToken))
            : Task.FromResult<HalResourceOfRegistrationPaymentDto?>(null);

    public Task<HalResourceOfRegistrationPaymentDto?> GetGuestPaymentAsync(
        Guid eventId,
        Guid orderId,
        GuestRegistrationOrderCapability capability,
        HalResourceOfGuestRegistrationOrderDto order,
        CancellationToken cancellationToken = default) =>
        HasLink(order._links, "payment-status")
            ? ExecuteAsync(() => guestPaymentClient.GetGuestRegistrationPaymentAsync(
                eventId, orderId, capability.Value, cancellationToken: cancellationToken))
            : Task.FromResult<HalResourceOfRegistrationPaymentDto?>(null);

    public Task<HalResourceOfRegistrationPaymentDto?> RefreshGuestPaymentAsync(
        Guid eventId,
        Guid orderId,
        GuestRegistrationOrderCapability capability,
        HalResourceOfRegistrationPaymentDto payment,
        CancellationToken cancellationToken = default) =>
        HasLink(payment._links, "payment-status")
            ? ExecuteAsync(() => guestPaymentClient.GetGuestRegistrationPaymentAsync(
                eventId, orderId, capability.Value, cancellationToken: cancellationToken))
            : Task.FromResult<HalResourceOfRegistrationPaymentDto?>(null);

    public Task<HalResourceOfRegistrationPaymentDto?> RetryGuestPaymentAsync(
        Guid eventId,
        Guid orderId,
        GuestRegistrationOrderCapability capability,
        HalResourceOfRegistrationPaymentDto payment,
        CancellationToken cancellationToken = default) =>
        HasLink(payment._links, "retry-payment")
            ? ExecuteAsync(() => guestPaymentClient.RetryGuestRegistrationPaymentAsync(
                eventId, orderId, NewIdempotencyKey(), capability.Value, cancellationToken: cancellationToken))
            : Task.FromResult<HalResourceOfRegistrationPaymentDto?>(null);

    public async Task<string?> IssueGuestPaymentCheckoutTicketAsync(
        string path,
        GuestRegistrationOrderCapability capability,
        CancellationToken cancellationToken = default) =>
        (await bffClient.IssueRegistrationPaymentCheckoutTicketAsync(path, capability.Value, cancellationToken))?.CheckoutPath;

    public Task<HalResourceOfRegistrationPaymentDto?> GetStudioPaymentAsync(
        Guid eventId,
        Guid orderId,
        HalResourceOfRegistrationOrderDto order,
        CancellationToken cancellationToken = default) =>
        HasLink(order._links, "studio-payment-status")
            ? ExecuteAsync(() => studioPaymentClient.GetStudioRegistrationPaymentAsync(eventId, orderId, cancellationToken: cancellationToken))
            : Task.FromResult<HalResourceOfRegistrationPaymentDto?>(null);

    public async Task<HalResourceOfRegistrationPaymentDto?> CreateStudioRefundAsync(
        Guid eventId,
        Guid orderId,
        HalResourceOfRegistrationPaymentDto payment,
        long? amountMinor,
        CancellationToken cancellationToken = default)
    {
        if (!HasLink(payment._links, "create-refund"))
        {
            return null;
        }

        HalResourceOfRegistrationRefundDto? created = await ExecuteAsync(() =>
            studioPaymentClient.CreateStudioRegistrationRefundAsync(
                eventId,
                orderId,
                NewIdempotencyKey(),
                new RegistrationRefundRequestDto { AmountMinor = amountMinor, ReasonCode = "organizer_refund" },
                cancellationToken: cancellationToken));
        return created is null
            ? null
            : await ExecuteAsync(() => studioPaymentClient.GetStudioRegistrationPaymentAsync(
                eventId, orderId, cancellationToken: cancellationToken));
    }

    public async Task<HalResourceOfRegistrationPaymentDto?> RetryStudioRefundAsync(
        Guid eventId,
        Guid orderId,
        HalResourceOfRegistrationPaymentDto payment,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetRefundAttemptId(payment._links, "retry-refund", out Guid refundAttemptId))
        {
            return null;
        }

        HalResourceOfRegistrationRefundDto? retried = await ExecuteAsync(() =>
            studioPaymentClient.RetryStudioRegistrationRefundAsync(
                eventId, orderId, refundAttemptId, NewIdempotencyKey(), cancellationToken: cancellationToken));
        return retried is null
            ? null
            : await ExecuteAsync(() => studioPaymentClient.GetStudioRegistrationPaymentAsync(
                eventId, orderId, cancellationToken: cancellationToken));
    }

    public Task<HalCollectionResourceOfRefundCampaignDto?> GetRefundCampaignsAsync(
        Guid eventId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => refundCampaignClient.GetRefundCampaignsAsync(eventId, cancellationToken: cancellationToken));

    public async Task<HalCollectionResourceOfRefundCampaignDto?> ResumeRefundCampaignAsync(
        Guid eventId,
        HalResourceOfRefundCampaignDto campaign,
        CancellationToken cancellationToken = default)
    {
        if (!HasLink(campaign._links, "resume-refund-campaign") || campaign.Id is not { } campaignId)
        {
            return null;
        }
        HalResourceOfRefundCampaignDto? resumed = await ExecuteAsync(() => refundCampaignClient.ResumeRefundCampaignAsync(
            eventId, campaignId, NewIdempotencyKey(), cancellationToken: cancellationToken));
        return resumed is null
            ? null
            : await GetRefundCampaignsAsync(eventId, cancellationToken);
    }

    private async Task<T?> ExecuteAsync<T>(Func<Task<T>> execute)
        where T : class
    {
        try
        {
            return await execute();
        }
        catch (ApiException exception)
        {
            logger.LogWarning("Registration payment client operation failed. Status: {StatusCode}.", exception.StatusCode);
            return null;
        }
    }

    private static bool HasLink(IDictionary<string, HalLink>? links, string relation) =>
        links?.ContainsKey(relation) == true;

    private static bool TryGetRefundAttemptId(
        IDictionary<string, HalLink>? links,
        string relation,
        out Guid refundAttemptId)
    {
        refundAttemptId = Guid.Empty;
        if (links is null || !links.TryGetValue(relation, out HalLink? link) || string.IsNullOrWhiteSpace(link.Href))
        {
            return false;
        }

        string rawHref = link.Href;
        int queryIndex = rawHref.IndexOf('?', StringComparison.Ordinal);
        string path = queryIndex >= 0 ? rawHref[..queryIndex] : rawHref;
        string candidate = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? string.Empty;
        return Guid.TryParse(candidate, out refundAttemptId);
    }

    private static string NewIdempotencyKey() => Guid.CreateVersion7().ToString("N");
}
