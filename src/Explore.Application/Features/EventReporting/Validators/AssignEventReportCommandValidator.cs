using Explore.Application.Features.EventReporting.Requests.Commands;
using FluentValidation;

namespace Explore.Application.Features.EventReporting.Validators;

public sealed class AssignEventReportCommandValidator : AbstractValidator<AssignEventReportCommand>
{
    public AssignEventReportCommandValidator()
    {
        RuleFor(command => command.EventId).NotEmpty();
        RuleFor(command => command.ReportId).NotEmpty();
        RuleFor(command => command.CaseId).NotEmpty();
        RuleFor(command => command.AssigneeUserId).NotEmpty();
        RuleFor(command => command.ExpectedCaseConcurrencyStamp).NotEmpty();
    }
}
