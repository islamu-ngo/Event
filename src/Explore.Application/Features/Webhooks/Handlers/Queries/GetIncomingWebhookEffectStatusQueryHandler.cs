using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Webhooks;
using Explore.Application.Features.Webhooks.Requests.Queries;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Webhooks.Handlers.Queries;

public sealed class GetIncomingWebhookEffectStatusQueryHandler(
    IIncomingWebhookEffectOutboxRepository repository)
    : IRequestHandler<GetIncomingWebhookEffectStatusQuery, BaseCommandResponse<IReadOnlyList<IncomingWebhookEffectStatusDto>>>
{
    private const int MaxLimit = 200;

    public async Task<BaseCommandResponse<IReadOnlyList<IncomingWebhookEffectStatusDto>>> Handle(
        GetIncomingWebhookEffectStatusQuery request,
        CancellationToken cancellationToken)
    {
        if (request.TenantId == Guid.Empty || request.Limit is < 1 or > MaxLimit)
        {
            return BaseCommandResponse.Validation<IReadOnlyList<IncomingWebhookEffectStatusDto>>(
                ["TenantId and a limit between 1 and 200 are required."],
                "TenantId and a limit between 1 and 200 are required.");
        }

        var rows = await repository.GetStatusRowsAsync(
            request.TenantId,
            request.Limit,
            cancellationToken);
        var statuses = rows.Select(row => new IncomingWebhookEffectStatusDto
        {
            EffectOutboxId = row.Id,
            TenantId = row.TenantId,
            IncomingWebhookMessageId = row.IncomingWebhookMessageId,
            EffectKind = row.EffectKind,
            Status = row.Status.ToString(),
            ProcessingGeneration = row.ProcessingGeneration,
            ProcessingFence = row.ProcessingFence,
            AttemptCount = row.AttemptCount,
            NextAttemptAt = row.NextAttemptAt,
            LeaseExpiresAt = row.ProcessingLeaseExpiresAt,
            CompletedAt = row.CompletedAt,
            DeadLetteredAt = row.DeadLetteredAt,
            FailureCategory = row.FailureCategory,
            SafeDetail = row.SafeDetail
        }).ToArray();

        return BaseCommandResponse.Success<IReadOnlyList<IncomingWebhookEffectStatusDto>>(
            statuses,
            "Incoming Coop effect status retrieved.");
    }
}
