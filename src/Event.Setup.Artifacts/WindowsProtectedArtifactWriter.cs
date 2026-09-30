namespace ISLAMU.Event.Setup.Artifacts;

internal static class WindowsProtectedArtifactWriter
{
    // Enable only after native protected-DACL creation, reparse/race and cleanup
    // invariants have executable evidence on Windows. Never use inherited ACLs.
    internal static ProtectedArtifactPreparation Unavailable() =>
        ProtectedArtifactPreparation.Rejected(ProtectedArtifactStatus.UnsupportedPlatform);
}
