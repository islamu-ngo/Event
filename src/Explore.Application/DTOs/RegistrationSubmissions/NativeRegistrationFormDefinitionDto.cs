using Explore.Application.DTOs.RegistrationForms;
using Explore.Domain.Enums;

namespace Explore.Application.DTOs.RegistrationSubmissions;

public sealed record NativeRegistrationFormDefinitionDto(
    Guid FormVersionId,
    int Version,
    string LanguageTag,
    string? SchemaHash,
    IReadOnlyList<NativeRegistrationFormSectionDto> Sections,
    IReadOnlyList<NativeRegistrationFormRuleDto> Rules);

public sealed record NativeRegistrationFormSectionDto(
    Guid Id,
    int Ordinal,
    string Title,
    IReadOnlyList<NativeRegistrationFormFieldDto> Fields);

public sealed record NativeRegistrationFormFieldDto(
    Guid Id,
    int Ordinal,
    string Namespace,
    string Key,
    string Label,
    int FieldTypeId,
    string FieldTypeCode,
    string FieldTypeName,
    bool RequiresExplicitConsent,
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
    IReadOnlyList<NativeRegistrationFormFieldOptionDto> Options);

public sealed record NativeRegistrationFormFieldOptionDto(
    Guid Id,
    int Ordinal,
    string Key,
    string Label,
    bool IsRetired);

public sealed record NativeRegistrationFormRuleDto(
    Guid Id,
    int Ordinal,
    string TargetNamespace,
    string TargetKey,
    int Effect,
    RegistrationFormConditionInputDto Condition);

public sealed record NativeRegistrationAnswerSubjectDto(
    RegistrationAnswerSubjectTypeEnum SubjectType,
    string SubjectTypeCode,
    Guid SubjectId,
    string SubjectKey,
    Guid? TicketAssignmentOrderLineId,
    bool IsCompleted,
    bool IsSkipped);

public sealed record NativeRegistrationRequirementProgressDto(
    int SubjectCount,
    int CompletedSubjectCount,
    int SkippedSubjectCount,
    int PendingSubjectCount,
    bool IsComplete);

public sealed record NativeRegistrationLaunchDescriptorDto(
    Guid RequirementId,
    Guid ChannelId,
    Guid FormId,
    Guid FormVersionId,
    bool CanSkip,
    IReadOnlyList<NativeRegistrationAnswerSubjectDto> Subjects,
    NativeRegistrationRequirementProgressDto Progress,
    Guid? BindingId = null);

public sealed record NativeRegistrationProviderLaunchDescriptorDto(
    Guid? AttemptId,
    Guid RequirementId,
    Guid ChannelId,
    Guid BindingId,
    Guid FormId,
    Guid FormVersionId,
    string Mode,
    bool Available,
    string? Url,
    string Title,
    bool OpenInNewTab,
    string FallbackMode,
    string Reason,
    IReadOnlyList<NativeRegistrationAnswerSubjectDto> Subjects,
    NativeRegistrationRequirementProgressDto Progress);

public sealed record NativeRegistrationRequirementProgressCollectionDto(
    Guid RegistrationOrderId,
    IReadOnlyList<NativeRegistrationLaunchDescriptorDto> Requirements,
    IReadOnlyList<NativeRegistrationProviderLaunchDescriptorDto>? ProviderRequirements = null);
