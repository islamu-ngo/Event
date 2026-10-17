namespace Explore.Persistence;

public partial class ExploreDbContext
{
    private readonly List<(Func<CancellationToken, Task> Sources, Action Time)> trustedSetupSettingsAdmissions = [];

    internal void RegisterTrustedSetupSettingsAdmission(
        Func<CancellationToken, Task> requireSources, Action requireTime) =>
        trustedSetupSettingsAdmissions.Add((requireSources, requireTime));

    /// <summary>
    /// Called by the original transaction owner before final human source/time validation.
    /// Registration is not a human grant or a settings write proof.
    /// </summary>
    internal async Task ValidateTrustedSetupSettingsSourcesAsync(CancellationToken cancellationToken)
    {
        foreach (var admission in trustedSetupSettingsAdmissions)
            await admission.Sources(cancellationToken);
    }

    /// <summary>Sample after every blocking source check and immediately before physical commit.</summary>
    internal void ValidateTrustedSetupSettingsTimes()
    {
        foreach (var admission in trustedSetupSettingsAdmissions)
            admission.Time();
    }

    internal void ResetTrustedSetupSettingsAdmissions() => trustedSetupSettingsAdmissions.Clear();
}
