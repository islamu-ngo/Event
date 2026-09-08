namespace Explore.Application.Authorization;

/// <summary>
/// Classifies an artifact included in an authorization policy package.
/// </summary>
public enum PolicyArtifactKind
{
    /// <summary>
    /// A provider policy definition artifact.
    /// </summary>
    Policy = 0,

    /// <summary>
    /// A schema or validation contract artifact referenced by policies.
    /// </summary>
    Schema = 1
}
