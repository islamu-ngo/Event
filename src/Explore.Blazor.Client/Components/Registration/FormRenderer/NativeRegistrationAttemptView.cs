using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Components.Registration.FormRenderer;

public sealed record NativeRegistrationAttemptView(
    Guid AttemptId,
    Guid RequirementId,
    string AttemptCapabilityToken,
    string LanguageTag,
    IReadOnlyList<RegistrationSectionView> Sections,
    IReadOnlyList<RegistrationRuleView> Rules,
    IReadOnlyList<RegistrationSubjectView> Subjects,
    RegistrationRequirementProgressView Progress,
    IReadOnlyDictionary<string, HalLink> Links)
{
    public static NativeRegistrationAttemptView From(HalResourceOfNativeRegistrationAttemptDto source) => new(
        source.AttemptId,
        source.RequirementId,
        source.AttemptCapabilityToken,
        source.Form.LanguageTag,
        source.Form.Sections.Select(section => new RegistrationSectionView(
            section.Id,
            section.Ordinal,
            section.Title,
            section.Fields.Select(item => new RegistrationFieldView(
                item.Id, item.Ordinal, item.Namespace, item.Key, item.Label, item.FieldTypeCode,
                item.IsRequired, item.IsMulti, item.MinLength, item.MaxLength, item.RegexPattern,
                item.MinNumber, item.MaxNumber, item.MinDateTime, item.MaxDateTime, item.AllowedUrlSchemes,
                item.ConsentTextVersion, item.ConsentText,
                item.Options.Select(option => new RegistrationFieldOptionView(
                    option.Id, option.Ordinal, option.Key, option.Label, option.IsRetired)).ToArray())).ToArray())).ToArray(),
        source.Form.Rules.Select(rule => new RegistrationRuleView(
            rule.Ordinal, rule.TargetNamespace, rule.TargetKey, rule.Effect, rule.Condition)).ToArray(),
        source.Subjects.Select(subject => new RegistrationSubjectView(
            subject.SubjectType, subject.SubjectId, subject.SubjectKey,
            subject.TicketAssignmentOrderLineId, subject.IsCompleted, subject.IsSkipped)).ToArray(),
        new RegistrationRequirementProgressView(
            source.Progress.SubjectCount, source.Progress.CompletedSubjectCount,
            source.Progress.SkippedSubjectCount, source.Progress.PendingSubjectCount, source.Progress.IsComplete),
        source._links is null ? new Dictionary<string, HalLink>() : new Dictionary<string, HalLink>(source._links, StringComparer.Ordinal));
}

public sealed record RegistrationSectionView(Guid Id, int Ordinal, string Title, IReadOnlyList<RegistrationFieldView> Fields);

public sealed record RegistrationFieldView(
    Guid Id, int Ordinal, string Namespace, string Key, string Label, string FieldTypeCode,
    bool IsRequired, bool IsMulti, int? MinLength, int? MaxLength, string? RegexPattern,
    double? MinNumber, double? MaxNumber, DateTimeOffset? MinDateTime, DateTimeOffset? MaxDateTime,
    string? AllowedUrlSchemes, string? ConsentTextVersion, string? ConsentText,
    IReadOnlyList<RegistrationFieldOptionView> Options);

public sealed record RegistrationFieldOptionView(Guid Id, int Ordinal, string Key, string Label, bool IsRetired);
public sealed record RegistrationRuleView(int Ordinal, string TargetNamespace, string TargetKey, int Effect, object Condition);
public sealed record RegistrationSubjectView(int SubjectType, Guid SubjectId, string SubjectKey, Guid? TicketAssignmentOrderLineId, bool IsCompleted, bool IsSkipped);
public sealed record RegistrationRequirementProgressView(int SubjectCount, int CompletedSubjectCount, int SkippedSubjectCount, int PendingSubjectCount, bool IsComplete);

public sealed record NativeRegistrationLaunchDescriptorView(
    Guid RequirementId, Guid ChannelId, Guid FormId, Guid FormVersionId, bool CanSkip,
    IReadOnlyList<RegistrationSubjectView> Subjects, RegistrationRequirementProgressView Progress);

public sealed record NativeRegistrationRequirementCollectionView(
    IReadOnlyList<NativeRegistrationLaunchDescriptorView> Requirements,
    IReadOnlyDictionary<string, HalLink> Links)
{
    public static NativeRegistrationRequirementCollectionView From(
        HalResourceOfNativeRegistrationRequirementProgressCollectionDto source) => new(
        source.Requirements.Select(requirement => new NativeRegistrationLaunchDescriptorView(
            requirement.RequirementId, requirement.ChannelId, requirement.FormId, requirement.FormVersionId,
            requirement.CanSkip,
            requirement.Subjects.Select(subject => new RegistrationSubjectView(
                subject.SubjectType, subject.SubjectId, subject.SubjectKey,
                subject.TicketAssignmentOrderLineId, subject.IsCompleted, subject.IsSkipped)).ToArray(),
            new RegistrationRequirementProgressView(
                requirement.Progress.SubjectCount, requirement.Progress.CompletedSubjectCount,
                requirement.Progress.SkippedSubjectCount, requirement.Progress.PendingSubjectCount,
                requirement.Progress.IsComplete))).ToArray(),
        source._links is null
            ? new Dictionary<string, HalLink>()
            : new Dictionary<string, HalLink>(source._links, StringComparer.Ordinal));
}

public sealed record NativeRegistrationActionResult(
    bool Success,
    IReadOnlyDictionary<Guid, IReadOnlyList<string>> FieldIssues)
{
    public static NativeRegistrationActionResult Accepted { get; } = new(true, new Dictionary<Guid, IReadOnlyList<string>>());
    public static NativeRegistrationActionResult Failed { get; } = new(false, new Dictionary<Guid, IReadOnlyList<string>>());
    public static NativeRegistrationActionResult Invalid(IReadOnlyDictionary<Guid, IReadOnlyList<string>> issues) => new(false, issues);
}
