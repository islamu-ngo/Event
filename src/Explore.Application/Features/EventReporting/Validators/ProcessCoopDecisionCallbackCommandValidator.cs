using Explore.Application.DTOs.EventReporting;
using Explore.Application.Features.EventReporting.Policies;
using Explore.Application.Features.EventReporting.Requests.Commands;
using Explore.Domain;
using FluentValidation;

namespace Explore.Application.Features.EventReporting.Validators;

public sealed class ProcessCoopDecisionCallbackCommandValidator : AbstractValidator<ProcessCoopDecisionCallbackCommand>
{
    private const int MaxProviderIdLength = 200;
    private const int MaxProviderUrlLength = 500;
    private const int MaxProviderCodeLength = 100;
    private const int MaxItemsPerCallback = 25;

    public ProcessCoopDecisionCallbackCommandValidator()
    {
        RuleFor(command => command.Request).NotNull();

        When(command => command.Request is not null, () =>
        {
            RuleFor(command => ResolveTenantId(command.Request))
                .NotEmpty()
                .WithMessage("TenantId is required.");

            RuleFor(command => ResolveReportId(command.Request))
                .NotEmpty()
                .WithMessage("ReportId is required.");

            RuleFor(command => ResolveEventId(command.Request))
                .NotEmpty()
                .WithMessage("EventId is required.");

            RuleFor(command => ResolveCaseId(command.Request))
                .NotEmpty()
                .WithMessage("CaseId is required.");

            RuleFor(command => command.Request.Action)
                .NotNull()
                .WithMessage("Coop action metadata is required.");

            RuleFor(command => command.Request.Action!.Id)
                .NotEmpty()
                .MaximumLength(MaxProviderCodeLength)
                .When(command => command.Request.Action is not null);

            RuleFor(command => FirstNonBlank(command.Request.ProviderDecisionId, command.Request.ProviderDecisionIdSnake))
                .NotEmpty()
                .WithMessage("ProviderDecisionId is required.")
                .MaximumLength(MaxProviderIdLength);

            RuleFor(command => FirstNonBlank(command.Request.ProviderCaseId, command.Request.ProviderCaseIdSnake))
                .MaximumLength(MaxProviderIdLength)
                .When(command => !string.IsNullOrWhiteSpace(FirstNonBlank(command.Request.ProviderCaseId, command.Request.ProviderCaseIdSnake)));

            RuleFor(command => FirstNonBlank(command.Request.ProviderUrl, command.Request.ProviderUrlSnake))
                .MaximumLength(MaxProviderUrlLength)
                .When(command => !string.IsNullOrWhiteSpace(FirstNonBlank(command.Request.ProviderUrl, command.Request.ProviderUrlSnake)));

            RuleFor(command => FirstNonBlank(command.Request.ProviderTargetScope, command.Request.ProviderTargetScopeSnake))
                .Must(IsValidProviderTargetScope)
                .WithMessage("ProviderTargetScope must be instance or tenant.")
                .When(command => !string.IsNullOrWhiteSpace(FirstNonBlank(command.Request.ProviderTargetScope, command.Request.ProviderTargetScopeSnake)));

            RuleFor(command => FirstNonBlank(command.Request.ProviderTargetId, command.Request.ProviderTargetIdSnake))
                .MaximumLength(MaxProviderIdLength)
                .When(command => !string.IsNullOrWhiteSpace(FirstNonBlank(command.Request.ProviderTargetId, command.Request.ProviderTargetIdSnake)));

            RuleFor(command => FirstNonBlank(command.Request.CorrelationId, command.Request.CorrelationIdSnake))
                .MaximumLength(EventReportReasonCodePolicy.MaxCorrelationIdLength)
                .When(command => !string.IsNullOrWhiteSpace(FirstNonBlank(command.Request.CorrelationId, command.Request.CorrelationIdSnake)));

            RuleFor(command => FirstNonBlank(command.Request.ReasonCode, command.Request.ReasonCodeSnake))
                .MaximumLength(EventReportDecision.MaxReasonCodeLength)
                .When(command => !string.IsNullOrWhiteSpace(FirstNonBlank(command.Request.ReasonCode, command.Request.ReasonCodeSnake)));

            RuleFor(command => FirstNonBlank(command.Request.SafeNote, command.Request.SafeNoteSnake))
                .MaximumLength(EventReportDecision.MaxSafeNoteLength)
                .When(command => !string.IsNullOrWhiteSpace(FirstNonBlank(command.Request.SafeNote, command.Request.SafeNoteSnake)));

            RuleFor(command => command.Request.Policies)
                .Must(items => items.Count <= MaxItemsPerCallback)
                .WithMessage($"No more than {MaxItemsPerCallback} Coop policies can be accepted in one callback.");

            RuleFor(command => command.Request.Rules)
                .Must(items => items.Count <= MaxItemsPerCallback)
                .WithMessage($"No more than {MaxItemsPerCallback} Coop rules can be accepted in one callback.");

            RuleForEach(command => command.Request.Policies).SetValidator(new CoopPolicyValidator());
            RuleForEach(command => command.Request.Rules).SetValidator(new CoopRuleValidator());
        });
    }

    internal static Guid ResolveTenantId(CoopDecisionCallbackRequestDto request) =>
        FirstNonEmpty(request.TenantId, request.TenantIdSnake, request.Item?.TenantId, request.Item?.TenantIdSnake);

    internal static Guid ResolveReportId(CoopDecisionCallbackRequestDto request) =>
        FirstNonEmpty(request.ReportId, request.ReportIdSnake, request.Item?.ReportId, request.Item?.ReportIdSnake);

    internal static Guid ResolveEventId(CoopDecisionCallbackRequestDto request) =>
        FirstNonEmpty(request.EventId, request.EventIdSnake, request.Item?.EventId, request.Item?.EventIdSnake);

    internal static Guid ResolveCaseId(CoopDecisionCallbackRequestDto request) =>
        FirstNonEmpty(request.CaseId, request.CaseIdSnake, request.Item?.CaseId, request.Item?.CaseIdSnake);

    internal static Guid? ResolveExpectedCaseConcurrencyStamp(CoopDecisionCallbackRequestDto request) =>
        request.ExpectedCaseConcurrencyStamp ?? request.ExpectedCaseConcurrencyStampSnake;

    internal static Guid? ResolveDuplicateGroupId(CoopDecisionCallbackRequestDto request) =>
        request.DuplicateGroupId ?? request.DuplicateGroupIdSnake;

    internal static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static Guid FirstNonEmpty(params Guid?[] values) =>
        values.FirstOrDefault(value => value.HasValue && value.Value != Guid.Empty) ?? Guid.Empty;

    private static bool IsValidProviderTargetScope(string? value)
    {
        return string.Equals(value, "instance", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "tenant", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class CoopPolicyValidator : AbstractValidator<CoopDecisionCallbackPolicyDto>
    {
        public CoopPolicyValidator()
        {
            RuleFor(policy => policy.Id)
                .MaximumLength(MaxProviderCodeLength)
                .When(policy => !string.IsNullOrWhiteSpace(policy.Id));
            RuleFor(policy => policy.Name)
                .MaximumLength(MaxProviderCodeLength)
                .When(policy => !string.IsNullOrWhiteSpace(policy.Name));
        }
    }

    private sealed class CoopRuleValidator : AbstractValidator<CoopDecisionCallbackRuleDto>
    {
        public CoopRuleValidator()
        {
            RuleFor(rule => rule.Id)
                .MaximumLength(MaxProviderCodeLength)
                .When(rule => !string.IsNullOrWhiteSpace(rule.Id));
            RuleFor(rule => rule.Name)
                .MaximumLength(MaxProviderCodeLength)
                .When(rule => !string.IsNullOrWhiteSpace(rule.Name));
        }
    }
}
