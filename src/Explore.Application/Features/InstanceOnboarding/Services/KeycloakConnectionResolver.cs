using Explore.Application.Contracts.Secrets;
using Explore.Domain.Secrets;
using Microsoft.Extensions.Configuration;

namespace Explore.Application.Features.InstanceOnboarding.Services;

public enum KeycloakConnectionStatus
{
    Resolved = 0,
    Unconfigured = 1,
    Unavailable = 2,
    Unauthorized = 3,
    Invalid = 4
}

public sealed class KeycloakConnectionResolution
{
    private KeycloakConnectionResolution(
        KeycloakConnectionStatus status,
        Uri? authority,
        string? realm,
        string? blazorClientId,
        string? apiClientId,
        string? clientSecret)
    {
        Status = status;
        Authority = authority;
        Realm = realm;
        BlazorClientId = blazorClientId;
        ApiClientId = apiClientId;
        ClientSecret = clientSecret;
    }

    public KeycloakConnectionStatus Status { get; }

    public Uri? Authority { get; }

    public string? Realm { get; }

    public string? BlazorClientId { get; }

    public string? ApiClientId { get; }

    public string? ClientSecret { get; }

    public static KeycloakConnectionResolution Resolved(
        Uri authority,
        string realm,
        string blazorClientId,
        string? apiClientId,
        string clientSecret) =>
        new(
            KeycloakConnectionStatus.Resolved,
            authority,
            realm,
            blazorClientId,
            apiClientId,
            clientSecret);

    public static KeycloakConnectionResolution Failed(KeycloakConnectionStatus status) =>
        status == KeycloakConnectionStatus.Resolved
            ? throw new ArgumentOutOfRangeException(nameof(status))
            : new(status, null, null, null, null, null);

    public override string ToString() =>
        $"{nameof(KeycloakConnectionResolution)} {{ Status = {Status} }}";
}

public enum KeycloakAdministratorCredentialStatus
{
    Resolved = 0,
    Missing = 1,
    Invalid = 2
}

public sealed class KeycloakAdministratorCredentialResolution
{
    private KeycloakAdministratorCredentialResolution(
        KeycloakAdministratorCredentialStatus status,
        string? username,
        string? password)
    {
        Status = status;
        Username = username;
        Password = password;
    }

    public KeycloakAdministratorCredentialStatus Status { get; }

    public string? Username { get; }

    public string? Password { get; }

    public static KeycloakAdministratorCredentialResolution Resolved(
        string username,
        string password) =>
        new(KeycloakAdministratorCredentialStatus.Resolved, username, password);

    public static KeycloakAdministratorCredentialResolution Missing { get; } =
        new(KeycloakAdministratorCredentialStatus.Missing, null, null);

    public static KeycloakAdministratorCredentialResolution Invalid { get; } =
        new(KeycloakAdministratorCredentialStatus.Invalid, null, null);

    public override string ToString() =>
        $"{nameof(KeycloakAdministratorCredentialResolution)} {{ Status = {Status} }}";
}

public sealed class KeycloakConnectionResolver(
    ISecretResolver secretResolver,
    IConfiguration? configuration = null)
{
    private readonly ISecretResolver _secretResolver = secretResolver;
    private readonly IConfiguration? _configuration = configuration;

    public async Task<KeycloakConnectionResolution> ResolveRuntimeAsync(
        CancellationToken cancellationToken = default)
    {
        SecretResolutionResult endpoint = await _secretResolver.ResolveAsync(
            SecretDefinitionRegistry.Keys.Keycloak.Endpoint,
            tenantId: null,
            cancellationToken);
        SecretResolutionResult realm = await _secretResolver.ResolveAsync(
            SecretDefinitionRegistry.Keys.Keycloak.Realm,
            tenantId: null,
            cancellationToken);
        SecretResolutionResult clientId = await _secretResolver.ResolveAsync(
            SecretDefinitionRegistry.Keys.Keycloak.ClientId,
            tenantId: null,
            cancellationToken);
        SecretResolutionResult clientSecret = await _secretResolver.ResolveAsync(
            SecretDefinitionRegistry.Keys.Keycloak.BlazorClientSecret,
            tenantId: null,
            cancellationToken);

        SecretResolutionResult[] values = [endpoint, realm, clientId, clientSecret];
        if (values.Any(result => !result.IsResolved))
        {
            return KeycloakConnectionResolution.Failed(MapFailure(values));
        }

        string endpointValue = endpoint.Value!.Trim();
        string realmValue = realm.Value!.Trim();
        string clientIdValue = clientId.Value!.Trim();
        string secretValue = clientSecret.Value!;
        string? apiClientId = _configuration?["Keycloak:Audience"]?.Trim();

        if (!TryBuildAuthority(endpointValue, realmValue, out Uri? authority)
            || string.IsNullOrWhiteSpace(clientIdValue)
            || string.IsNullOrEmpty(secretValue)
            || (!string.IsNullOrWhiteSpace(apiClientId)
                && string.Equals(clientIdValue, apiClientId, StringComparison.OrdinalIgnoreCase)))
        {
            return KeycloakConnectionResolution.Failed(KeycloakConnectionStatus.Invalid);
        }

        return KeycloakConnectionResolution.Resolved(
            authority!,
            realmValue,
            clientIdValue,
            apiClientId,
            secretValue);
    }

    public Task<KeycloakAdministratorCredentialResolution> ResolveAdministratorCredentialsAsync(
        string? username,
        string? password,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        bool hasUsername = !string.IsNullOrWhiteSpace(username);
        bool hasPassword = !string.IsNullOrWhiteSpace(password);
        if (!hasUsername && !hasPassword)
        {
            return Task.FromResult(KeycloakAdministratorCredentialResolution.Missing);
        }

        if (!hasUsername || !hasPassword)
        {
            return Task.FromResult(KeycloakAdministratorCredentialResolution.Invalid);
        }

        return Task.FromResult(KeycloakAdministratorCredentialResolution.Resolved(
            username!.Trim(),
            password!));
    }

    private static KeycloakConnectionStatus MapFailure(
        IReadOnlyCollection<SecretResolutionResult> results)
    {
        if (results.Any(result => result.Status == SecretResolutionStatus.Unauthorized))
        {
            return KeycloakConnectionStatus.Unauthorized;
        }

        if (results.Any(result => result.Status == SecretResolutionStatus.Unavailable))
        {
            return KeycloakConnectionStatus.Unavailable;
        }

        if (results.Any(result => result.Status == SecretResolutionStatus.Invalid))
        {
            return KeycloakConnectionStatus.Invalid;
        }

        return KeycloakConnectionStatus.Unconfigured;
    }

    private static bool TryBuildAuthority(
        string endpoint,
        string realm,
        out Uri? authority)
    {
        authority = null;
        if (string.IsNullOrWhiteSpace(realm)
            || !Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? baseUri)
            || !string.IsNullOrEmpty(baseUri.UserInfo)
            || !string.IsNullOrEmpty(baseUri.Query)
            || !string.IsNullOrEmpty(baseUri.Fragment))
        {
            return false;
        }

        var builder = new UriBuilder(baseUri)
        {
            Path = $"{baseUri.AbsolutePath.TrimEnd('/')}/realms/{Uri.EscapeDataString(realm)}"
        };
        authority = builder.Uri;
        return true;
    }
}
