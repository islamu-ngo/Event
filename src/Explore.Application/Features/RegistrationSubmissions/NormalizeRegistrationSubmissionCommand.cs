// ABOUTME: Normalizes and validates one pinned native submission before atomically persisting answers or safe issues.
// ABOUTME: Uses the published form graph, Phase 7 condition evaluator, manual validation, and ciphertext-only sensitive storage.

using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.RegistrationSubmissions;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;
using FluentValidation;
using MediatR;

namespace Explore.Application.Features.RegistrationSubmissions.Commands;

public sealed record RegistrationSubmissionAnswerInput(
    Guid FieldId,
    RegistrationAnswerSubjectTypeEnum SubjectType,
    Guid SubjectId,
    Guid? TicketAssignmentOrderLineId,
    JsonElement Value);

public sealed record NormalizeRegistrationSubmissionCommand(
    Guid TenantId,
    Guid SubmissionId,
    IReadOnlyList<RegistrationSubmissionAnswerInput> Answers) : IRequest<RegistrationSubmissionNormalizationResult>;

public sealed record RegistrationSubmissionIssueDto(string Code, string? FieldKey);

public sealed record RegistrationSubmissionNormalizationResult(bool IsValid, int AnswerCount, int IssueCount)
{
    public IReadOnlyList<RegistrationSubmissionIssueDto> Issues { get; init; } = [];
}

public sealed record RegistrationSubmissionNormalizationDraft(
    bool IsValid,
    IReadOnlyCollection<RegistrationAnswer> Answers,
    IReadOnlyCollection<RegistrationConsentRecord> ConsentRecords,
    IReadOnlyCollection<RegistrationSubmissionIssue> Issues,
    IReadOnlyCollection<RegistrationRequirementFulfillment> Fulfillments,
    IReadOnlyList<RegistrationSubmissionIssueDto> SafeIssues,
    IReadOnlyList<NativeRegistrationAnswerSubjectDto> CompletedSubjects);

public sealed class NormalizeRegistrationSubmissionCommandValidator : AbstractValidator<NormalizeRegistrationSubmissionCommand>
{
    public NormalizeRegistrationSubmissionCommandValidator()
    {
        RuleFor(command => command.TenantId).NotEmpty();
        RuleFor(command => command.SubmissionId).NotEmpty();
        RuleFor(command => command.Answers).NotNull();
    }
}

public sealed class NormalizeRegistrationSubmissionCommandHandler(
    IRegistrationInventoryRepository inventoryRepository,
    IRegistrationSubmissionRepository submissionRepository,
    IRegistrationFormAuthoringRepository formRepository,
    IRegistrationParticipantRepository participantRepository,
    IRegistrationSensitiveValueProtector protector,
    ISender sender,
    TimeProvider timeProvider)
    : IRequestHandler<NormalizeRegistrationSubmissionCommand, RegistrationSubmissionNormalizationResult>
{
    public async Task<RegistrationSubmissionNormalizationResult> Handle(
        NormalizeRegistrationSubmissionCommand request,
        CancellationToken cancellationToken)
    {
        await new NormalizeRegistrationSubmissionCommandValidator().ValidateAndThrowAsync(request, cancellationToken);
        RegistrationSubmission submission = await submissionRepository.GetSubmissionAsync(
            request.TenantId, request.SubmissionId, cancellationToken)
            ?? throw new InvalidOperationException("Registration submission was not found.");
        RegistrationOrder order = await inventoryRepository.GetOrderWithLinesAsync(
            submission.RegistrationOrderId, submission.TenantId, cancellationToken)
            ?? throw new InvalidOperationException("Registration order was not found.");
        IReadOnlyList<RegistrationParticipant> participants = await participantRepository.GetParticipantsByOrderAsync(
            order.Id, submission.TenantId, cancellationToken);
        IReadOnlyList<RegistrationTicketAssignment> assignments = await participantRepository
            .GetAssignmentsWithParticipantsByOrderAsync(order.Id, submission.TenantId, cancellationToken);
        RegistrationSubmissionNormalizationDraft draft = await PrepareAsync(
            submission, request.Answers, order, participants, assignments, submissionRepository, formRepository,
            protector, timeProvider, cancellationToken);
        await submissionRepository.PersistNormalizationAsync(
            draft.Answers, draft.ConsentRecords, draft.Issues, cancellationToken);
        await RecordFulfillmentAsync(submission, draft.CompletedSubjects, draft.IsValid, sender, cancellationToken);
        return new(draft.IsValid, draft.Answers.Count, draft.Issues.Count) { Issues = draft.SafeIssues };
    }

    internal static async Task<RegistrationSubmissionNormalizationDraft> PrepareAsync(
        RegistrationSubmission submission,
        IReadOnlyList<RegistrationSubmissionAnswerInput> inputs,
        RegistrationOrder order,
        IReadOnlyList<RegistrationParticipant> participants,
        IReadOnlyList<RegistrationTicketAssignment> assignments,
        IRegistrationSubmissionRepository submissionRepository,
        IRegistrationFormAuthoringRepository formRepository,
        IRegistrationSensitiveValueProtector protector,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        RegistrationFormVersion version = await formRepository.GetVersionAsync(
            submission.EventId, submission.RegistrationFormId, submission.RegistrationFormVersionId, cancellationToken)
            ?? throw new InvalidOperationException("Pinned registration form version was not found.");
        RegistrationRequirement requirement = await submissionRepository.GetRequirementAsync(
            submission.TenantId, submission.RegistrationRequirementId, cancellationToken)
            ?? throw new InvalidOperationException("Pinned registration requirement was not found.");
        if (order.TenantId != submission.TenantId || order.EventId != submission.EventId ||
            order.Id != submission.RegistrationOrderId ||
            order.RegistrationWorkflowVersionId != submission.RegistrationWorkflowId)
        {
            throw new InvalidOperationException("Pinned registration order lineage is invalid.");
        }

        IReadOnlyList<NativeRegistrationAnswerSubjectDto> allowedSubjects = NativeRegistrationAttemptContractBuilder.Subjects(
            order, requirement, participants, assignments, []);
        HashSet<(RegistrationAnswerSubjectTypeEnum Type, Guid Id, Guid? LineId)> allowedSubjectKeys = allowedSubjects
            .Select(subject => (subject.SubjectType, subject.SubjectId, subject.TicketAssignmentOrderLineId))
            .ToHashSet();
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        Dictionary<Guid, RegistrationFormField> fields = version.Sections
            .SelectMany(section => section.Fields)
            .Where(field => !field.IsDeleted)
            .ToDictionary(field => field.Id);
        List<(RegistrationSubmissionAnswerInput Input, RegistrationFormField Field, NormalizedRegistrationValue Value)> normalized = [];
        List<RegistrationSubmissionIssue> issues = [];

        foreach (RegistrationSubmissionAnswerInput input in inputs)
        {
            if (!fields.TryGetValue(input.FieldId, out RegistrationFormField? field))
            {
                issues.Add(RegistrationSubmissionIssue.Create(submission, "UNKNOWN_FIELD", now));
                continue;
            }

            if (!allowedSubjectKeys.Contains((input.SubjectType, input.SubjectId, input.TicketAssignmentOrderLineId)))
            {
                issues.Add(RegistrationSubmissionIssue.Create(submission, "INVALID_SUBJECT", now, field.Id));
                continue;
            }

            if (input.SubjectType == RegistrationAnswerSubjectTypeEnum.Participant &&
                (RegistrationFieldTypeEnum)field.FieldTypeId == RegistrationFieldTypeEnum.Consent &&
                field.ConsentPurposeCode is { } purposeCode && ConsentPurposeCodes.IsMarketing(purposeCode) &&
                participants.Any(participant => participant.Id == input.SubjectId &&
                    participant.ParticipantTypeId is (int)ParticipantTypeEnum.Child or (int)ParticipantTypeEnum.Dependent))
            {
                issues.Add(RegistrationSubmissionIssue.Create(submission, "CHILD_MARKETING_CONSENT_DISABLED", now, field.Id));
                continue;
            }

            if (normalized.Any(item => item.Field.Id == input.FieldId &&
                    item.Input.SubjectType == input.SubjectType && item.Input.SubjectId == input.SubjectId))
            {
                issues.Add(RegistrationSubmissionIssue.Create(submission, "DUPLICATE_FIELD_VALUE", now, field.Id));
                continue;
            }

            RegistrationValueNormalizationResult result = RegistrationAnswerNormalizer.Normalize(ToSpec(field), input.Value);
            if (!result.IsValid || !OptionsBelongToField(field, result.Value!))
            {
                issues.Add(RegistrationSubmissionIssue.Create(submission, result.IssueCode ?? "INVALID_OPTION", now, field.Id));
                continue;
            }

            normalized.Add((input, field, result.Value!));
        }

        IReadOnlyList<NativeRegistrationAnswerSubjectDto> submittedSubjects = inputs.Count == 0 && allowedSubjects.Count == 1
            ? allowedSubjects
            : [.. allowedSubjects.Where(subject => inputs.Any(input =>
                input.SubjectType == subject.SubjectType &&
                input.SubjectId == subject.SubjectId &&
                input.TicketAssignmentOrderLineId == subject.TicketAssignmentOrderLineId))];
        foreach (NativeRegistrationAnswerSubjectDto subject in submittedSubjects)
        {
            var subjectAnswers = normalized.Where(item =>
                    item.Input.SubjectType == subject.SubjectType &&
                    item.Input.SubjectId == subject.SubjectId &&
                    item.Input.TicketAssignmentOrderLineId == subject.TicketAssignmentOrderLineId)
                .ToArray();
            Dictionary<FormFieldReference, FormAnswerValue> conditionAnswers = subjectAnswers.ToDictionary(
                item => new FormFieldReference(item.Field.Namespace, item.Field.Key),
                item => ToConditionValue(item.Value));
            HashSet<Guid> suppliedFields = subjectAnswers.Select(item => item.Field.Id).ToHashSet();
            foreach (RegistrationFormField field in fields.Values)
            {
                (bool visible, bool required) = ApplyRules(version, field, conditionAnswers);
                if (!visible && suppliedFields.Contains(field.Id))
                {
                    issues.Add(RegistrationSubmissionIssue.Create(submission, "HIDDEN_FIELD_SUPPLIED", now, field.Id));
                }
                else if (visible && required && !suppliedFields.Contains(field.Id))
                {
                    issues.Add(RegistrationSubmissionIssue.Create(submission, "REQUIRED_FIELD_MISSING", now, field.Id));
                }
            }
        }

        now = timeProvider.GetUtcNow().UtcDateTime;
        if (normalized.Any(item => item.Field.FieldTypeId != (int)RegistrationFieldTypeEnum.Consent) &&
            !AnonymousRegistrationRetentionPolicy.CanDisclose(order, null, now))
        {
            issues.Add(RegistrationSubmissionIssue.Create(submission, "ANONYMOUS_RETENTION_EXPIRED", now));
        }

        List<RegistrationAnswer> answers = [];
        List<RegistrationConsentRecord> consentRecords = [];
        if (issues.Count == 0)
        {
            foreach ((RegistrationSubmissionAnswerInput input, RegistrationFormField field, NormalizedRegistrationValue value) in normalized)
            {
                if ((RegistrationFieldTypeEnum)field.FieldTypeId == RegistrationFieldTypeEnum.Consent)
                {
                    consentRecords.Add(RegistrationConsentRecord.Grant(submission, requirement, version, field,
                        input.SubjectType, input.SubjectId, input.TicketAssignmentOrderLineId, now));
                    continue;
                }

                answers.AddRange(CreateAnswers(submission, requirement, field, input, value, protector, now, order.AnonymousPiiRetentionUntilUtc));
            }
        }

        if (issues.Count > 0)
        {
            answers.Clear();
            consentRecords.Clear();
        }

        IReadOnlyCollection<RegistrationRequirementFulfillment> fulfillments = issues.Count == 0 && submission.IsFinalizable
            ? [.. submittedSubjects.Select(subject => RegistrationRequirementFulfillment.CreateFulfilled(
                order, requirement, submission, subject.SubjectType, subject.SubjectId, now))]
            : [];

        return new(
            issues.Count == 0,
            answers,
            consentRecords,
            issues,
            fulfillments,
            issues.Select(issue => new RegistrationSubmissionIssueDto(
                issue.Code,
                issue.RegistrationFormFieldId is { } fieldId && fields.TryGetValue(fieldId, out RegistrationFormField? field)
                    ? field.Key
                    : null)).ToArray(),
            issues.Count == 0 ? submittedSubjects : []);
    }

    internal static async Task RecordFulfillmentAsync(
        RegistrationSubmission submission,
        IReadOnlyList<NativeRegistrationAnswerSubjectDto> completedSubjects,
        bool normalizationIsValid,
        ISender sender,
        CancellationToken cancellationToken)
    {
        if (normalizationIsValid && submission.IsFinalizable)
        {
            foreach (NativeRegistrationAnswerSubjectDto subject in completedSubjects)
            {
                await sender.Send(new RecordRegistrationRequirementFulfillmentCommand(
                    submission.TenantId,
                    submission.RegistrationOrderId,
                    submission.RegistrationRequirementId,
                    submission.Id,
                    subject.SubjectType,
                    subject.SubjectId,
                    false), cancellationToken);
            }
        }
    }

    private static RegistrationFieldNormalizationSpec ToSpec(RegistrationFormField field) => new(
        (RegistrationFieldTypeEnum)field.FieldTypeId, field.MinNumber, field.MaxNumber, field.MinLength,
        field.MaxLength, field.RegexPattern, field.MinDateTime, field.MaxDateTime, field.AllowedUrlSchemes);

    private static bool OptionsBelongToField(RegistrationFormField field, NormalizedRegistrationValue value) =>
        value.OptionIds is null || value.OptionIds.All(id => field.Options.Any(option => option.Id == id && !option.IsDeleted));

    private static FormAnswerValue ToConditionValue(NormalizedRegistrationValue value)
    {
        if (value.OptionIds is { Count: > 1 } ids)
        {
            return FormAnswerValue.From(ids.Select(id => (FormScalarValue)FormScalarValue.From(id.ToString("D"))).ToArray());
        }

        if (value.Boolean is { } boolean) return FormAnswerValue.From(boolean);
        if (value.DecimalValue is { } number) return FormAnswerValue.From(number);
        if (value.IntegerValue is { } integer) return FormAnswerValue.From((decimal)integer);
        if (value.Date is { } date) return FormAnswerValue.From(date);
        return FormAnswerValue.From(value.OptionIds is { Count: 1 } option ? option[0].ToString("D") : value.Text ?? value.Canonical);
    }

    private static (bool Visible, bool Required) ApplyRules(
        RegistrationFormVersion version,
        RegistrationFormField field,
        IReadOnlyDictionary<FormFieldReference, FormAnswerValue> answers)
    {
        bool visible = true;
        bool required = field.IsRequired;
        foreach (RegistrationFormRule rule in version.Rules.Where(rule => !rule.IsDeleted &&
                     rule.TargetNamespace == field.Namespace && rule.TargetKey == field.Key).OrderBy(rule => rule.Ordinal))
        {
            if (!FormConditionEvaluator.Evaluate(rule.Condition, answers)) continue;
            (visible, required) = rule.Effect switch
            {
                RegistrationFormRuleEffect.Show => (true, required),
                RegistrationFormRuleEffect.Hide => (false, required),
                RegistrationFormRuleEffect.Require => (visible, true),
                RegistrationFormRuleEffect.MakeOptional => (visible, false),
                _ => throw new InvalidOperationException("Unsupported registration form rule effect.")
            };
        }

        return (visible, required);
    }

    private static IEnumerable<RegistrationAnswer> CreateAnswers(
        RegistrationSubmission submission,
        RegistrationRequirement requirement,
        RegistrationFormField field,
        RegistrationSubmissionAnswerInput input,
        NormalizedRegistrationValue value,
        IRegistrationSensitiveValueProtector protector,
        DateTime now,
        DateTime? anonymousUpperBoundUtc)
    {
        bool sensitive = value.OptionIds is null &&
            field.OrganizerVisibilityId == (int)RegistrationOrganizerVisibilityEnum.Hidden;
        if (sensitive)
        {
            RegistrationProtectedValue protectedValue = protector.Protect(value.Canonical);
            RegistrationSensitiveAnswerValue sensitiveValue = RegistrationSensitiveAnswerValue.Create(
                submission.TenantId, protectedValue.Ciphertext, protectedValue.KeyVersion, field.RetentionPolicyId, now, anonymousUpperBoundUtc);
            yield return RegistrationAnswer.CreateSensitive(submission, field, requirement, input.SubjectType,
                input.SubjectId, 1, sensitiveValue, now, input.TicketAssignmentOrderLineId, anonymousUpperBoundUtc);
            yield break;
        }

        if (value.OptionIds is { } optionIds)
        {
            for (int index = 0; index < optionIds.Count; index++)
            {
                RegistrationFormFieldOption option = field.Options.Single(candidate => candidate.Id == optionIds[index]);
                yield return RegistrationAnswer.CreateOption(submission, field, requirement, input.SubjectType,
                    input.SubjectId, index + 1, option, now, input.TicketAssignmentOrderLineId, anonymousUpperBoundUtc);
            }
            yield break;
        }

        yield return value switch
        {
            { Text: not null } => RegistrationAnswer.CreateText(submission, field, requirement, input.SubjectType, input.SubjectId, 1, value.Text, now, input.TicketAssignmentOrderLineId, anonymousUpperBoundUtc),
            { IntegerValue: not null } => RegistrationAnswer.CreateInteger(submission, field, requirement, input.SubjectType, input.SubjectId, 1, value.IntegerValue.Value, now, input.TicketAssignmentOrderLineId, anonymousUpperBoundUtc),
            { DecimalValue: not null } => RegistrationAnswer.CreateDecimal(submission, field, requirement, input.SubjectType, input.SubjectId, 1, value.DecimalValue.Value, now, input.TicketAssignmentOrderLineId, anonymousUpperBoundUtc),
            { Boolean: not null } => RegistrationAnswer.CreateBoolean(submission, field, requirement, input.SubjectType, input.SubjectId, 1, value.Boolean.Value, now, input.TicketAssignmentOrderLineId, anonymousUpperBoundUtc),
            { Date: not null } => RegistrationAnswer.CreateDate(submission, field, requirement, input.SubjectType, input.SubjectId, 1, value.Date.Value, now, input.TicketAssignmentOrderLineId, anonymousUpperBoundUtc),
            { Time: not null } => RegistrationAnswer.CreateTime(submission, field, requirement, input.SubjectType, input.SubjectId, 1, value.Time.Value, now, input.TicketAssignmentOrderLineId, anonymousUpperBoundUtc),
            { Instant: not null } => RegistrationAnswer.CreateInstant(submission, field, requirement, input.SubjectType, input.SubjectId, 1, value.Instant.Value, now, input.TicketAssignmentOrderLineId, anonymousUpperBoundUtc),
            _ => throw new InvalidOperationException("Normalized registration value has no persistable type.")
        };
    }
}
