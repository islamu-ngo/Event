using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.RegistrationOrders.Requests.Queries;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;

namespace Explore.Application.Features.RegistrationOrders.Handlers.Queries;

public sealed class GetRefundCampaignsQueryHandler(IRefundCampaignRepository campaigns, ITenantContext tenant)
    : IQueryHandler<GetRefundCampaignsQuery, IReadOnlyList<RefundCampaignDto>>
{
    public async Task<IReadOnlyList<RefundCampaignDto>> QueryAsync(
        GetRefundCampaignsQuery query,
        CancellationToken cancellationToken = default) =>
        (await campaigns.GetByEventAsync(tenant.TenantId, query.EventId, cancellationToken))
        .Select(RefundCampaignMapper.Map)
        .ToArray();
}

public sealed class GetRefundCampaignQueryHandler(IRefundCampaignRepository campaigns, ITenantContext tenant)
    : IQueryHandler<GetRefundCampaignQuery, RefundCampaignDto?>
{
    public async Task<RefundCampaignDto?> QueryAsync(GetRefundCampaignQuery query, CancellationToken cancellationToken = default)
    {
        RefundCampaign? campaign = await campaigns.GetByIdAsync(tenant.TenantId, query.CampaignId, cancellationToken);
        return campaign?.EventId == query.EventId ? RefundCampaignMapper.Map(campaign) : null;
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
