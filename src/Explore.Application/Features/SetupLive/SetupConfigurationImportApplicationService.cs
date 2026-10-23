namespace Explore.Application.Features.SetupLive;

using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Contracts.Persistence;

/// <summary>
/// Adds ephemeral enrollment authority to the existing configuration import state machine.
/// </summary>
public sealed class SetupConfigurationImportApplicationService(
    SetupLiveApplicationService enrollment,
    ConfigurationImportSessionApplicationService sessions,
    ConfigurationImportApplyService apply,
    Func<IHumanSecuritySourceGate> humanGateFactory,
    Func<IEventDiscoveryEnrollmentGate> discoveryGateFactory)
{
    public Task<ConfigurationImportSessionCreatedResult> CreateAsync(
        Guid tenantId, Guid enrollmentId, Guid userId, string? capability,
        ReadOnlyMemory<byte> artifact, CancellationToken cancellationToken) =>
        enrollment.WithConfigurationImportAuthorityAsync(
            tenantId, enrollmentId, userId, capability,
            (generation, token) => sessions.CreateSetupLiveTenantAsync(
                ConfigurationImportTarget.ForSetupLiveTenant(
                    tenantId, enrollmentId, generation, userId),
                artifact, token),
            cancellationToken);

    public Task<ConfigurationImportPreviewResult> PreviewAsync(
        Guid tenantId, Guid enrollmentId, Guid userId, string? capability,
        Guid sessionId, string accessToken, ConfigurationImportPreviewRequest request,
        CancellationToken cancellationToken) =>
        enrollment.WithConfigurationImportAuthorityAsync(
            tenantId, enrollmentId, userId, capability,
            (generation, token) => sessions.PreviewSetupLiveTenantAsync(
                ConfigurationImportTarget.ForSetupLiveTenant(
                    tenantId, enrollmentId, generation, userId),
                sessionId, accessToken, request, token),
            cancellationToken);

    public async Task<ConfigurationImportOperationResult> ApplyAsync(
        Guid tenantId, Guid enrollmentId, Guid userId, string? capability,
        Guid sessionId, string accessToken, ConfigurationImportPreviewRequest request,
        CancellationToken cancellationToken)
    {
        var committed = await ConfigurationImportCoordination.ExecuteAsync(
            humanGateFactory, discoveryGateFactory, (requireCurrent, token) =>
                enrollment.WithConfigurationImportAuthorityAsync(
                    tenantId, enrollmentId, userId, capability,
                    (generation, ownedToken) => apply.ApplySetupLiveTenantAsync(
                        ConfigurationImportTarget.ForSetupLiveTenant(
                            tenantId, enrollmentId, generation, userId),
                        sessionId, accessToken, request, ownedToken), token), cancellationToken);
        return await apply.DeliverSetupLiveEffectsAsync(committed, cancellationToken);
    }
}
