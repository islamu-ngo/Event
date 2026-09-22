using Explore.Domain.Keycloak;

namespace Explore.Application.Contracts.Services;

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

public sealed class KeycloakProvisioningOperationRequest
{
    public KeycloakProvisioningOperationRequest(
        Uri authority,
        string realm,
        string blazorClientId,
        string? apiClientId,
        KeycloakChangeStep step,
        string? runtimeClientSecret,
        string administratorUsername,
        string administratorPassword,
        string? providerResourceId = null)
    {
        Authority = authority;
        Realm = realm;
        BlazorClientId = blazorClientId;
        ApiClientId = apiClientId;
        Step = step;
        RuntimeClientSecret = runtimeClientSecret;
        AdministratorUsername = administratorUsername;
        AdministratorPassword = administratorPassword;
        ProviderResourceId = providerResourceId;
    }

    public Uri Authority { get; }
    public string Realm { get; }
    public string BlazorClientId { get; }
    public string? ApiClientId { get; }
    public KeycloakChangeStep Step { get; }
    public string? RuntimeClientSecret { get; }
    public string AdministratorUsername { get; }
    public string AdministratorPassword { get; }
    public string? ProviderResourceId { get; }

    public override string ToString() =>
        $"{nameof(KeycloakProvisioningOperationRequest)} "
        + $"{{ Realm = {Realm}, StepId = {Step.StepId}, "
        + $"Kind = {Step.Kind} }}";
}

public sealed record KeycloakProvisioningOperationResult(
    KeycloakStepOutcomeKind Outcome,
    string ReasonCode,
    string? ProviderResourceId = null,
    string? ObservedFingerprint = null);
