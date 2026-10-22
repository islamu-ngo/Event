using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Security;

namespace Explore.Persistence.Services;

/// <summary>
/// Applies an exact effect only through its preparation's current concrete source.
/// </summary>
public sealed class AtprotoFederationSettingsWriter(
    AtprotoFederationSettingsMutationStore store,
    IHumanSettingsMutationPreparation preparation) : IAtprotoFederationSettingsWriter
{
    public Task<AtprotoFederationSettingWriteResult> ApplyCurrentAsync(
        HumanSettingsMutation effect,
        CancellationToken cancellationToken = default) =>
        store.ApplyCurrentAsync(preparation.CurrentAtprotoFederationSource
            ?? throw new UnauthorizedAccessException("The original acquired federation source is required."),
            effect, cancellationToken);
}
