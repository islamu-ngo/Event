namespace ISLAMU.Event.Setup.Core.Composition;

using ISLAMU.Event.Setup.Core;

public enum SetupCompositionScaleProfileId
{
    Small,
    Medium,
    Large,
    Ceiling
}

public enum SetupCompositionScaleAdmissionCode
{
    Accepted,
    UnknownProfile,
    ProfileDisabled,
    EvidenceMismatch,
    TargetIncompatible
}

public enum SetupCompositionScaleOutcome
{
    Succeeded,
    Rejected,
    Cancelled
}

public sealed class SetupCompositionScaleProfile
{
    internal SetupCompositionScaleProfile(
        SetupCompositionScaleProfileId id,
        string name,
        SetupCompositionSourceKind sourceKind,
        string evidenceDigest,
        int canonicalArtifactBytes)
    {
        Id = id;
        Name = name;
        SourceKind = sourceKind;
        EvidenceDigest = ArtifactDigest.Parse(evidenceDigest);
        CanonicalArtifactBytes = canonicalArtifactBytes;
    }

    public SetupCompositionScaleProfileId Id { get; }
    public string Name { get; }
    public SetupCompositionSourceKind SourceKind { get; }
    public ArtifactDigest EvidenceDigest { get; }
    public int CanonicalArtifactBytes { get; }
    public SetupCompositionLimits EffectiveLimits => SetupCompositionLimits.Default;

    public override string ToString() =>
        $"{nameof(SetupCompositionScaleProfile)}:{Name}:{SourceKind}";
}

public sealed class SetupCompositionScaleAdmission
{
    private SetupCompositionScaleAdmission(
        SetupCompositionScaleAdmissionCode code,
        SetupCompositionScaleProfile? profile)
    {
        Code = code;
        Profile = profile;
    }

    public bool Succeeded =>
        Code == SetupCompositionScaleAdmissionCode.Accepted && Profile is not null;
    public SetupCompositionScaleAdmissionCode Code { get; }
    public SetupCompositionScaleProfile? Profile { get; }

    internal static SetupCompositionScaleAdmission Accepted(
        SetupCompositionScaleProfile profile) =>
        new(SetupCompositionScaleAdmissionCode.Accepted, profile);

    internal static SetupCompositionScaleAdmission Rejected(
        SetupCompositionScaleAdmissionCode code) =>
        new(code, null);

    public override string ToString() =>
        $"{nameof(SetupCompositionScaleAdmission)}:{Code}:Succeeded={Succeeded}";
}

public sealed record SetupCompositionScaleTelemetry(
    SetupCompositionSourceKind SourceKind,
    SetupCompositionScaleProfileId Profile,
    SetupCompositionScaleOutcome Outcome,
    int AggregateBytes,
    int Nodes,
    int Files,
    long DurationMicroseconds);

public static class SetupCompositionScaleProfiles
{
    public const string DisabledExpandedProfileName = "expanded";

    private static readonly SetupCompositionScaleProfile[] Profiles =
    [
        new(
            SetupCompositionScaleProfileId.Small,
            "small",
            SetupCompositionSourceKind.Json,
            "29bc56c574126626ef4e7dc48090c54a3ec5aff378b3f7c65bd478e6eac9e062",
            681),
        new(
            SetupCompositionScaleProfileId.Medium,
            "medium",
            SetupCompositionSourceKind.Yaml,
            "3ccb79c47265802eb9ec5aedd2db60ace4537d946e6913a5b59c24dc97d331ea",
            9_634),
        new(
            SetupCompositionScaleProfileId.Large,
            "large",
            SetupCompositionSourceKind.Directory,
            "aad301a4d4780a668637e3f9d15986fa11c8278170b50c41a40af4c1e553cdea",
            91_425),
        new(
            SetupCompositionScaleProfileId.Ceiling,
            "ceiling",
            SetupCompositionSourceKind.Json,
            "0cc1498495205e8ae03e99268c3c48c676032ab411e79802c58c00dfd0599841",
            233_763)
    ];

    private static readonly IReadOnlyList<SetupCompositionScaleProfile> ReadOnlyProfiles =
        Array.AsReadOnly(Profiles);

    public static IReadOnlyList<SetupCompositionScaleProfile> All => ReadOnlyProfiles;

    public static SetupCompositionScaleAdmission Admit(
        string? profileName,
        ArtifactDigest evidenceDigest,
        int targetMaximumArtifactBytes)
    {
        if (string.Equals(
                profileName, DisabledExpandedProfileName, StringComparison.Ordinal))
            return SetupCompositionScaleAdmission.Rejected(
                SetupCompositionScaleAdmissionCode.ProfileDisabled);

        SetupCompositionScaleProfile? profile = Profiles.FirstOrDefault(item =>
            string.Equals(item.Name, profileName, StringComparison.Ordinal));
        if (profile is null)
            return SetupCompositionScaleAdmission.Rejected(
                SetupCompositionScaleAdmissionCode.UnknownProfile);
        if (profile.EvidenceDigest != evidenceDigest)
            return SetupCompositionScaleAdmission.Rejected(
                SetupCompositionScaleAdmissionCode.EvidenceMismatch);
        if (targetMaximumArtifactBytes < profile.CanonicalArtifactBytes)
            return SetupCompositionScaleAdmission.Rejected(
                SetupCompositionScaleAdmissionCode.TargetIncompatible);
        return SetupCompositionScaleAdmission.Accepted(profile);
    }
}
