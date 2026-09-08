namespace Explore.Application.DTOs.RegistrationForms;

public sealed record RegistrationWorkflowDto(
    Guid Id,
    Guid TenantId,
    Guid EventId,
    string Purpose,
    Guid ConcurrencyStamp,
    IReadOnlyList<RegistrationRequirementDto> Requirements,
    IReadOnlyList<RegistrationFormDto> Forms);

public sealed record RegistrationRequirementDto(
    Guid Id,
    int Ordinal,
    int CriticalityId,
    string CriticalityCode,
    string CriticalityName,
    bool CanSkip,
    int CompletionEffectId,
    string CompletionEffectCode,
    string CompletionEffectName,
    int AnswerSyncModeId,
    string AnswerSyncModeCode,
    string AnswerSyncModeName,
    int AppliesToSubjectTypeId,
    string AppliesToSubjectTypeCode,
    string AppliesToSubjectTypeName,
    Guid? AppliesToSubjectId,
    Guid ConcurrencyStamp,
    bool IsAttached,
    IReadOnlyList<RegistrationChannelDto> Channels);

public sealed record RegistrationChannelDto(
    Guid Id,
    int Ordinal,
    bool IsNative,
    Guid? RegistrationProviderBindingId,
    Guid ConcurrencyStamp);

public sealed record RegistrationFormDto(
    Guid Id,
    Guid TenantId,
    Guid EventId,
    string Namespace,
    string Key,
    string Name,
    Guid ConcurrencyStamp,
    IReadOnlyList<RegistrationFormVersionSummaryDto> Versions);

public sealed record RegistrationFormVersionSummaryDto(
    Guid Id,
    int Version,
    int StatusId,
    string StatusCode,
    string StatusName,
    string LanguageTag,
    string? SchemaHash,
    DateTime? PublishedAt,
    DateTime? RetiredAt,
    Guid? SourceTemplateFormId,
    Guid? SourceTemplateVersionId,
    Guid ConcurrencyStamp);

public sealed record RegistrationFormVersionDto(
    Guid Id,
    Guid TenantId,
    Guid EventId,
    Guid RegistrationFormId,
    int Version,
    int StatusId,
    string StatusCode,
    string StatusName,
    string LanguageTag,
    string? SchemaHash,
    DateTime? PublishedAt,
    DateTime? RetiredAt,
    Guid? SourceTemplateFormId,
    Guid? SourceTemplateVersionId,
    Guid ConcurrencyStamp,
    IReadOnlyList<RegistrationFormSectionDto> Sections,
    IReadOnlyList<RegistrationFormRuleDto> Rules);

public sealed record RegistrationFormSectionDto(
    Guid Id,
    int Ordinal,
    string Title,
    Guid ConcurrencyStamp,
    IReadOnlyList<RegistrationFormFieldDto> Fields);

public sealed record RegistrationFormFieldDto(
    Guid Id,
    int Ordinal,
    string Namespace,
    string Key,
    string Label,
    int FieldTypeId,
    string FieldTypeCode,
    string FieldTypeName,
    int RetentionPolicyId,
    int OrganizerVisibilityId,
    string OrganizerVisibilityCode,
    string OrganizerVisibilityName,
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
    Guid ConcurrencyStamp,
    IReadOnlyList<RegistrationFormFieldOptionDto> Options);

public sealed record RegistrationFormFieldOptionDto(
    Guid Id,
    int Ordinal,
    string Key,
    string Label,
    DateTime? RetiredAt,
    Guid ConcurrencyStamp);

public sealed record RegistrationFormRuleDto(
    Guid Id,
    int Ordinal,
    string TargetNamespace,
    string TargetKey,
    int Effect,
    RegistrationFormConditionInputDto Condition,
    Guid ConcurrencyStamp);

public sealed record RegistrationFormPublishPreflightDto(
    bool CanPublish,
    IReadOnlyList<RegistrationFormPublishPreflightIssueDto> Issues);

public sealed record RegistrationFormPublishPreflightIssueDto(
    string Code,
    string Message,
    Guid? FieldId = null,
    Guid? RuleId = null);

public sealed record RegistrationFormConditionInputDto(
    string Operator,
    string? FieldNamespace = null,
    string? FieldKey = null,
    string? Comparison = null,
    RegistrationFormScalarValueInputDto? Value = null,
    IReadOnlyList<RegistrationFormScalarValueInputDto>? Values = null,
    IReadOnlyList<RegistrationFormConditionInputDto>? Conditions = null,
    RegistrationFormConditionInputDto? Condition = null);

public sealed record RegistrationFormScalarValueInputDto(
    string Type,
    string? TextValue = null,
    bool? BooleanValue = null,
    decimal? NumberValue = null,
    DateOnly? DateValue = null);
