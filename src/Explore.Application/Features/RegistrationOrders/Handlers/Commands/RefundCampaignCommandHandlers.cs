using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.RegistrationOrders.Handlers.Queries;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Services.Registration;
using Explore.Domain;

namespace Explore.Application.Features.RegistrationOrders.Handlers.Commands;

public sealed class ResumeRefundCampaignCommandHandler(
    IRefundCampaignRepository campaigns,
    ITenantContext tenant,
    TimeProvider timeProvider)
    : ICommandHandler<ResumeRefundCampaignCommand, RefundCampaignDto?>
{
    public async Task<RefundCampaignDto?> ExecuteAsync(ResumeRefundCampaignCommand command, CancellationToken cancellationToken = default)
    {
        RefundCampaign? campaign = await campaigns.GetByIdAsync(tenant.TenantId, command.CampaignId, cancellationToken);
        if (campaign?.EventId != command.EventId)
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
