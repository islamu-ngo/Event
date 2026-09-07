using Explore.Application.Features.Webhooks.Requests.Commands;
using Explore.Domain;
using FluentValidation;

namespace Explore.Application.Features.Webhooks.Validators;

public sealed class RedriveIncomingWebhookCommandValidator : AbstractValidator<RedriveIncomingWebhookCommand>
{
    public RedriveIncomingWebhookCommandValidator()
    {
        RuleFor(command => command.TenantId)
            .NotEmpty()
            .WithMessage("TenantId is required.");

        RuleFor(command => command.IncomingWebhookMessageId)
            .NotEmpty()
            .WithMessage("IncomingWebhookMessageId is required.");

        RuleFor(command => command.ExpectedProcessingGeneration)
            .GreaterThan(0)
            .WithMessage("ExpectedProcessingGeneration must be greater than zero.");

        RuleFor(command => command.Reason)
            .NotEmpty()
            .MaximumLength(IncomingWebhookRedriveRecord.MaxReasonLength)
            .WithMessage($"Reason is required and cannot exceed {IncomingWebhookRedriveRecord.MaxReasonLength} characters.");
    }
}
