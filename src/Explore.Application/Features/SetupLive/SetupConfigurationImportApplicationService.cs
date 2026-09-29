namespace Explore.Application.Features.SetupLive;

using Explore.Application.Features.ConfigurationManifest.Importing;

/// <summary>
/// Adds ephemeral enrollment authority to the existing configuration import state machine.
/// </summary>
public sealed class SetupConfigurationImportApplicationService(
    SetupLiveApplicationService enrollment,
    ConfigurationImportSessionApplicationService sessions,
    ConfigurationImportApplyService apply)
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

    public Task<ConfigurationImportOperationResult> ApplyAsync(
        Guid tenantId, Guid enrollmentId, Guid userId, string? capability,
        Guid sessionId, string accessToken, ConfigurationImportPreviewRequest request,
        CancellationToken cancellationToken) =>
        enrollment.WithConfigurationImportAuthorityAsync(
            tenantId, enrollmentId, userId, capability,
            (generation, token) => apply.ApplySetupLiveTenantAsync(
                ConfigurationImportTarget.ForSetupLiveTenant(
                    tenantId, enrollmentId, generation, userId),
                sessionId, accessToken, request, token),
            cancellationToken);
}
