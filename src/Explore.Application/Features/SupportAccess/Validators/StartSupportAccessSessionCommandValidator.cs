using Explore.Application.Features.SupportAccess.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Enums;
using FluentValidation;

namespace Explore.Application.Features.SupportAccess.Validators;

public sealed class StartSupportAccessSessionCommandValidator : AbstractValidator<StartSupportAccessSessionCommand>
{
    public StartSupportAccessSessionCommandValidator()
    {
        RuleFor(command => command.TargetTenantId)
            .NotEmpty();

        RuleFor(command => command.TargetTenantUserId)
            .Must(id => id is null || id.Value != Guid.Empty)
            .WithMessage("Target tenant user id must be non-empty when provided.");

        RuleFor(command => command.Mode)
            .Must(mode => Enum.IsDefined(mode))
            .WithMessage("Support-access mode is not valid.");

        RuleFor(command => command.DurationMinutes)
            .InclusiveBetween(1, SupportAccessSettingMaxDurationMinutes);

        RuleFor(command => command.ReasonCode)
            .NotEmpty()
            .MaximumLength(SupportAccessSession.MaxReasonCodeLength);

        RuleFor(command => command.ReasonText)
            .NotEmpty()
            .MaximumLength(SupportAccessSession.MaxReasonTextLength);

        RuleFor(command => command.TicketReference)
            .MaximumLength(SupportAccessSession.MaxTicketReferenceLength);
    }

    private const int SupportAccessSettingMaxDurationMinutes = 240;
}
