using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Domain.Keycloak;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Queries;

public class RunKeycloakRealmDoctorQueryHandler(
    KeycloakConnectionResolver connectionResolver,
    IKeycloakAdminClient keycloakAdminClient)
    : IQueryHandler<RunKeycloakRealmDoctorQuery, KeycloakRealmDoctorResultDto>
{
    private static readonly KeycloakOperationPolicy OperationPolicy = new();

    public async Task<KeycloakRealmDoctorResultDto> QueryAsync(
        RunKeycloakRealmDoctorQuery request,
        CancellationToken cancellationToken)
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

        var inspectionRequest = new KeycloakAdminInspectionRequest(
            connection.Authority!,
            connection.Realm!,
            connection.BlazorClientId!,
            connection.ApiClientId,
            credentials.Username,
            credentials.Password);
        KeycloakAdminInspectionResult inspection =
            await keycloakAdminClient.InspectAsync(inspectionRequest, cancellationToken);
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
        var checks = new List<KeycloakRealmDoctorCheckDto>
        {
            new()
            {
                Code = "keycloak_discovery_reachable",
                Name = "OIDC discovery",
                Status = "healthy",
                Message = "Keycloak OIDC discovery is reachable."
            }
        };
        if (inspection.Status == KeycloakInspectionStatus.PublicOnly)
        {
            checks.Add(new KeycloakRealmDoctorCheckDto
            {
                Code = "keycloak_privileged_inspection_not_requested",
                Name = "Privileged inspection",
                Status = "not-inspected",
                Message = "Client mapper state was not inspected.",
                Remediation = "Submit fresh administrator credentials only when privileged inspection is needed."
            });
        }
        else
        {
            checks.AddRange(
                Enum.GetValues<KeycloakMapperSemantic>()
                    .Select(semantic => new KeycloakRealmDoctorCheckDto
                    {
                        Code = $"keycloak_{semantic.ToString().ToLowerInvariant()}_mapper",
                        Name = $"{semantic} mapper",
                        Status = repairs.Contains(semantic) ? "needs-repair" : "healthy",
                        Message = repairs.Contains(semantic)
                            ? $"An effective {semantic.ToString().ToLowerInvariant()} mapper was not found."
                            : $"An effective {semantic.ToString().ToLowerInvariant()} mapper is assigned.",
                        Remediation = repairs.Contains(semantic)
                            ? "Review a narrow mapper operation or configure it manually in Keycloak."
                            : null
                    }));
        }

        return new KeycloakRealmDoctorResultDto
        {
            OverallStatus = repairs.Count == 0 ? "healthy" : "needs-repair",
            Message = repairs.Count == 0
                ? "Keycloak inspection found no supported mapper repair."
                : "Keycloak inspection found a supported mapper prerequisite.",
            Realm = connection.Realm!,
            Authority = connection.Authority!.AbsoluteUri.TrimEnd('/'),
            ClientId = connection.BlazorClientId!,
            ApiClientId = connection.ApiClientId,
            Checks = checks
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

    private static KeycloakRealmDoctorResultDto Blocked(
        string reasonCode,
        string message) =>
        new()
        {
            OverallStatus = "blocked",
            Message = message,
            Checks =
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
