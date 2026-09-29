namespace Explore.Application.Features.SetupLive;

using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.SetupLive;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using ISLAMU.Wire.Contracts.SetupLive;

public sealed partial class SetupLiveApplicationService
{
    public async Task<bool> CanImportConfigurationAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (!IsVersion7(tenantId) || !IsVersion7(userId)
            || tenantContext.TenantId != tenantId)
            return false;

        AuthorizationDecision decision = await authorization.AuthorizeAsync(
            new AuthorizationRequest(
                ResourceKinds.TenantSetting,
                CreateTenantConfigurationImportSessionCommand.ResourceKey,
                AuthorizationActions.TenantSettings.Update,
                new AuthorizationScope(TenantId: tenantId.ToString("D")),
                new TenantSettingAuthorizationFacts(
                    tenantId, CreateTenantConfigurationImportSessionCommand.ResourceKey),
                Subject: new AuthorizationSubject(userId),
                Tenant: new AuthorizationTenant(TenantId: tenantId)),
            cancellationToken);
        return decision.IsAllowed;
    }

    public async Task<bool> ValidateConfigurationImportAsync(
        Guid tenantId,
        Guid enrollmentId,
        Guid userId,
        string? capability,
        CancellationToken cancellationToken) =>
        await AuthorizeCurrentEnrollmentAsync(
            tenantId, enrollmentId, userId, capability,
            AuthorizationActions.Tenants.Update,
            SetupEnrollmentScope.ConfigurationImport, cancellationToken) is not null
        && await CanImportConfigurationAsync(tenantId, userId, cancellationToken);

    internal async Task<T> WithConfigurationImportAuthorityAsync<T>(
        Guid tenantId,
        Guid enrollmentId,
        Guid userId,
        string? capability,
        Func<long, CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        AuthorizedEnrollment? admission = await AuthorizeCurrentEnrollmentAsync(
            tenantId, enrollmentId, userId, capability,
            AuthorizationActions.Tenants.Update,
            SetupEnrollmentScope.ConfigurationImport, cancellationToken);
        if (admission is null)
            throw UnavailableConfigurationImport();

        // Rotation and revocation share this enrollment-generation fence.
        await using IAsyncDisposable lease = await operationCoordinator.AcquireAsync(
            new SetupSecretBindingCoordinationRequest(
                tenantId, enrollmentId, admission.Enrollment.Generation),
            cancellationToken);
        if (!await ValidateConfigurationImportAsync(
                tenantId, enrollmentId, userId, capability, cancellationToken))
            throw UnavailableConfigurationImport();

        return await action(admission.Enrollment.Generation, cancellationToken);
    }

    private static ConfigurationImportSessionException UnavailableConfigurationImport() =>
        new(ConfigurationImportFailureCodes.ArtifactMissing);
}
