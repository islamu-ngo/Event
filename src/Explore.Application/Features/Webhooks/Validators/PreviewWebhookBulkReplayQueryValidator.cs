using Explore.Application.Features.Webhooks.Requests.Queries;
using Explore.Domain;
using FluentValidation;

namespace Explore.Application.Features.Webhooks.Validators;

public sealed class PreviewWebhookBulkReplayQueryValidator : AbstractValidator<PreviewWebhookBulkReplayQuery>
{
    public PreviewWebhookBulkReplayQueryValidator()
    {
        RuleFor(query => query.TenantId).NotEmpty();
        RuleFor(query => query.FromUtc).Must(BeUtc).WithMessage("FromUtc must use UTC kind.");
        RuleFor(query => query.ToUtc).Must(BeUtc).WithMessage("ToUtc must use UTC kind.");
        RuleFor(query => query.ToUtc).GreaterThan(query => query.FromUtc);
        RuleFor(query => query.WebhookConsumerId)
            .Must(value => value is null || value != Guid.Empty)
            .WithMessage("WebhookConsumerId cannot be empty when supplied.");
        RuleFor(query => query.WebhookEndpointId)
            .Must(value => value is null || value != Guid.Empty)
            .WithMessage("WebhookEndpointId cannot be empty when supplied.");
        RuleFor(query => query.EventType)
            .MaximumLength(WebhookMessage.MaxEventTypeLength)
            .When(query => !string.IsNullOrWhiteSpace(query.EventType));
        RuleFor(query => query.MaxItems).InclusiveBetween(1, WebhookBulkReplayOperation.HardMaximumItems);
    }

    private static bool BeUtc(DateTime value) => value.Kind == DateTimeKind.Utc;
}
