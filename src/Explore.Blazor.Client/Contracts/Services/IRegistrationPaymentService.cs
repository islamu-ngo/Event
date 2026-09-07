using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services;

public interface IRegistrationPaymentService
{
    Task<PaidOrderAcceptanceDisclosureDto?> GetCurrentPaymentAcceptanceAsync(Guid eventId, Guid orderId, HalResourceOfRegistrationOrderDto order, CancellationToken cancellationToken = default);
    Task<HalResourceOfRegistrationPaymentDto?> StartCurrentPaymentAsync(Guid eventId, Guid orderId, HalResourceOfRegistrationOrderDto order, string disclosureRevision, CancellationToken cancellationToken = default);
    Task<HalResourceOfRegistrationPaymentDto?> GetCurrentPaymentAsync(Guid eventId, Guid orderId, HalResourceOfRegistrationOrderDto order, CancellationToken cancellationToken = default);
    Task<HalResourceOfRegistrationPaymentDto?> RefreshCurrentPaymentAsync(Guid eventId, Guid orderId, HalResourceOfRegistrationPaymentDto payment, CancellationToken cancellationToken = default);
    Task<HalResourceOfRegistrationPaymentDto?> RetryCurrentPaymentAsync(Guid eventId, Guid orderId, HalResourceOfRegistrationPaymentDto payment, CancellationToken cancellationToken = default);
    Task<HalResourceOfRegistrationPaymentDto?> RequestCurrentRefundAsync(Guid eventId, Guid orderId, HalResourceOfRegistrationPaymentDto payment, CancellationToken cancellationToken = default);
    Task<HalResourceOfRegistrationPaymentDto?> RespondCurrentMaterialChangeAsync(Guid eventId, Guid orderId, HalResourceOfRegistrationPaymentDto payment, Guid campaignId, string choiceCode, CancellationToken cancellationToken = default);
    Task<string?> IssueCurrentPaymentCheckoutTicketAsync(string path, CancellationToken cancellationToken = default);

    Task<PaidOrderAcceptanceDisclosureDto?> GetGuestPaymentAcceptanceAsync(Guid eventId, Guid orderId, GuestRegistrationOrderCapability capability, HalResourceOfGuestRegistrationOrderDto order, CancellationToken cancellationToken = default);
    Task<HalResourceOfRegistrationPaymentDto?> StartGuestPaymentAsync(Guid eventId, Guid orderId, GuestRegistrationOrderCapability capability, HalResourceOfGuestRegistrationOrderDto order, string disclosureRevision, CancellationToken cancellationToken = default);
    Task<HalResourceOfRegistrationPaymentDto?> GetGuestPaymentAsync(Guid eventId, Guid orderId, GuestRegistrationOrderCapability capability, HalResourceOfGuestRegistrationOrderDto order, CancellationToken cancellationToken = default);
    Task<HalResourceOfRegistrationPaymentDto?> RefreshGuestPaymentAsync(Guid eventId, Guid orderId, GuestRegistrationOrderCapability capability, HalResourceOfRegistrationPaymentDto payment, CancellationToken cancellationToken = default);
    Task<HalResourceOfRegistrationPaymentDto?> RetryGuestPaymentAsync(Guid eventId, Guid orderId, GuestRegistrationOrderCapability capability, HalResourceOfRegistrationPaymentDto payment, CancellationToken cancellationToken = default);
    Task<string?> IssueGuestPaymentCheckoutTicketAsync(string path, GuestRegistrationOrderCapability capability, CancellationToken cancellationToken = default);

    Task<HalResourceOfRegistrationPaymentDto?> GetStudioPaymentAsync(Guid eventId, Guid orderId, HalResourceOfRegistrationOrderDto order, CancellationToken cancellationToken = default);
    Task<HalResourceOfRegistrationPaymentDto?> CreateStudioRefundAsync(Guid eventId, Guid orderId, HalResourceOfRegistrationPaymentDto payment, long? amountMinor, CancellationToken cancellationToken = default);
    Task<HalResourceOfRegistrationPaymentDto?> RetryStudioRefundAsync(Guid eventId, Guid orderId, HalResourceOfRegistrationPaymentDto payment, CancellationToken cancellationToken = default);

    Task<HalCollectionResourceOfRefundCampaignDto?> GetRefundCampaignsAsync(Guid eventId, CancellationToken cancellationToken = default);
    Task<HalCollectionResourceOfRefundCampaignDto?> ResumeRefundCampaignAsync(Guid eventId, HalResourceOfRefundCampaignDto campaign, CancellationToken cancellationToken = default);
}
