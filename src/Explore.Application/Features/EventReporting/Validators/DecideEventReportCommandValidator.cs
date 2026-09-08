using Explore.Application.Features.EventReporting.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Enums;
using FluentValidation;

namespace Explore.Application.Features.EventReporting.Validators;

public sealed class DecideEventReportCommandValidator : AbstractValidator<DecideEventReportCommand>
{
    public DecideEventReportCommandValidator()
    {
        RuleFor(command => command.EventId).NotEmpty();
        RuleFor(command => command.ReportId).NotEmpty();
        RuleFor(command => command.CaseId).NotEmpty();
        RuleFor(command => command.ExpectedCaseConcurrencyStamp).NotEmpty();

        RuleFor(command => command.DecisionKind)
            .IsInEnum();

        RuleFor(command => command.ReasonCode)
            .NotEmpty()
            .MaximumLength(EventReportDecision.MaxReasonCodeLength);

        RuleFor(command => command.SafeNote)
            .MaximumLength(EventReportDecision.MaxSafeNoteLength)
            .When(command => !string.IsNullOrWhiteSpace(command.SafeNote));

        When(command => command.DecisionKind == EventReportDecisionKind.Duplicate, () =>
        {
            RuleFor(command => command.DuplicateGroupId)
                .NotEmpty()
                .WithMessage("DuplicateGroupId is required for duplicate report decisions.");
        });

        When(command => command.DecisionKind != EventReportDecisionKind.Duplicate, () =>
        {
            RuleFor(command => command.DuplicateGroupId)
                .Must(value => value is null || value.Value != Guid.Empty)
                .WithMessage("DuplicateGroupId cannot be empty.");
        });
    }
}
