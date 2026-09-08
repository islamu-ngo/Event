using Explore.Application.Authorization;
using Explore.Application.Features.RegistrationForms.Requests.Commands;
using Explore.Application.Features.RegistrationForms.Requests.Queries;
using FluentValidation;

namespace Explore.Application.Features.RegistrationForms.Validators;

public sealed class RegistrationFormAuthoringCommandValidator<TCommand> : AbstractValidator<TCommand>
    where TCommand : IRegistrationFormAuthoringCommand
{
    public RegistrationFormAuthoringCommandValidator()
    {
        RuleFor(value => value.EventId).NotEmpty();
        RuleFor(value => value).Must(HasValidIdentifiers)
            .WithMessage("Registration authoring identifiers and expected concurrency stamp are required.");
        RuleFor(value => value).Must(HasRequiredContent)
            .WithMessage("Registration authoring content is incomplete.");
    }

    private static bool HasValidIdentifiers(TCommand value) => value switch
    {
        CreateRegistrationWorkflowCommand command => command.ExpectedConcurrencyStamp != Guid.Empty,
        UpdateRegistrationWorkflowCommand command => Valid(command.WorkflowId, command.ExpectedConcurrencyStamp),
        CreateRegistrationRequirementCommand command => Valid(command.WorkflowId, command.ExpectedConcurrencyStamp),
        UpdateRegistrationRequirementCommand command => Valid(command.WorkflowId, command.RequirementId,
            command.ExpectedConcurrencyStamp),
        DeleteRegistrationRequirementCommand command => Valid(command.WorkflowId, command.RequirementId,
            command.ExpectedConcurrencyStamp),
        CreateRegistrationFormCommand command => Valid(command.WorkflowId, command.ExpectedConcurrencyStamp),
        CreateRegistrationFormVersionCommand command => Valid(command.FormId, command.ExpectedConcurrencyStamp) &&
            command.CloneFromVersionId != Guid.Empty,
        AddRegistrationFormSectionCommand command => Valid(command.FormId, command.VersionId,
            command.ExpectedConcurrencyStamp),
        UpdateRegistrationFormSectionCommand command => Valid(command.FormId, command.VersionId, command.SectionId,
            command.ExpectedConcurrencyStamp),
        ReorderRegistrationFormSectionsCommand command => Valid(command.FormId, command.VersionId,
            command.ExpectedConcurrencyStamp),
        DeleteRegistrationFormSectionCommand command => Valid(command.FormId, command.VersionId, command.SectionId,
            command.ExpectedConcurrencyStamp),
        AddRegistrationFormFieldCommand command => Valid(command.FormId, command.VersionId, command.SectionId,
            command.ExpectedConcurrencyStamp),
        UpdateRegistrationFormFieldCommand command => Valid(command.FormId, command.VersionId, command.SectionId,
            command.FieldId, command.ExpectedConcurrencyStamp),
        ReorderRegistrationFormFieldsCommand command => Valid(command.FormId, command.VersionId, command.SectionId,
            command.ExpectedConcurrencyStamp),
        DeleteRegistrationFormFieldCommand command => Valid(command.FormId, command.VersionId, command.SectionId,
            command.FieldId, command.ExpectedConcurrencyStamp),
        AddRegistrationFormFieldOptionCommand command => Valid(command.FormId, command.VersionId, command.SectionId,
            command.FieldId, command.ExpectedConcurrencyStamp),
        UpdateRegistrationFormFieldOptionCommand command => Valid(command.FormId, command.VersionId, command.SectionId,
            command.FieldId, command.OptionId, command.ExpectedConcurrencyStamp),
        RetireRegistrationFormFieldOptionCommand command => Valid(command.FormId, command.VersionId, command.SectionId,
            command.FieldId, command.OptionId, command.ExpectedConcurrencyStamp),
        AddRegistrationFormRuleCommand command => Valid(command.FormId, command.VersionId,
            command.ExpectedConcurrencyStamp),
        UpdateRegistrationFormRuleCommand command => Valid(command.FormId, command.VersionId, command.RuleId,
            command.ExpectedConcurrencyStamp),
        DeleteRegistrationFormRuleCommand command => Valid(command.FormId, command.VersionId, command.RuleId,
            command.ExpectedConcurrencyStamp),
        PublishRegistrationFormVersionCommand command => Valid(command.FormId, command.VersionId,
            command.ExpectedConcurrencyStamp),
        _ => false
    };

    private static bool HasRequiredContent(TCommand value) => value switch
    {
        CreateRegistrationWorkflowCommand command => Present(command.Purpose),
        UpdateRegistrationWorkflowCommand command => Present(command.Purpose),
        CreateRegistrationFormCommand command => Present(command.Namespace, command.Key, command.Name, command.LanguageTag),
        CreateRegistrationFormVersionCommand command => command.CloneFromVersionId.HasValue || Present(command.LanguageTag),
        AddRegistrationFormSectionCommand command => command.Ordinal > 0 && Present(command.Title),
        UpdateRegistrationFormSectionCommand command => command.Ordinal > 0 && Present(command.Title),
        ReorderRegistrationFormSectionsCommand command => ValidOrder(command.OrderedIds),
        AddRegistrationFormFieldCommand command => command.Ordinal > 0 && command.FieldTypeId > 0 &&
            command.RetentionPolicyId > 0 && command.OrganizerVisibilityId > 0 &&
            Present(command.Namespace, command.Key, command.Label) &&
            ValidExport(command.IsExportable, command.ExportPurposeCode) &&
            ValidConsent(command.RequiresExplicitConsent, command.ConsentPurposeCode, command.ConsentTextVersion,
                command.ConsentText),
        UpdateRegistrationFormFieldCommand command => command.Ordinal > 0 && command.RetentionPolicyId > 0 &&
            command.OrganizerVisibilityId > 0 && Present(command.Label) &&
            ValidExport(command.IsExportable, command.ExportPurposeCode) &&
            ValidConsent(command.RequiresExplicitConsent, command.ConsentPurposeCode, command.ConsentTextVersion,
                command.ConsentText),
        ReorderRegistrationFormFieldsCommand command => ValidOrder(command.OrderedIds),
        AddRegistrationFormFieldOptionCommand command => command.Ordinal > 0 && Present(command.Key, command.Label),
        UpdateRegistrationFormFieldOptionCommand command => command.Ordinal > 0 && Present(command.Key, command.Label),
        AddRegistrationFormRuleCommand command => command.Ordinal > 0 && command.Effect > 0 && command.Condition is not null &&
            Present(command.TargetNamespace, command.TargetKey),
        UpdateRegistrationFormRuleCommand command => command.Ordinal > 0 && command.Effect > 0 && command.Condition is not null &&
            Present(command.TargetNamespace, command.TargetKey),
        CreateRegistrationRequirementCommand command => command.Ordinal > 0 && command.CriticalityId > 0 &&
            command.CompletionEffectId > 0 && command.AnswerSyncModeId > 0 && command.AppliesToSubjectTypeId > 0,
        UpdateRegistrationRequirementCommand command => command.Ordinal > 0 && command.CriticalityId > 0 &&
            command.CompletionEffectId > 0 && command.AnswerSyncModeId > 0 && command.AppliesToSubjectTypeId > 0,
        _ => true
    };

    private static bool Valid(params Guid[] values) => values.All(value => value != Guid.Empty);

    private static bool Present(params string?[] values) => values.All(value => !string.IsNullOrWhiteSpace(value));

    private static bool ValidConsent(bool requiresExplicitConsent, string? purposeCode, string? textVersion,
        string? text) => requiresExplicitConsent
        ? Present(purposeCode, textVersion, text)
        : !Present(purposeCode) && !Present(textVersion) && !Present(text);

    private static bool ValidExport(bool isExportable, string? purposeCode) =>
        isExportable ? Present(purposeCode) : !Present(purposeCode);

    private static bool ValidOrder(IReadOnlyList<Guid>? ids) =>
        ids is { Count: > 0 and <= 200 } && ids.All(id => id != Guid.Empty) && ids.Distinct().Count() == ids.Count;
}

public sealed class RegistrationFormAuthoringQueryValidator<TQuery> : AbstractValidator<TQuery>
    where TQuery : ISecureRequest
{
    public RegistrationFormAuthoringQueryValidator()
    {
        RuleFor(value => value).Must(Valid).WithMessage("Registration authoring query identifiers are required.");
    }

    private static bool Valid(TQuery value) => value switch
    {
        GetRegistrationWorkflowQuery query => query.EventId != Guid.Empty && !string.IsNullOrWhiteSpace(query.Purpose),
        GetRegistrationFormQuery query => query.EventId != Guid.Empty && query.FormId != Guid.Empty,
        GetRegistrationFormVersionQuery query => query.EventId != Guid.Empty && query.FormId != Guid.Empty &&
            query.VersionId != Guid.Empty,
        GetRegistrationFormPublishPreflightQuery query => query.EventId != Guid.Empty && query.FormId != Guid.Empty &&
            query.VersionId != Guid.Empty,
        _ => false
    };
}
