using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.RegistrationOrders.Requests.Queries;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.RegistrationOrders.Handlers.Queries;

public sealed class GetRefundCampaignsQueryHandler(IRefundCampaignRepository campaigns, ITenantContext tenant)
    : IRequestHandler<GetRefundCampaignsQuery, IReadOnlyList<RefundCampaignDto>>
{
    public async Task<IReadOnlyList<RefundCampaignDto>> Handle(
        GetRefundCampaignsQuery request,
        CancellationToken cancellationToken) =>
        (await campaigns.GetByEventAsync(tenant.TenantId, request.EventId, cancellationToken))
        .Select(RefundCampaignMapper.Map)
        .ToArray();
}

public sealed class GetRefundCampaignQueryHandler(IRefundCampaignRepository campaigns, ITenantContext tenant)
    : IRequestHandler<GetRefundCampaignQuery, RefundCampaignDto?>
{
    public async Task<RefundCampaignDto?> Handle(GetRefundCampaignQuery request, CancellationToken cancellationToken)
    {
        RefundCampaign? campaign = await campaigns.GetByIdAsync(tenant.TenantId, request.CampaignId, cancellationToken);
        return campaign?.EventId == request.EventId ? RefundCampaignMapper.Map(campaign) : null;
    }
}

internal static class RefundCampaignMapper
{
    internal static RefundCampaignDto Map(RefundCampaign campaign) => new()
    {
        Id = campaign.Id,
        EventId = campaign.EventId,
        KindCode = campaign.Kind.ToString(),
        StatusCode = campaign.Status.ToString(),
        DecisionAt = campaign.DecisionAt,
        TotalPaymentCount = campaign.TotalPaymentCount,
        GeneratedCount = campaign.GeneratedCount,
        PendingCount = campaign.PendingCount,
        SucceededCount = campaign.SucceededCount,
        FailedCount = campaign.FailedCount,
        UnknownCount = campaign.UnknownCount,
        OperatorCaseCount = campaign.OperatorCaseCount
    };
}
