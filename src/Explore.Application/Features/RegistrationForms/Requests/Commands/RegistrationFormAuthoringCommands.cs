using Explore.Application.Authorization;
using Explore.Application.DTOs.RegistrationForms;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.RegistrationForms.Requests.Commands;

public interface IRegistrationFormAuthoringCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    Guid EventId { get; }

    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString();

    // Form, version, and workflow identifiers select the payload, not the authority: registration
    // authoring is decided against the parent event, which the resolver reloads server-side.
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new EventScopedAuthorizationFacts(Guid.Empty, EventId);
}

public interface IRegistrationFormScopedCommand : IRegistrationFormAuthoringCommand
{
    Guid FormId { get; }

    string? ISecureRequest.ResourceId => FormId == Guid.Empty ? null : FormId.ToString();
}

public interface IRegistrationFormVersionScopedCommand : IRegistrationFormScopedCommand
{
    Guid VersionId { get; }
}

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManageRegistrationWorkflow)]
public sealed record CreateRegistrationWorkflowCommand(
    Guid EventId,
    string Purpose,
    Guid ExpectedConcurrencyStamp) : IRegistrationFormAuthoringCommand;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManageRegistrationWorkflow)]
public sealed record UpdateRegistrationWorkflowCommand(
    Guid EventId,
    Guid WorkflowId,
    string Purpose,
    Guid ExpectedConcurrencyStamp) : IRegistrationFormAuthoringCommand;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManageRegistrationWorkflow)]
public sealed record CreateRegistrationRequirementCommand(
    Guid EventId,
    Guid WorkflowId,
    int Ordinal,
    int CriticalityId,
    bool CanSkip,
    int CompletionEffectId,
    int AnswerSyncModeId,
    int AppliesToSubjectTypeId,
    Guid? AppliesToSubjectId,
    Guid ExpectedConcurrencyStamp) : IRegistrationFormAuthoringCommand;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManageRegistrationWorkflow)]
public sealed record UpdateRegistrationRequirementCommand(
    Guid EventId,
    Guid WorkflowId,
    Guid RequirementId,
    int Ordinal,
    int CriticalityId,
    bool CanSkip,
    int CompletionEffectId,
    int AnswerSyncModeId,
    int AppliesToSubjectTypeId,
    Guid? AppliesToSubjectId,
    Guid ExpectedConcurrencyStamp) : IRegistrationFormAuthoringCommand;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManageRegistrationWorkflow)]
public sealed record DeleteRegistrationRequirementCommand(
    Guid EventId,
    Guid WorkflowId,
    Guid RequirementId,
    Guid ExpectedConcurrencyStamp) : IRegistrationFormAuthoringCommand;

[AuthorizeResource(ResourceKinds.RegistrationForm, AuthorizationActions.RegistrationForms.Create)]
public sealed record CreateRegistrationFormCommand(
    Guid EventId,
    Guid WorkflowId,
    string Namespace,
    string Key,
    string Name,
    string LanguageTag,
    Guid ExpectedConcurrencyStamp) : IRegistrationFormAuthoringCommand;

[AuthorizeResource(ResourceKinds.RegistrationForm, AuthorizationActions.RegistrationForms.Create)]
public sealed record CreateRegistrationFormVersionCommand(
    Guid EventId,
    Guid FormId,
    Guid? CloneFromVersionId,
    string LanguageTag,
    Guid ExpectedConcurrencyStamp) : IRegistrationFormScopedCommand;

[AuthorizeResource(ResourceKinds.RegistrationForm, AuthorizationActions.RegistrationForms.Create)]
public sealed record AddRegistrationFormSectionCommand(
    Guid EventId,
    Guid FormId,
    Guid VersionId,
    int Ordinal,
    string Title,
    Guid ExpectedConcurrencyStamp) : IRegistrationFormVersionScopedCommand;

[AuthorizeResource(ResourceKinds.RegistrationForm, AuthorizationActions.RegistrationForms.Update)]
public sealed record UpdateRegistrationFormSectionCommand(
    Guid EventId,
    Guid FormId,
    Guid VersionId,
    Guid SectionId,
    int Ordinal,
    string Title,
    Guid ExpectedConcurrencyStamp) : IRegistrationFormVersionScopedCommand;

[AuthorizeResource(ResourceKinds.RegistrationForm, AuthorizationActions.RegistrationForms.Update)]
public sealed record ReorderRegistrationFormSectionsCommand(
    Guid EventId,
    Guid FormId,
    Guid VersionId,
    IReadOnlyList<Guid> OrderedIds,
    Guid ExpectedConcurrencyStamp) : IRegistrationFormVersionScopedCommand;

[AuthorizeResource(ResourceKinds.RegistrationForm, AuthorizationActions.RegistrationForms.Delete)]
public sealed record DeleteRegistrationFormSectionCommand(
    Guid EventId,
    Guid FormId,
    Guid VersionId,
    Guid SectionId,
    Guid ExpectedConcurrencyStamp) : IRegistrationFormVersionScopedCommand;

[AuthorizeResource(ResourceKinds.RegistrationForm, AuthorizationActions.RegistrationForms.Create)]
public sealed record AddRegistrationFormFieldCommand(
    Guid EventId,
    Guid FormId,
    Guid VersionId,
    Guid SectionId,
    int Ordinal,
    string Namespace,
    string Key,
    string Label,
    int FieldTypeId,
    int RetentionPolicyId,
    int OrganizerVisibilityId,
    bool RequiresExplicitConsent,
    bool IsProviderTransferAllowed,
    bool IsExportable,
    string? ExportPurposeCode,
    bool IsAnalyticsRelevant,
    bool IsOperationallyFilterable,
    string? ConsentPurposeCode,
    string? ConsentTextVersion,
    string? ConsentText,
    Guid ExpectedConcurrencyStamp) : IRegistrationFormVersionScopedCommand;

[AuthorizeResource(ResourceKinds.RegistrationForm, AuthorizationActions.RegistrationForms.Update)]
public sealed record UpdateRegistrationFormFieldCommand(
    Guid EventId,
    Guid FormId,
    Guid VersionId,
    Guid SectionId,
    Guid FieldId,
    int Ordinal,
    string Label,
    int RetentionPolicyId,
    int OrganizerVisibilityId,
    bool RequiresExplicitConsent,
    bool IsProviderTransferAllowed,
    bool IsExportable,
    string? ExportPurposeCode,
    bool IsAnalyticsRelevant,
    bool IsOperationallyFilterable,
    string? ConsentPurposeCode,
    string? ConsentTextVersion,
    string? ConsentText,
    bool IsRequired,
    bool IsMulti,
    int? MinLength,
    int? MaxLength,
    string? RegexPattern,
    decimal? MinNumber,
    decimal? MaxNumber,
    DateTimeOffset? MinDateTime,
    DateTimeOffset? MaxDateTime,
    string? AllowedUrlSchemes,
    Guid ExpectedConcurrencyStamp) : IRegistrationFormVersionScopedCommand;

[AuthorizeResource(ResourceKinds.RegistrationForm, AuthorizationActions.RegistrationForms.Update)]
public sealed record ReorderRegistrationFormFieldsCommand(
    Guid EventId,
    Guid FormId,
    Guid VersionId,
    Guid SectionId,
    IReadOnlyList<Guid> OrderedIds,
    Guid ExpectedConcurrencyStamp) : IRegistrationFormVersionScopedCommand;

[AuthorizeResource(ResourceKinds.RegistrationForm, AuthorizationActions.RegistrationForms.Delete)]
public sealed record DeleteRegistrationFormFieldCommand(
    Guid EventId,
    Guid FormId,
    Guid VersionId,
    Guid SectionId,
    Guid FieldId,
    Guid ExpectedConcurrencyStamp) : IRegistrationFormVersionScopedCommand;

[AuthorizeResource(ResourceKinds.RegistrationForm, AuthorizationActions.RegistrationForms.Create)]
public sealed record AddRegistrationFormFieldOptionCommand(
    Guid EventId,
    Guid FormId,
    Guid VersionId,
    Guid SectionId,
    Guid FieldId,
    int Ordinal,
    string Key,
    string Label,
    Guid ExpectedConcurrencyStamp) : IRegistrationFormVersionScopedCommand;

[AuthorizeResource(ResourceKinds.RegistrationForm, AuthorizationActions.RegistrationForms.Update)]
public sealed record UpdateRegistrationFormFieldOptionCommand(
    Guid EventId,
    Guid FormId,
    Guid VersionId,
    Guid SectionId,
    Guid FieldId,
    Guid OptionId,
    int Ordinal,
    string Key,
    string Label,
    Guid ExpectedConcurrencyStamp) : IRegistrationFormVersionScopedCommand;

[AuthorizeResource(ResourceKinds.RegistrationForm, AuthorizationActions.RegistrationForms.Update)]
public sealed record RetireRegistrationFormFieldOptionCommand(
    Guid EventId,
    Guid FormId,
    Guid VersionId,
    Guid SectionId,
    Guid FieldId,
    Guid OptionId,
    Guid ExpectedConcurrencyStamp) : IRegistrationFormVersionScopedCommand;

[AuthorizeResource(ResourceKinds.RegistrationForm, AuthorizationActions.RegistrationForms.Create)]
public sealed record AddRegistrationFormRuleCommand(
    Guid EventId,
    Guid FormId,
    Guid VersionId,
    int Ordinal,
    string TargetNamespace,
    string TargetKey,
    int Effect,
    RegistrationFormConditionInputDto Condition,
    Guid ExpectedConcurrencyStamp) : IRegistrationFormVersionScopedCommand;

[AuthorizeResource(ResourceKinds.RegistrationForm, AuthorizationActions.RegistrationForms.Update)]
public sealed record UpdateRegistrationFormRuleCommand(
    Guid EventId,
    Guid FormId,
    Guid VersionId,
    Guid RuleId,
    int Ordinal,
    string TargetNamespace,
    string TargetKey,
    int Effect,
    RegistrationFormConditionInputDto Condition,
    Guid ExpectedConcurrencyStamp) : IRegistrationFormVersionScopedCommand;

[AuthorizeResource(ResourceKinds.RegistrationForm, AuthorizationActions.RegistrationForms.Delete)]
public sealed record DeleteRegistrationFormRuleCommand(
    Guid EventId,
    Guid FormId,
    Guid VersionId,
    Guid RuleId,
    Guid ExpectedConcurrencyStamp) : IRegistrationFormVersionScopedCommand;

[AuthorizeResource(ResourceKinds.RegistrationForm, AuthorizationActions.RegistrationForms.Publish)]
public sealed record PublishRegistrationFormVersionCommand(
    Guid EventId,
    Guid FormId,
    Guid VersionId,
    Guid ExpectedConcurrencyStamp) : IRegistrationFormVersionScopedCommand;
