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
        int serializedArtifactBytes)
    {
        Id = id;
        Name = name;
        SourceKind = sourceKind;
        EvidenceDigest = ArtifactDigest.Parse(evidenceDigest);
        SerializedArtifactBytes = serializedArtifactBytes;
    }

    public SetupCompositionScaleProfileId Id { get; }
    public string Name { get; }
    public SetupCompositionSourceKind SourceKind { get; }
    public ArtifactDigest EvidenceDigest { get; }
    public int SerializedArtifactBytes { get; }
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
            "8fdd1b6819cbb6e60778e16b90257d00f572b5605cc6e486edcf3394f64a22d5",
            681),
        new(
            SetupCompositionScaleProfileId.Medium,
            "medium",
            SetupCompositionSourceKind.Yaml,
            "a4735d0bbd1cd08ee47a8a5fe8456f38150a453bd67769af7751f5e21414e443",
            9_634),
        new(
            SetupCompositionScaleProfileId.Large,
            "large",
            SetupCompositionSourceKind.Directory,
            "6c1fe6990caa6cb68e2d9352a1ee2d31af305ca68533b496a20b7703f0b1821a",
            91_425),
        new(
            SetupCompositionScaleProfileId.Ceiling,
            "ceiling",
            SetupCompositionSourceKind.Json,
            "32bc03f3b90998fd05fc4227a2650de78dad30e925fbe58ed7cb9dbfc5a4a60c",
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
        if (targetMaximumArtifactBytes < profile.SerializedArtifactBytes)
            return SetupCompositionScaleAdmission.Rejected(
                SetupCompositionScaleAdmissionCode.TargetIncompatible);
        return SetupCompositionScaleAdmission.Accepted(profile);
    }
}
