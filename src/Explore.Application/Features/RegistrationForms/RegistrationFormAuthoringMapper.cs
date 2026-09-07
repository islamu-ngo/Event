using Explore.Application.DTOs.RegistrationForms;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;

namespace Explore.Application.Features.RegistrationForms;

internal static class RegistrationFormAuthoringMapper
{
    public static RegistrationWorkflowDto ToDto(
        RegistrationWorkflow workflow,
        IReadOnlyList<RegistrationForm> forms,
        IReadOnlySet<Guid> attachedRequirementIds) => new(
        workflow.Id,
        workflow.TenantId,
        workflow.EventId,
        workflow.Purpose,
        workflow.ConcurrencyStamp,
        [.. workflow.Requirements.Where(value => !value.IsDeleted).OrderBy(value => value.Ordinal)
            .Select(value => ToDto(value, attachedRequirementIds.Contains(value.Id)))],
        [.. forms.Where(value => !value.IsDeleted).OrderBy(value => value.Name).Select(ToDto)]);

    public static RegistrationFormDto ToDto(RegistrationForm form) => new(
        form.Id,
        form.TenantId,
        form.EventId,
        form.Namespace,
        form.Key,
        form.Name,
        form.ConcurrencyStamp,
        [.. form.Versions.Where(value => !value.IsDeleted).OrderByDescending(value => value.Version).Select(ToSummaryDto)]);

    public static RegistrationFormVersionDto ToDto(RegistrationFormVersion version) => new(
        version.Id,
        version.TenantId,
        version.EventId,
        version.RegistrationFormId,
        version.Version,
        version.StatusId,
        Code((RegistrationFormStatusEnum)version.StatusId),
        Name((RegistrationFormStatusEnum)version.StatusId),
        version.LanguageTag,
        version.SchemaHash,
        version.PublishedAt,
        version.RetiredAt,
        version.SourceTemplateFormId,
        version.SourceTemplateVersionId,
        version.ConcurrencyStamp,
        [.. version.Sections.Where(value => !value.IsDeleted).OrderBy(value => value.Ordinal).Select(ToDto)],
        [.. version.Rules.Where(value => !value.IsDeleted).OrderBy(value => value.Ordinal).Select(ToDto)]);

    public static RegistrationFormConditionInputDto ToDto(FormCondition condition) => condition switch
    {
        FormCondition.EqualsCondition value => Leaf("equals", value.Field, ToDto(value.Value)),
        FormCondition.NotEqualsCondition value => Leaf("notEquals", value.Field, ToDto(value.Value)),
        FormCondition.InCondition value => new("in", value.Field.Namespace, value.Field.Key,
            Values: [.. value.Values.Select(ToDto)]),
        FormCondition.ContainsCondition value => Leaf("contains", value.Field, ToDto(value.Value)),
        FormCondition.ExistsCondition value => Leaf("exists", value.Field),
        FormCondition.CompareCondition value => new("compare", value.Field.Namespace, value.Field.Key,
            value.Comparison.ToString(), ToDto(value.Value)),
        FormCondition.AllCondition value => new("all", Conditions: [.. value.Conditions.Select(ToDto)]),
        FormCondition.AnyCondition value => new("any", Conditions: [.. value.Conditions.Select(ToDto)]),
        FormCondition.NotCondition value => new("not", Condition: ToDto(value.Condition)),
        _ => throw new ArgumentOutOfRangeException(nameof(condition))
    };

    public static FormCondition ToDomain(RegistrationFormConditionInputDto condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return condition.Operator switch
        {
            "equals" => new FormCondition.EqualsCondition(Field(condition), Scalar(condition.Value)),
            "notEquals" => new FormCondition.NotEqualsCondition(Field(condition), Scalar(condition.Value)),
            "in" => new FormCondition.InCondition(Field(condition),
                condition.Values?.Select(Scalar).ToArray() ?? throw InvalidCondition()),
            "contains" => new FormCondition.ContainsCondition(Field(condition), Scalar(condition.Value)),
            "exists" => new FormCondition.ExistsCondition(Field(condition)),
            "compare" => new FormCondition.CompareCondition(Field(condition),
                Enum.Parse<FormComparisonKind>(condition.Comparison ?? string.Empty, true), Scalar(condition.Value)),
            "all" => new FormCondition.AllCondition(Conditions(condition)),
            "any" => new FormCondition.AnyCondition(Conditions(condition)),
            "not" => new FormCondition.NotCondition(ToDomain(condition.Condition ?? throw InvalidCondition())),
            _ => throw InvalidCondition()
        };
    }

    private static RegistrationRequirementDto ToDto(RegistrationRequirement value, bool isAttached)
    {
        RegistrationRequirementCriticalityEnum criticality = (RegistrationRequirementCriticalityEnum)value.CriticalityId;
        RegistrationRequirementCompletionEffectEnum effect = (RegistrationRequirementCompletionEffectEnum)value.CompletionEffectId;
        RegistrationAnswerSyncModeEnum sync = (RegistrationAnswerSyncModeEnum)value.AnswerSyncModeId;
        RegistrationRequirementSubjectTypeEnum subject = (RegistrationRequirementSubjectTypeEnum)value.AppliesToSubjectTypeId;
        return new(
            value.Id,
            value.Ordinal,
            value.CriticalityId,
            Code(criticality),
            Name(criticality),
            value.CanSkip,
            value.CompletionEffectId,
            Code(effect),
            Name(effect),
            value.AnswerSyncModeId,
            Code(sync),
            Name(sync),
            value.AppliesToSubjectTypeId,
            Code(subject),
            Name(subject),
            value.AppliesToSubjectId,
            value.ConcurrencyStamp,
            isAttached,
            [.. value.Channels.Where(channel => !channel.IsDeleted).OrderBy(channel => channel.Ordinal)
                .Select(channel => new RegistrationChannelDto(channel.Id, channel.Ordinal, channel.IsNative,
                    channel.RegistrationProviderBindingId, channel.ConcurrencyStamp))]);
    }

    private static RegistrationFormVersionSummaryDto ToSummaryDto(RegistrationFormVersion value) => new(
        value.Id,
        value.Version,
        value.StatusId,
        Code((RegistrationFormStatusEnum)value.StatusId),
        Name((RegistrationFormStatusEnum)value.StatusId),
        value.LanguageTag,
        value.SchemaHash,
        value.PublishedAt,
        value.RetiredAt,
        value.SourceTemplateFormId,
        value.SourceTemplateVersionId,
        value.ConcurrencyStamp);

    private static RegistrationFormSectionDto ToDto(RegistrationFormSection value) => new(
        value.Id,
        value.Ordinal,
        value.Title,
        value.ConcurrencyStamp,
        [.. value.Fields.Where(field => !field.IsDeleted).OrderBy(field => field.Ordinal).Select(ToDto)]);

    private static RegistrationFormFieldDto ToDto(RegistrationFormField value)
    {
        RegistrationFieldTypeEnum fieldType = (RegistrationFieldTypeEnum)value.FieldTypeId;
        RegistrationOrganizerVisibilityEnum visibility = (RegistrationOrganizerVisibilityEnum)value.OrganizerVisibilityId;
        return new(
            value.Id,
            value.Ordinal,
            value.Namespace,
            value.Key,
            value.Label,
            value.FieldTypeId,
            Code(fieldType),
            Name(fieldType),
            value.RetentionPolicyId,
            value.OrganizerVisibilityId,
            Code(visibility),
            Name(visibility),
            value.RequiresExplicitConsent,
            value.IsProviderTransferAllowed,
            value.IsExportable,
            value.ExportPurposeCode,
            value.IsAnalyticsRelevant,
            value.IsOperationallyFilterable,
            value.ConsentPurposeCode,
            value.ConsentTextVersion,
            value.ConsentText,
            value.IsRequired,
            value.IsMulti,
            value.MinLength,
            value.MaxLength,
            value.RegexPattern,
            value.MinNumber,
            value.MaxNumber,
            value.MinDateTime,
            value.MaxDateTime,
            value.AllowedUrlSchemes,
            value.ConcurrencyStamp,
            [.. value.Options.Where(option => !option.IsDeleted).OrderBy(option => option.Ordinal)
                .Select(option => new RegistrationFormFieldOptionDto(option.Id, option.Ordinal, option.Key,
                    option.Label, option.RetiredAt, option.ConcurrencyStamp))]);
    }

    private static RegistrationFormRuleDto ToDto(RegistrationFormRule value) => new(
        value.Id,
        value.Ordinal,
        value.TargetNamespace,
        value.TargetKey,
        (int)value.Effect,
        ToDto(value.Condition),
        value.ConcurrencyStamp);

    private static RegistrationFormConditionInputDto Leaf(
        string @operator,
        FormFieldReference field,
        RegistrationFormScalarValueInputDto? value = null) =>
        new(@operator, field.Namespace, field.Key, Value: value);

    private static RegistrationFormScalarValueInputDto ToDto(FormScalarValue value) => value switch
    {
        FormScalarValue.Null => new("null"),
        FormScalarValue.Text text => new("text", TextValue: text.Value),
        FormScalarValue.Boolean boolean => new("boolean", BooleanValue: boolean.Value),
        FormScalarValue.Number number => new("number", NumberValue: number.Value),
        FormScalarValue.Date date => new("date", DateValue: date.Value),
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static FormScalarValue Scalar(RegistrationFormScalarValueInputDto? value) => value?.Type switch
    {
        "null" => FormScalarValue.NullValue,
        "text" when value.TextValue is not null => FormScalarValue.From(value.TextValue),
        "boolean" when value.BooleanValue.HasValue => FormScalarValue.From(value.BooleanValue.Value),
        "number" when value.NumberValue.HasValue => FormScalarValue.From(value.NumberValue.Value),
        "date" when value.DateValue.HasValue => FormScalarValue.From(value.DateValue.Value),
        _ => throw InvalidCondition()
    };

    private static FormFieldReference Field(RegistrationFormConditionInputDto value) =>
        new(value.FieldNamespace ?? throw InvalidCondition(), value.FieldKey ?? throw InvalidCondition());

    private static FormCondition[] Conditions(RegistrationFormConditionInputDto value) =>
        value.Conditions?.Select(ToDomain).ToArray() ?? throw InvalidCondition();

    private static ArgumentException InvalidCondition() => new("Registration form condition is incomplete or invalid.");

    private static string Code<T>(T value) where T : struct, Enum => value switch
    {
        RegistrationRequirementCriticalityEnum.PostRegistration => "POST_REGISTRATION",
        RegistrationRequirementCompletionEffectEnum.BlocksRegistration => "BLOCKS_REGISTRATION",
        RegistrationRequirementCompletionEffectEnum.EnrichesRegistration => "ENRICHES_REGISTRATION",
        RegistrationRequirementCompletionEffectEnum.NoRegistrationEffect => "NO_REGISTRATION_EFFECT",
        RegistrationRequirementSubjectTypeEnum.AllOrders => "ALL_ORDERS",
        RegistrationRequirementSubjectTypeEnum.SpecificTicketType => "SPECIFIC_TICKET_TYPE",
        RegistrationRequirementSubjectTypeEnum.EveryParticipant => "EVERY_PARTICIPANT",
        RegistrationRequirementSubjectTypeEnum.LeadBookerOnly => "LEAD_BOOKER_ONLY",
        RegistrationRequirementSubjectTypeEnum.ChildParticipants => "CHILD_PARTICIPANTS",
        RegistrationRequirementSubjectTypeEnum.SpecificSessionSelection => "SPECIFIC_SESSION_SELECTION",
        RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers => "AUTHORIZED_ORGANIZERS",
        RegistrationFieldTypeEnum.ShortText => "SHORT_TEXT",
        RegistrationFieldTypeEnum.LongText => "LONG_TEXT",
        RegistrationFieldTypeEnum.CountryCode => "COUNTRY_CODE",
        RegistrationFieldTypeEnum.LanguageTag => "LANGUAGE_TAG",
        RegistrationFieldTypeEnum.SingleChoice => "SINGLE_CHOICE",
        RegistrationFieldTypeEnum.MultipleChoice => "MULTIPLE_CHOICE",
        RegistrationFieldTypeEnum.OpaqueExternal => "OPAQUE_EXTERNAL",
        _ => value.ToString().ToUpperInvariant()
    };

    private static string Name<T>(T value) where T : struct, Enum => value switch
    {
        RegistrationRequirementCriticalityEnum.PostRegistration => "Post-registration",
        RegistrationRequirementCompletionEffectEnum.BlocksRegistration => "Blocks registration",
        RegistrationRequirementCompletionEffectEnum.EnrichesRegistration => "Enriches registration",
        RegistrationRequirementCompletionEffectEnum.NoRegistrationEffect => "No registration effect",
        RegistrationRequirementSubjectTypeEnum.AllOrders => "All orders",
        RegistrationRequirementSubjectTypeEnum.SpecificTicketType => "Specific ticket type",
        RegistrationRequirementSubjectTypeEnum.EveryParticipant => "Every participant",
        RegistrationRequirementSubjectTypeEnum.LeadBookerOnly => "Lead booker only",
        RegistrationRequirementSubjectTypeEnum.ChildParticipants => "Child participants",
        RegistrationRequirementSubjectTypeEnum.SpecificSessionSelection => "Specific session selection",
        RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers => "Authorized organizers",
        RegistrationFieldTypeEnum.ShortText => "Short text",
        RegistrationFieldTypeEnum.LongText => "Long text",
        RegistrationFieldTypeEnum.CountryCode => "Country code",
        RegistrationFieldTypeEnum.LanguageTag => "Language tag",
        RegistrationFieldTypeEnum.SingleChoice => "Single choice",
        RegistrationFieldTypeEnum.MultipleChoice => "Multiple choice",
        RegistrationFieldTypeEnum.OpaqueExternal => "Opaque external",
        _ => value.ToString()
    };
}
