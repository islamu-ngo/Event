using Explore.Application.DTOs.Onboarding;

namespace Explore.Application.Contracts.Services;

public interface IKeycloakBootstrapService
{
    Task<KeycloakBootstrapResultDto> BootstrapAsync(
        KeycloakBootstrapRequestDto request,
        CancellationToken cancellationToken);

    Task<KeycloakRealmDoctorResultDto> DiagnoseRealmAsync(
        AuthProviderConfigurationDto configuration,
        KeycloakRealmDoctorRequestDto request,
        CancellationToken cancellationToken);

    Task<KeycloakRealmSyncPlanDto> PreviewRealmSyncAsync(
        AuthProviderConfigurationDto configuration,
        KeycloakRealmSyncPreviewRequestDto request,
        CancellationToken cancellationToken);

    Task<KeycloakRealmSyncPlanDto> ApplyRealmSyncAsync(
        AuthProviderConfigurationDto configuration,
        KeycloakRealmSyncApplyRequestDto request,
        CancellationToken cancellationToken);

    Task<KeycloakClientSecretRotationResultDto> RotateClientSecretAsync(
        AuthProviderConfigurationDto configuration,
        KeycloakClientSecretRotationRequestDto request,
        CancellationToken cancellationToken);
}
