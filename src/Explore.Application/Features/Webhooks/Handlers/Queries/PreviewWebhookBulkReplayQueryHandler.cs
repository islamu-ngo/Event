using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.DTOs.Webhooks;
using Explore.Application.Features.Webhooks.Requests.Queries;
using Explore.Application.Features.Webhooks.Validators;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Webhooks.Handlers.Queries;

public sealed class PreviewWebhookBulkReplayQueryHandler(
    IWebhookBulkReplayRepository repository,
    IWebhookBulkReplayPolicyResolver policyResolver,
    TimeProvider timeProvider)
    : IRequestHandler<PreviewWebhookBulkReplayQuery, WebhookBulkReplayPreviewResult>
{
    public async Task<WebhookBulkReplayPreviewResult> Handle(
        PreviewWebhookBulkReplayQuery request,
        CancellationToken cancellationToken)
    {
        var validator = new PreviewWebhookBulkReplayQueryValidator();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return WebhookBulkReplayPreviewResult.Failed(
                "webhook_bulk_replay_preview_validation_failed",
                validation.Errors.Select(error => error.ErrorMessage));
        }

        var limits = policyResolver.Resolve();
        var policyErrors = ValidatePolicyLimits(request, limits);
        if (policyErrors.Count > 0)
        {
            return WebhookBulkReplayPreviewResult.Failed(
                "webhook_bulk_replay_limit_exceeded",
                policyErrors);
        }

        var filter = ToFilter(request);
        var previewedAt = timeProvider.GetUtcNow().UtcDateTime;
        var preview = await repository.PreviewAsync(
            request.TenantId,
            filter,
            previewedAt,
            cancellationToken);

        return WebhookBulkReplayPreviewResult.Succeeded(new WebhookBulkReplayPreviewDto
        {
            Filter = new WebhookBulkReplayFilterDto
            {
                FromUtc = filter.FromUtc,
                ToUtc = filter.ToUtc,
                WebhookConsumerId = filter.WebhookConsumerId,
                WebhookEndpointId = filter.WebhookEndpointId,
                EventType = filter.EventType,
                MaxItems = request.MaxItems
            },
            EligibleCount = preview.EligibleCount,
            EstimatedSelectedCount = Math.Min(preview.EligibleCount, request.MaxItems),
            ExcludedCount = preview.TotalExcludedCount,
            ExcludedHeldCount = preview.HeldCount,
            ExcludedPayloadUnavailableCount = preview.PayloadUnavailableCount,
            ExcludedEndpointUnavailableCount = preview.EndpointUnavailableCount,
            ExcludedIneligibleLocalStateCount = preview.IneligibleLocalStateCount,
            ExcludedProviderConflictCount = preview.ProviderConflictCount,
            ExcludedProviderUnknownCount = preview.ProviderUnknownCount,
            ExcludedProviderManualReconciliationCount = preview.ProviderManualReconciliationCount,
            ExcludedProviderIneligibleCount = preview.ProviderIneligibleCount,
            MaximumItemsPerOperation = limits.MaximumItemsPerOperation,
            MaximumReservedItemsPerTenant = limits.MaximumReservedItemsPerTenant,
            PreviewedAt = previewedAt
        });
    }

    internal static WebhookBulkReplayFilter ToFilter(PreviewWebhookBulkReplayQuery request) =>
        new(
            request.FromUtc,
            request.ToUtc,
            request.WebhookConsumerId,
            request.WebhookEndpointId,
            string.IsNullOrWhiteSpace(request.EventType) ? null : request.EventType.Trim());

    internal static IReadOnlyList<string> ValidatePolicyLimits(
        PreviewWebhookBulkReplayQuery request,
        WebhookBulkReplayLimits limits)
    {
        var errors = new List<string>();
        if (request.MaxItems > limits.MaximumItemsPerOperation)
        {
            errors.Add($"MaxItems cannot exceed {limits.MaximumItemsPerOperation}.");
        }

        if (request.ToUtc - request.FromUtc > TimeSpan.FromDays(limits.MaximumFilterWindowDays))
        {
            errors.Add($"Replay filter window cannot exceed {limits.MaximumFilterWindowDays} days.");
        }

        return errors;
    }
}
