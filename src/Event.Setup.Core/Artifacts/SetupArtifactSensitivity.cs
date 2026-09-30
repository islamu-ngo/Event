namespace ISLAMU.Event.Setup.Core;

public enum SetupArtifactKind
{
    Unknown,
    PublicCatalogue,
    PublicTemplate,
    Environment,
    Configuration,
    OperatorIdentity
}

public enum SetupArtifactSensitivity
{
    Restricted,
    Public
}

public static class SetupArtifactPolicy
{
    public static SetupArtifactSensitivity Classify(SetupArtifactKind kind) =>
        kind is SetupArtifactKind.PublicCatalogue or SetupArtifactKind.PublicTemplate
            ? SetupArtifactSensitivity.Public
            : SetupArtifactSensitivity.Restricted;

    public static bool TryProjectPublic(
        SetupArtifactKind kind,
        ReadOnlyMemory<byte> bytes,
        out ReadOnlyMemory<byte> publicBytes)
    {
        publicBytes = Classify(kind) == SetupArtifactSensitivity.Public
            ? bytes : ReadOnlyMemory<byte>.Empty;
        return Classify(kind) == SetupArtifactSensitivity.Public;
    }
}
