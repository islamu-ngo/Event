namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Untrusted coordinates of one retained original notice. These identify custody, not permission:
/// the native owner must reacquire the effect, immutable recipe, privacy and human fences.
/// </summary>
public sealed record ModerationOriginalEffectAnchor(Guid EffectId, string SnapshotHash, string EffectBindingHash);
