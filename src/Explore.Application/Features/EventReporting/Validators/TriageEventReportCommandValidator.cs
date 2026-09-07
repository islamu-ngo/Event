using Explore.Application.Features.EventReporting.Requests.Commands;
using Explore.Domain;
using FluentValidation;

namespace Explore.Application.Features.EventReporting.Validators;

public sealed class TriageEventReportCommandValidator : AbstractValidator<TriageEventReportCommand>
{
    public TriageEventReportCommandValidator()
    {
        RuleFor(command => command.EventId).NotEmpty();
        RuleFor(command => command.ReportId).NotEmpty();
        RuleFor(command => command.CaseId).NotEmpty();
        RuleFor(command => command.ExpectedCaseConcurrencyStamp).NotEmpty();

        RuleFor(command => command.QueueCode)
            .NotEmpty()
            .MaximumLength(EventReportCase.MaxQueueCodeLength);

        RuleFor(command => command.Priority)
            .IsInEnum();
    }
}
