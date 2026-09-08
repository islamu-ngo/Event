using Explore.Application.Features.EventReporting.Policies;
using Explore.Application.Features.EventReporting.Requests.Commands;
using FluentValidation;

namespace Explore.Application.Features.EventReporting.Validators;

public sealed class SubmitEventReportCommandValidator : AbstractValidator<SubmitEventReportCommand>
{
    public SubmitEventReportCommandValidator(EventReportSubmissionOptions options)
    {
        RuleFor(command => command.Request).NotNull();

        When(command => command.Request is not null, () =>
        {
            RuleFor(command => command.Request.EventId)
                .NotEmpty();

            RuleFor(command => command.Request.ReasonCode)
                .Must(reasonCode => EventReportReasonCodePolicy.TryNormalize(reasonCode, out _, out _))
                .WithMessage("ReasonCode must be one of the supported event report reason codes.");

            RuleFor(command => command.Request.SubcategoryCode)
                .MaximumLength(EventReportReasonCodePolicy.MaxSubcategoryCodeLength)
                .When(command => !string.IsNullOrWhiteSpace(command.Request.SubcategoryCode));

            RuleFor(command => command)
                .Must(command =>
                    command.SubmissionChannel
                        != EventReportSubmissionChannel.General
                    || !EventReportReasonCodePolicy.IsReservedRemedySubcategory(
                        command.Request.SubcategoryCode))
                .WithMessage(
                    "Reserved remedy subcategories require their dedicated submission route.");

            RuleFor(command => command.Request.ReporterText)
                .NotEmpty()
                .MaximumLength(Math.Max(1, options.MaxReporterTextLength));

            RuleFor(command => command.Request.ReporterLocale)
                .MaximumLength(10)
                .When(command => !string.IsNullOrWhiteSpace(command.Request.ReporterLocale));
        });

        RuleFor(command => command.ReporterIpHash)
            .MaximumLength(64)
            .When(command => !string.IsNullOrWhiteSpace(command.ReporterIpHash));

        RuleFor(command => command.ReporterUserAgentHash)
            .MaximumLength(64)
            .When(command => !string.IsNullOrWhiteSpace(command.ReporterUserAgentHash));

        RuleFor(command => command.CorrelationId)
            .MaximumLength(EventReportReasonCodePolicy.MaxCorrelationIdLength)
            .When(command => !string.IsNullOrWhiteSpace(command.CorrelationId));
    }
}
