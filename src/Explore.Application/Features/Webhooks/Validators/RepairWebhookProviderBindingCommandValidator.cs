using Explore.Application.Features.Webhooks.Requests.Commands;
using FluentValidation;

namespace Explore.Application.Features.Webhooks.Validators;

public sealed class RepairWebhookProviderBindingCommandValidator
    : AbstractValidator<RepairWebhookProviderBindingCommand>
{
    public const int MaxExternalApplicationIdLength = 500;
    public const int MaxReasonCodeLength = 64;

    public RepairWebhookProviderBindingCommandValidator()
    {
        RuleFor(command => command.ConsumerId)
            .NotEmpty()
            .WithMessage("ConsumerId is required.");

        RuleFor(command => command.ExternalApplicationId)
            .NotEmpty()
            .MaximumLength(MaxExternalApplicationIdLength)
            .WithMessage($"ExternalApplicationId is required and cannot exceed {MaxExternalApplicationIdLength} characters.");

        RuleFor(command => command.ReasonCode)
            .NotEmpty()
            .MaximumLength(MaxReasonCodeLength)
            .Must(IsSafeReasonCode)
            .WithMessage(
                $"ReasonCode is required, cannot exceed {MaxReasonCodeLength} characters, and may contain only ASCII letters, digits, '.', '_' or '-'.");
    }

    private static bool IsSafeReasonCode(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');
}
