using Explore.Application.DTOs.RegistrationForms;
using Explore.Application.Configuration;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;

namespace Explore.Application.Services.Registration;

public sealed class RegistrationFormPublishPreflightService
{
    private readonly RegistrationFileAnswerOptions _fileAnswers;

    public RegistrationFormPublishPreflightService() : this(new RegistrationFileAnswerOptions())
    {
    }

    public RegistrationFormPublishPreflightService(RegistrationFileAnswerOptions fileAnswers)
    {
        _fileAnswers = fileAnswers;
    }

    public RegistrationFormPublishPreflightDto Check(RegistrationFormVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        List<RegistrationFormPublishPreflightIssueDto> issues = [];
        RegistrationFormField[] fields =
        [
            .. version.Sections.Where(section => !section.IsDeleted).OrderBy(section => section.Ordinal)
                .SelectMany(section => section.Fields.Where(field => !field.IsDeleted).OrderBy(field => field.Ordinal))
        ];

        if (fields.Length == 0)
        {
            issues.Add(new("form.empty", "A published form requires at least one active field."));
        }

        Dictionary<FormFieldReference, int> indexes = [];
        for (int index = 0; index < fields.Length; index++)
        {
            RegistrationFormField field = fields[index];
            FormFieldReference reference = new(field.Namespace, field.Key);
            if (!indexes.TryAdd(reference, index))
            {
                issues.Add(new("field.duplicate_identity", "Active field identities must be unique.", field.Id));
            }

            if ((RegistrationFieldTypeEnum)field.FieldTypeId is RegistrationFieldTypeEnum.SingleChoice or
                RegistrationFieldTypeEnum.MultipleChoice && !field.Options.Any(option => !option.IsDeleted && option.RetiredAt is null))
            {
                issues.Add(new("field.options_incomplete", "Choice fields require at least one active option.", field.Id));
            }

            if (field.RequiresExplicitConsent &&
                (string.IsNullOrWhiteSpace(field.ConsentPurposeCode) || string.IsNullOrWhiteSpace(field.ConsentText) ||
                 string.IsNullOrWhiteSpace(field.ConsentTextVersion)))
            {
                issues.Add(new("field.consent_incomplete", "Consent fields require a purpose code, text, and text version.", field.Id));
            }

            if ((RegistrationFieldTypeEnum)field.FieldTypeId == RegistrationFieldTypeEnum.File && !_fileAnswers.Enabled)
            {
                issues.Add(new("field.file_pipeline_disabled",
                    "File fields require the deployment file-answer pipeline to be enabled.", field.Id));
            }
        }

        foreach (RegistrationFormRule rule in version.Rules.Where(rule => !rule.IsDeleted))
        {
            if (!indexes.TryGetValue(rule.Target, out int targetIndex))
            {
                issues.Add(new("rule.target_unresolved", "Rule target does not resolve to an active field.", RuleId: rule.Id));
                continue;
            }

            foreach (FormFieldReference reference in FormConditionEvaluator.References(rule.Condition))
            {
                if (!indexes.TryGetValue(reference, out int referenceIndex))
                {
                    issues.Add(new("rule.reference_unresolved", "Rule condition references an unknown field.", RuleId: rule.Id));
                }
                else if (referenceIndex >= targetIndex)
                {
                    issues.Add(new("rule.reference_forward", "Rule conditions may reference only earlier fields.", RuleId: rule.Id));
                }
            }
        }

        return new(issues.Count == 0, issues);
    }
}
