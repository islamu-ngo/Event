using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Domain.Keycloak;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Queries;

public class PreviewKeycloakRealmSyncQueryHandler(
    KeycloakConnectionResolver connectionResolver,
    IKeycloakAdminClient keycloakAdminClient,
    IKeycloakRealmDesiredStateBuilder desiredStateBuilder)
    : IQueryHandler<PreviewKeycloakRealmSyncQuery, KeycloakRealmSyncPlanDto>
{
    private static readonly KeycloakOperationPolicy OperationPolicy = new();

    public async Task<KeycloakRealmSyncPlanDto> QueryAsync(PreviewKeycloakRealmSyncQuery request, CancellationToken cancellationToken)
    {
        KeycloakConnectionResolution connection =
            await connectionResolver.ResolveRuntimeAsync(cancellationToken);
        if (connection.Status != KeycloakConnectionStatus.Resolved)
        {
            return Blocked(
                connection.Status.ToString().ToLowerInvariant(),
                "The selected Keycloak deployment binding is not available.");
        }
        if (!MatchesRuntimeAudience(
                request.Request.ApiClientId,
                connection.ApiClientId))
        {
            return Blocked(
                "keycloak_target_conflict",
                "The requested API audience does not match the deployment-owned Keycloak target.");
        }

        KeycloakAdministratorCredentialResolution credentials =
            await connectionResolver.ResolveAdministratorCredentialsAsync(
                request.Request.UseTemporaryAdminCredentials
                    ? request.Request.BootstrapAdminUsername
                    : null,
                request.Request.UseTemporaryAdminCredentials
                    ? request.Request.BootstrapAdminPassword
                    : null,
                cancellationToken);
        if (request.Request.UseTemporaryAdminCredentials
            && credentials.Status != KeycloakAdministratorCredentialStatus.Resolved)
        {
            return Blocked(
                "keycloak_admin_credentials_invalid",
                "Fresh administrator credentials are required for privileged inspection.");
        }

        string? apiClientId = connection.ApiClientId;
        KeycloakAdminInspectionResult inspection =
            await keycloakAdminClient.InspectAsync(
                new KeycloakAdminInspectionRequest(
                    connection.Authority!,
                    connection.Realm!,
                    connection.BlazorClientId!,
                    apiClientId,
                    credentials.Username,
                    credentials.Password),
                cancellationToken);
        if (inspection.Snapshot is null)
        {
            return Blocked(
                inspection.ReasonCode,
                "Keycloak inspection could not be completed.");
        }
        if (OperationPolicy.HasConflictingMapper(
                inspection.Snapshot,
                KeycloakMapperSemantic.Subject))
        {
            return Blocked(
                "keycloak_subject_mapper_conflict",
                "A conflicting subject claim producer requires manual Keycloak repair.");
        }

        IReadOnlyList<KeycloakMapperSemantic> repairs =
            inspection.Status == KeycloakInspectionStatus.PublicOnly
                ? []
                : OperationPolicy.GetRequiredMapperRepairs(inspection.Snapshot);
        IReadOnlyList<KeycloakRealmSyncOperationDto> operations =
            inspection.Status == KeycloakInspectionStatus.PublicOnly
                ? []
                : repairs.Select(semantic => new KeycloakRealmSyncOperationDto
                {
                    OperationId = $"keycloak-{semantic.ToString().ToLowerInvariant()}-mapper-review",
                    Category = "mapper",
                    TargetType = "client",
                    Target = connection.BlazorClientId!,
                    Action = "review",
                    Status = "needs-review",
                    Summary = $"Review the missing {semantic.ToString().ToLowerInvariant()} mapper.",
                    Reason = "Only a narrow mapper operation can be automated; unrelated client and realm settings remain operator-owned."
                }).ToArray();

        return new KeycloakRealmSyncPlanDto
        {
            Status = inspection.Status == KeycloakInspectionStatus.PublicOnly
                ? "inspected-public"
                : operations.Count == 0
                    ? "no-changes"
                    : "review-required",
            Message = inspection.Status == KeycloakInspectionStatus.PublicOnly
                ? "Public discovery succeeded. Privileged mapper state was not inspected."
                : operations.Count == 0
                    ? "No supported mapper repair is required."
                    : "Review the supported mapper changes before applying them.",
            Realm = connection.Realm!,
            Authority = connection.Authority!.AbsoluteUri.TrimEnd('/'),
            ClientId = connection.BlazorClientId!,
            ApiClientId = apiClientId,
            DestructiveOperationsSupported = false,
            DesiredState = desiredStateBuilder.Build(new KeycloakRealmDesiredStateBuildRequestDto
            {
                Realm = connection.Realm!,
                BlazorClientId = connection.BlazorClientId!,
                ApiClientId = apiClientId,
                BlazorRedirectUris = request.Request.BlazorRedirectUris,
                BlazorWebOrigins = request.Request.BlazorWebOrigins
            }),
            Operations = operations
        };
    }

    private static bool MatchesRuntimeAudience(
        string? requestedApiClientId,
        string? runtimeApiClientId)
    {
        if (requestedApiClientId is null)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(requestedApiClientId)
            && string.Equals(
                requestedApiClientId.Trim(),
                runtimeApiClientId,
                StringComparison.Ordinal);
    }

    private static KeycloakRealmSyncPlanDto Blocked(
        string reasonCode,
        string message) =>
        new()
        {
            Status = "blocked",
            Message = message,
            DestructiveOperationsSupported = false,
            Diagnostics =
            [
                new KeycloakRealmDoctorCheckDto
                {
                    Code = reasonCode,
                    Name = "Keycloak inspection",
                    Status = "blocked",
                    Message = message
                }
            ]
        };
}
