using Explore.Application.Features.SupportAccess.Requests.Commands;
using Explore.Domain;
using FluentValidation;

namespace Explore.Application.Features.SupportAccess.Validators;

public sealed class StopSupportAccessSessionCommandValidator : AbstractValidator<StopSupportAccessSessionCommand>
{
    public StopSupportAccessSessionCommandValidator()
    {
        RuleFor(command => command.SessionId)
            .NotEmpty();

        RuleFor(command => command.EndReasonText)
            .MaximumLength(SupportAccessSession.MaxEndReasonTextLength);
    }
}
