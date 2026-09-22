namespace Explore.Application.Contracts.Identity;

public sealed record KeycloakOperatorAuthority(
    Guid InstanceId,
    string Actor,
    long SetupGeneration,
    bool IsSetupAuthority);

public interface IKeycloakOperatorAuthority
{
    Task<KeycloakOperatorAuthority> RequireAsync(
        CancellationToken cancellationToken = default);
}
