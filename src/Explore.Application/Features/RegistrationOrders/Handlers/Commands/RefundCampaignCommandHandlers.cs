using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.RegistrationOrders.Handlers.Queries;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Services.Registration;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.RegistrationOrders.Handlers.Commands;

public sealed class ResumeRefundCampaignCommandHandler(
    IRefundCampaignRepository campaigns,
    ITenantContext tenant,
    TimeProvider timeProvider)
    : IRequestHandler<ResumeRefundCampaignCommand, RefundCampaignDto?>
{
    public async Task<RefundCampaignDto?> Handle(ResumeRefundCampaignCommand request, CancellationToken cancellationToken)
    {
        RefundCampaign? campaign = await campaigns.GetByIdAsync(tenant.TenantId, request.CampaignId, cancellationToken);
        if (campaign?.EventId != request.EventId)
        {
            return null;
        }

        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        bool resumed = await campaigns.ResumeAsync(
            tenant.TenantId,
            campaign.Id,
            RefundOutboxMessageFactory.CreateCampaignProcess(campaign, now),
            now,
            cancellationToken);
        return resumed
            ? RefundCampaignMapper.Map((await campaigns.GetByIdAsync(
                tenant.TenantId, campaign.Id, cancellationToken))!)
            : null;
    }
}
