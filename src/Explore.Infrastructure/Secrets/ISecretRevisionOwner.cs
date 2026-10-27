using Explore.Domain.Secrets;

namespace Explore.Infrastructure.Secrets;

/// <summary>
/// Fences resolver cache returns against the selected process owner's retained
/// comparison evidence. This contract does not read or cache secret material.
/// </summary>
internal interface ISecretRevisionOwner
{
    bool IsRevisionCurrent(SecretBinding binding, string? revision);
}
