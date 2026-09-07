using Explore.Domain.Enums;

namespace Explore.Domain.Secrets.Events;

/// <summary>
/// Domain event describing a change to a <see cref="SecretBinding"/>. The event itself is pure — it has
/// no MediatR dependency (Domain layer has zero outbound dependencies). Application-layer wrappers
/// translate this event into <c>INotification</c> dispatches for handlers.
/// </summary>
public sealed record SecretBindingUpdatedEvent(
    Guid BindingId,
    string SettingKey,
    SecretScope Scope,
    Guid? ScopeId,
    SecretSourceType SourceType,
    SecretBindingChangeKind ChangeKind,
    DateTimeOffset OccurredAt);

/// <summary>
/// The nature of the binding change. Drives downstream handler logic (e.g. whether to force a cache
/// invalidation vs. also triggering a scheme refresh).
/// </summary>
public enum SecretBindingChangeKind
{
    /// <summary>A new binding was inserted.</summary>
    Created = 0,

    /// <summary>An existing binding's metadata was updated without changing <see cref="SecretBinding.SourceType"/>.</summary>
    Updated = 1,

    /// <summary>An existing binding was deleted (hard or soft).</summary>
    Deleted = 2,

    /// <summary>An existing binding had its <see cref="SecretBinding.SourceType"/> swapped (forces re-validation).</summary>
    SourceSwitched = 3,

    /// <summary>A validate-only action updated <see cref="SecretBinding.LastValidationResult"/>.</summary>
    Validated = 4,
}
