using Explore.Domain.Keycloak;

namespace Explore.Application.Contracts.Infrastructure;

public sealed class KeycloakMapperOperationRequest
{
    public KeycloakMapperOperationRequest(
        Uri authority,
        string realm,
        string clientId,
        string? apiClientId,
        string mapperName,
        KeycloakMapperSemantic semantic,
        KeycloakChangeStep step,
        string administratorUsername,
        string administratorPassword)
    {
        Authority = authority;
        Realm = realm;
        ClientId = clientId;
        ApiClientId = apiClientId;
        MapperName = mapperName;
        Semantic = semantic;
        Step = step;
        AdministratorUsername = administratorUsername;
        AdministratorPassword = administratorPassword;
    }

    public Uri Authority { get; }

    public string Realm { get; }

    public string ClientId { get; }

    public string? ApiClientId { get; }

    public string MapperName { get; }

    public KeycloakMapperSemantic Semantic { get; }

    public KeycloakChangeStep Step { get; }

    public string AdministratorUsername { get; }

    public string AdministratorPassword { get; }

    public override string ToString() =>
        $"{nameof(KeycloakMapperOperationRequest)} "
        + $"{{ Realm = {Realm}, ClientId = {ClientId}, "
        + $"Semantic = {Semantic}, StepId = {Step.StepId} }}";
}

public sealed record KeycloakMapperOperationResult(
    KeycloakStepOutcomeKind Outcome,
    string ReasonCode,
    string? ProviderResourceId = null,
    string? ObservedFingerprint = null);

public interface IKeycloakAdminOperationClient
{
    Task<KeycloakMapperOperationResult> ApplyApprovedMapperAsync(
        KeycloakMapperOperationRequest request,
        CancellationToken cancellationToken);

    Task<KeycloakMapperOperationResult> InspectApprovedMapperAsync(
        KeycloakMapperOperationRequest request,
        CancellationToken cancellationToken);
}
