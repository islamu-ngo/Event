using Explore.Application.Features.SupportAccess.Requests.Commands;
using Explore.Domain;
using FluentValidation;

namespace Explore.Application.Features.SupportAccess.Validators;

public sealed class ForceStopSupportAccessSessionCommandValidator : AbstractValidator<ForceStopSupportAccessSessionCommand>
{
    public ForceStopSupportAccessSessionCommandValidator()
    {
        RuleFor(command => command.SessionId)
            .NotEmpty();

        RuleFor(command => command.EndReasonText)
            .MaximumLength(SupportAccessSession.MaxEndReasonTextLength);
    }
}
