namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Applies only an exact dedicated federation-setting effect of the original
/// active human preparation and its concrete issuer-owned acquired source.
/// The supplied effect is data, never an authorization or completion capability.
/// </summary>
public interface IAtprotoFederationSettingsWriter
{
    Task<AtprotoFederationSettingWriteResult> ApplyCurrentAsync(
        HumanSettingsMutation effect,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Observed write outcome for handler responses and notifications, not a source
/// receipt or permission. Nullable values distinguish absent rows and refusals;
/// a successful removal may retain the removed entity's identifier.
/// </summary>
public sealed record AtprotoFederationSettingWriteResult(
    bool Applied,
    Guid? EntityId,
    string? FailureCode,
    string? PreviousValue,
    string? CurrentValue,
    bool? PreviousLock,
    bool? CurrentLock);
