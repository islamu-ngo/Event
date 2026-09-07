// ABOUTME: Classifies the value-free instance bootstrap fields into exhaustive client startup states.
// ABOUTME: Consumes only canonical generated properties and fails closed for inconsistent combinations.

using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Services;

public enum InstanceOnboardingStartupDisposition
{
    Unavailable,
    InteractivePending,
    ConfiguredAdministratorPending,
    Completed
}

public sealed record InstanceOnboardingStartupStatus(
    InstanceOnboardingStartupDisposition Disposition,
    string? Provider,
    long Generation,
    bool IsAuthenticated,
    bool IsCurrentUserInstanceAdmin,
    string? SelectedDeploymentMode)
{
    public static InstanceOnboardingStartupStatus Unavailable { get; } = new(
        InstanceOnboardingStartupDisposition.Unavailable,
        Provider: null,
        Generation: 0,
        IsAuthenticated: false,
        IsCurrentUserInstanceAdmin: false,
        SelectedDeploymentMode: null);
}

internal static class InstanceOnboardingStartupStatusAdapter
{
    public static InstanceOnboardingStartupStatus FromGenerated(InstanceOnboardingStatusDto? status)
    {
        if (status is null)
        {
            return InstanceOnboardingStartupStatus.Unavailable;
        }

        string? state = status.State;
        string? mode = status.Mode;
        string? provider = status.Provider;
        long? generation = status.Generation is > 0
            ? status.Generation
            : null;

        if (generation is null)
        {
            return InstanceOnboardingStartupStatus.Unavailable;
        }

        var disposition = (status.IsCompleted, state, mode, provider) switch
        {
            (false, "InteractivePending", "Interactive", null or "Local" or "Keycloak" or "Atproto") =>
                InstanceOnboardingStartupDisposition.InteractivePending,
            (false, "ConfiguredAdministratorPending", "ConfiguredAdministrator", "Keycloak" or "Atproto") =>
                InstanceOnboardingStartupDisposition.ConfiguredAdministratorPending,
            (true, "Completed", "Interactive", null) =>
                InstanceOnboardingStartupDisposition.Completed,
            (true, "Completed", "ConfiguredAdministrator", null or "Keycloak" or "Atproto") =>
                InstanceOnboardingStartupDisposition.Completed,
            _ => InstanceOnboardingStartupDisposition.Unavailable
        };

        return disposition == InstanceOnboardingStartupDisposition.Unavailable
            ? InstanceOnboardingStartupStatus.Unavailable
            : new InstanceOnboardingStartupStatus(
                disposition,
                provider,
                generation.Value,
                status.IsAuthenticated == true,
                status.IsCurrentUserInstanceAdmin == true,
                status.SelectedDeploymentMode);
    }
}
