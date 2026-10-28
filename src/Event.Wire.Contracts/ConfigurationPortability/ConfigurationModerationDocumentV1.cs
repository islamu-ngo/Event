namespace ISLAMU.Wire.Contracts.ConfigurationPortability;

using System.Text.Json.Serialization;

public static class ConfigurationModerationContentLimits
{
    public const int SchemaVersion = 1;
    public const int MaximumDefinitions = 256;
    public const int MaximumTextLength = 4000;
}

/// <summary>
/// Complete neutral definitions and requested support. Destination mapping, complete recipe
/// compilation, draft publication and activation qualification remain native Application responsibilities.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfigurationModerationDocumentV1
{
    private IReadOnlyList<ConfigurationModerationMode> _modes = Array.Empty<ConfigurationModerationMode>();
    private IReadOnlyList<ConfigurationModerationClassV1> _classes = Array.Empty<ConfigurationModerationClassV1>();
    private IReadOnlyList<ConfigurationModerationReasonV1> _reasons = Array.Empty<ConfigurationModerationReasonV1>();
    private IReadOnlyList<ConfigurationModerationProfileV1> _profiles = Array.Empty<ConfigurationModerationProfileV1>();
    private IReadOnlyList<ConfigurationModerationReferenceV1> _supportedProfiles = Array.Empty<ConfigurationModerationReferenceV1>();

    public required int SchemaVersion { get; init; }
    public required long CatalogueRevision { get; init; }
    public required IReadOnlyList<ConfigurationModerationMode> Modes { get => _modes; init => _modes = Snapshot.List(value); }
    public required IReadOnlyList<ConfigurationModerationClassV1> Classes { get => _classes; init => _classes = Snapshot.List(value); }
    public required IReadOnlyList<ConfigurationModerationReasonV1> Reasons { get => _reasons; init => _reasons = Snapshot.List(value); }
    public required IReadOnlyList<ConfigurationModerationProfileV1> Profiles { get => _profiles; init => _profiles = Snapshot.List(value); }
    public required IReadOnlyList<ConfigurationModerationReferenceV1> SupportedProfiles { get => _supportedProfiles; init => _supportedProfiles = Snapshot.List(value); }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfigurationModerationReferenceV1
{
    public required string Code { get; init; }
    public required long Revision { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfigurationModerationClassV1
{
    public required ConfigurationModerationReferenceV1 Reference { get; init; }
    public required string Meaning { get; init; }
    public required ConfigurationModerationGround Ground { get; init; }
    public required ConfigurationModerationBasis Basis { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfigurationModerationReasonV1
{
    public required ConfigurationModerationReferenceV1 Reference { get; init; }
    public required string Meaning { get; init; }
    public required ConfigurationModerationReferenceV1 Class { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfigurationModerationProfileV1
{
    private IReadOnlyList<ConfigurationModerationScopeKind> _scopeKinds = Array.Empty<ConfigurationModerationScopeKind>();
    private IReadOnlyList<ConfigurationModerationSubjectKind> _subjectKinds = Array.Empty<ConfigurationModerationSubjectKind>();
    private IReadOnlyList<ConfigurationModerationEffectV1> _effects = Array.Empty<ConfigurationModerationEffectV1>();
    private IReadOnlyList<ConfigurationModerationStrategy> _strategies = Array.Empty<ConfigurationModerationStrategy>();
    private IReadOnlyList<ConfigurationModerationRecipeV1> _recipes = Array.Empty<ConfigurationModerationRecipeV1>();

    public required ConfigurationModerationReferenceV1 Rule { get; init; }
    public required ConfigurationModerationReferenceV1 Reason { get; init; }
    public required ConfigurationModerationReferenceProfile ReferenceProfile { get; init; }
    public required long ReferenceRevision { get; init; }
    public required ConfigurationModerationGrade Grade { get; init; }
    public required IReadOnlyList<ConfigurationModerationScopeKind> ScopeKinds { get => _scopeKinds; init => _scopeKinds = Snapshot.List(value); }
    public required IReadOnlyList<ConfigurationModerationSubjectKind> SubjectKinds { get => _subjectKinds; init => _subjectKinds = Snapshot.List(value); }
    public required IReadOnlyList<ConfigurationModerationEffectV1> Effects { get => _effects; init => _effects = Snapshot.List(value); }
    public required ConfigurationModerationBundle Bundle { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required ConfigurationModerationProtectionPurpose? ProtectionPurpose { get; init; }
    public required IReadOnlyList<ConfigurationModerationStrategy> Strategies { get => _strategies; init => _strategies = Snapshot.List(value); }
    public required ConfigurationModerationTimingV1 Timing { get; init; }
    public required int RequiredApprovals { get; init; }
    public required int ContestationCalendarMonths { get; init; }
    public required ConfigurationModerationRecognition Recognition { get; init; }
    public required IReadOnlyList<ConfigurationModerationRecipeV1> Recipes { get => _recipes; init => _recipes = Snapshot.List(value); }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfigurationModerationTimingV1
{
    public required ConfigurationModerationTimingKind Kind { get; init; }
    public required TimeSpan Duration { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfigurationModerationEffectV1
{
    public required ConfigurationModerationEffectKind Kind { get; init; }
    public required ConfigurationModerationEffectMode Mode { get; init; }
    public required ConfigurationModerationTarget Target { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required ConfigurationModerationCapability? Capability { get; init; }
    public required ConfigurationModerationExecution Execution { get; init; }
}

/// <summary>All thirteen neutral dimensions; null Strategy identifies the base recipe.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfigurationModerationRecipeV1
{
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required ConfigurationModerationStrategy? Strategy { get; init; }
    public required ConfigurationModerationContentMode Content { get; init; }
    public required ConfigurationModerationManagementMode Management { get; init; }
    public required ConfigurationModerationResourceReadMode Resources { get; init; }
    public required ConfigurationModerationNewAdmissionMode NewAdmission { get; init; }
    public required ConfigurationModerationExistingAdmissionMode ExistingAdmission { get; init; }
    public required ConfigurationModerationPrivateDisclosureMode PrivateDisclosure { get; init; }
    public required ConfigurationModerationSettlementMode Settlement { get; init; }
    public required ConfigurationModerationCapacityMode Capacity { get; init; }
    public required ConfigurationModerationCredentialMode Credentials { get; init; }
    public required ConfigurationModerationNotificationMode Notifications { get; init; }
    public required ConfigurationModerationRecognitionMode Recognition { get; init; }
    public required ConfigurationModerationPendingWorkMode PendingWork { get; init; }
    public required ConfigurationModerationRestorationMode Restoration { get; init; }
}

/// <summary>Transport shape only. No catalogue matching, semantic compiler or operational authority.</summary>
internal static class ConfigurationModerationShape
{
    internal static void Validate(ConfigurationModerationDocumentV1? document, string path)
    {
        if (document is null)
            return;
        if (document.SchemaVersion != ConfigurationModerationContentLimits.SchemaVersion || document.CatalogueRevision < 1)
            Invalid(path);
        List(document.Modes, path + ".modes");
        List(document.Classes, path + ".classes", minimum: 1);
        List(document.Reasons, path + ".reasons", minimum: 1);
        List(document.Profiles, path + ".profiles", minimum: 1);
        List(document.SupportedProfiles, path + ".supportedProfiles");
        Unique(document.Modes, path + ".modes");
        Unique(document.Classes.Select(item => item.Reference), path + ".classes");
        Unique(document.Reasons.Select(item => item.Reference), path + ".reasons");
        Unique(document.Profiles.Select(item => item.Rule), path + ".profiles");
        Unique(document.SupportedProfiles, path + ".supportedProfiles");
        foreach (var item in document.Classes)
        {
            Reference(item.Reference, path + ".classes.reference");
            Text(item.Meaning, path + ".classes.meaning");
        }
        foreach (var item in document.Reasons)
        {
            Reference(item.Reference, path + ".reasons.reference");
            Reference(item.Class, path + ".reasons.class");
            Text(item.Meaning, path + ".reasons.meaning");
        }
        foreach (var item in document.SupportedProfiles)
            Reference(item, path + ".supportedProfiles");
        foreach (var item in document.Profiles)
        {
            string profilePath = path + ".profiles";
            Reference(item.Rule, profilePath + ".rule");
            Reference(item.Reason, profilePath + ".reason");
            if (item.ReferenceRevision < 1 || item.Timing is null || item.Timing.Duration < TimeSpan.Zero
                || item.RequiredApprovals < 0 || item.ContestationCalendarMonths < 0)
                Invalid(profilePath);
            List(item.ScopeKinds, profilePath + ".scopeKinds");
            List(item.SubjectKinds, profilePath + ".subjectKinds");
            List(item.Effects, profilePath + ".effects");
            List(item.Strategies, profilePath + ".strategies");
            List(item.Recipes, profilePath + ".recipes");
            Unique(item.ScopeKinds, profilePath + ".scopeKinds");
            Unique(item.SubjectKinds, profilePath + ".subjectKinds");
            Unique(item.Effects, profilePath + ".effects");
            Unique(item.Strategies, profilePath + ".strategies");
            Unique(item.Recipes.Select(recipe => recipe.Strategy), profilePath + ".recipes");
        }
    }

    private static void List<T>(IReadOnlyList<T>? items, string path, int minimum = 0)
    {
        if (items is null || items.Count < minimum || items.Any(item => item is null))
            Invalid(path);
        if (items!.Count > ConfigurationModerationContentLimits.MaximumDefinitions)
            throw new ConfigurationPortabilityContractException(ConfigurationPortabilityDiagnosticCodes.CountExceeded, path);
    }

    private static void Unique<T>(IEnumerable<T> items, string path)
    {
        var seen = new HashSet<T>();
        foreach (var item in items)
            if (!seen.Add(item))
                Invalid(path);
    }

    private static void Reference(ConfigurationModerationReferenceV1? reference, string path)
    {
        if (reference is null || reference.Revision < 1)
            Invalid(path);
        Text(reference!.Code, path + ".code");
    }

    private static void Text(string? text, string path)
    {
        if (string.IsNullOrWhiteSpace(text))
            Invalid(path);
        if (text!.Length > ConfigurationModerationContentLimits.MaximumTextLength)
            throw new ConfigurationPortabilityContractException(ConfigurationPortabilityDiagnosticCodes.StringTooLong, path);
    }

    private static void Invalid(string path) =>
        throw new ConfigurationPortabilityContractException(ConfigurationPortabilityDiagnosticCodes.ContractInvalid, path);
}
