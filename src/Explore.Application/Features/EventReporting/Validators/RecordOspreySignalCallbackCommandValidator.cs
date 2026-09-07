using Explore.Application.DTOs.EventReporting;
using Explore.Application.Features.EventReporting.Requests.Commands;
using FluentValidation;

namespace Explore.Application.Features.EventReporting.Validators;

public sealed class RecordOspreySignalCallbackCommandValidator : AbstractValidator<RecordOspreySignalCallbackCommand>
{
    private const int MaxSignalsPerCallback = 50;
    private const int MaxSignalTypeLength = 100;
    private const int MaxPolicyCodeLength = 100;
    private const int MaxProviderCodeLength = 100;
    private const int MaxSafeSummaryLength = 500;
    private const int MaxExternalSignalIdLength = 200;
    private const int MaxCorrelationIdLength = 100;
    private const int MaxProviderTargetIdLength = 200;

    public RecordOspreySignalCallbackCommandValidator()
    {
        RuleFor(command => command.Request).NotNull();
        RuleFor(command => command.Request.TenantId).NotEmpty();
        RuleFor(command => command.Request.ReportId).NotEmpty();
        RuleFor(command => command.Request.EventId).NotEmpty();
        RuleFor(command => command.Request.CaseId)
            .Must(value => value is null || value.Value != Guid.Empty)
            .WithMessage("CaseId cannot be empty.");
        RuleFor(command => command.Request.ProviderSignalId)
            .MaximumLength(MaxExternalSignalIdLength)
            .When(command => !string.IsNullOrWhiteSpace(command.Request.ProviderSignalId));
        RuleFor(command => command.Request.CorrelationId)
            .MaximumLength(MaxCorrelationIdLength)
            .When(command => !string.IsNullOrWhiteSpace(command.Request.CorrelationId));
        RuleFor(command => command.Request.ProviderTargetScope)
            .Must(IsValidProviderTargetScope)
            .WithMessage("ProviderTargetScope must be instance or tenant.")
            .When(command => !string.IsNullOrWhiteSpace(command.Request.ProviderTargetScope));
        RuleFor(command => command.Request.ProviderTargetId)
            .MaximumLength(MaxProviderTargetIdLength)
            .When(command => !string.IsNullOrWhiteSpace(command.Request.ProviderTargetId));

        RuleFor(command => command.Request.Signals)
            .NotEmpty()
            .WithMessage("At least one Osprey signal is required.")
            .Must(signals => signals.Count <= MaxSignalsPerCallback)
            .WithMessage($"No more than {MaxSignalsPerCallback} Osprey signals can be accepted in one callback.");

        RuleForEach(command => command.Request.Signals).SetValidator(new OspreySignalCallbackItemDtoValidator());
    }

    private sealed class OspreySignalCallbackItemDtoValidator : AbstractValidator<OspreySignalCallbackItemDto>
    {
        public OspreySignalCallbackItemDtoValidator()
        {
            RuleFor(signal => signal.SignalType)
                .NotEmpty()
                .MaximumLength(MaxSignalTypeLength);
            RuleFor(signal => signal.PolicyCode)
                .NotEmpty()
                .MaximumLength(MaxPolicyCodeLength);
            RuleFor(signal => signal.Score)
                .InclusiveBetween(0m, 1m)
                .When(signal => signal.Score.HasValue);
            RuleFor(signal => signal.Verdict)
                .MaximumLength(MaxProviderCodeLength)
                .When(signal => !string.IsNullOrWhiteSpace(signal.Verdict));
            RuleFor(signal => signal.RecommendedAction)
                .MaximumLength(MaxProviderCodeLength)
                .When(signal => !string.IsNullOrWhiteSpace(signal.RecommendedAction));
            RuleFor(signal => signal.SafeSummary)
                .MaximumLength(MaxSafeSummaryLength)
                .When(signal => !string.IsNullOrWhiteSpace(signal.SafeSummary));
            RuleFor(signal => signal.ExternalSignalId)
                .MaximumLength(MaxExternalSignalIdLength)
                .When(signal => !string.IsNullOrWhiteSpace(signal.ExternalSignalId));
            RuleFor(signal => signal.CorrelationId)
                .MaximumLength(MaxCorrelationIdLength)
                .When(signal => !string.IsNullOrWhiteSpace(signal.CorrelationId));
        }
    }

    private static bool IsValidProviderTargetScope(string? value)
    {
        return string.Equals(value, "instance", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "tenant", StringComparison.OrdinalIgnoreCase);
    }
}
