using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Explore.Application.Contracts.Services;
using Explore.Domain.Keycloak;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Explore.Infrastructure.Services.Keycloak;

public sealed partial class KeycloakAdminClient : IKeycloakAdminClient
{
    private readonly HttpClient httpClient;
    private readonly IHostEnvironment hostEnvironment;
    private readonly IConfiguration configuration;

    public KeycloakAdminClient(
        HttpClient httpClient,
        IHostEnvironment hostEnvironment,
        IConfiguration configuration)
    {
        this.httpClient = httpClient;
        this.hostEnvironment = hostEnvironment;
        this.configuration = configuration;
    }

    private const int MaximumResponseBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<KeycloakAdminInspectionResult> InspectAsync(
        KeycloakAdminInspectionRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryValidateTarget(request, out Uri? serverBase))
        {
            return KeycloakAdminInspectionResult.Failure(
                KeycloakInspectionStatus.InvalidTarget,
                "keycloak_target_invalid");
        }

        using var timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(GetRequestTimeout());
        CancellationToken requestToken = timeoutSource.Token;

        try
        {
            using HttpResponseMessage discoveryResponse = await httpClient.GetAsync(
                new Uri(
                    $"{request.Authority.AbsoluteUri.TrimEnd('/')}/.well-known/openid-configuration",
                    UriKind.Absolute),
                HttpCompletionOption.ResponseHeadersRead,
                requestToken);
            bool realmDiscoveryMissing =
                discoveryResponse.StatusCode == HttpStatusCode.NotFound;
            if (!discoveryResponse.IsSuccessStatusCode
                && !(realmDiscoveryMissing
                    && request.HasAdministratorCredentials))
            {
                return KeycloakAdminInspectionResult.Failure(
                    discoveryResponse.StatusCode == HttpStatusCode.NotFound
                        ? KeycloakInspectionStatus.InvalidTarget
                        : KeycloakInspectionStatus.Unavailable,
                    discoveryResponse.StatusCode == HttpStatusCode.NotFound
                        ? "keycloak_discovery_not_found"
                        : "keycloak_discovery_unavailable");
            }
            if (discoveryResponse.IsSuccessStatusCode)
            {
                DiscoveryDocument? discovery =
                    await ReadBoundedJsonAsync<DiscoveryDocument>(
                        discoveryResponse.Content,
                        requestToken);
                if (discovery?.Issuer is null
                    || !Uri.TryCreate(
                        discovery.Issuer,
                        UriKind.Absolute,
                        out Uri? issuer)
                    || !SameIssuer(issuer, request.Authority))
                {
                    return KeycloakAdminInspectionResult.Failure(
                        KeycloakInspectionStatus.InvalidResponse,
                        "keycloak_discovery_issuer_invalid");
                }
            }

            var publicSnapshot = new KeycloakInspectionSnapshot(
                request.Realm,
                request.BlazorClientId,
                request.ApiClientId,
                realmExists: true);
            if (!request.HasAdministratorCredentials)
            {
                return KeycloakAdminInspectionResult.Success(
                    KeycloakInspectionStatus.PublicOnly,
                    publicSnapshot);
            }

            string? accessToken = await RequestAccessTokenAsync(
                serverBase!,
                request.AdministratorUsername!,
                request.AdministratorPassword!,
                requestToken);
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return KeycloakAdminInspectionResult.Failure(
                    KeycloakInspectionStatus.Unauthorized,
                    "keycloak_admin_unauthorized");
            }

            bool realmExists = await RealmExistsAsync(
                serverBase!,
                request.Realm,
                accessToken,
                requestToken);
            if (!realmExists)
            {
                return KeycloakAdminInspectionResult.Success(
                    KeycloakInspectionStatus.Inspected,
                    new KeycloakInspectionSnapshot(
                        request.Realm,
                        request.BlazorClientId,
                        request.ApiClientId,
                        realmExists: false,
                        blazorClient: new(
                            request.BlazorClientId,
                            0,
                            ProviderId: null,
                            Shape: null),
                        apiClient: string.IsNullOrWhiteSpace(
                            request.ApiClientId)
                            ? null
                            : new(
                                request.ApiClientId,
                                0,
                                ProviderId: null,
                                Shape: null)));
            }

            KeycloakClientInspectionSnapshot blazorClient =
                await InspectClientAsync(
                    serverBase!,
                    request.Realm,
                    request.BlazorClientId,
                    accessToken,
                    requestToken);
            KeycloakClientInspectionSnapshot? apiClient =
                string.IsNullOrWhiteSpace(request.ApiClientId)
                    ? null
                    : await InspectClientAsync(
                        serverBase!,
                        request.Realm,
                        request.ApiClientId,
                        accessToken,
                        requestToken);
            IReadOnlyList<KeycloakEffectiveMapperSnapshot> mappers =
                blazorClient.IsUnambiguous
                    ? await ReadEffectiveMappersAsync(
                    serverBase!,
                    request.Realm,
                    blazorClient.ProviderId!,
                    accessToken,
                    request.RequestedScopes,
                    requestToken)
                    : [];
            return KeycloakAdminInspectionResult.Success(
                KeycloakInspectionStatus.Inspected,
                new KeycloakInspectionSnapshot(
                    request.Realm,
                    request.BlazorClientId,
                    request.ApiClientId,
                    realmExists: true,
                    mappers,
                    blazorClient,
                    apiClient));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return KeycloakAdminInspectionResult.Failure(
                KeycloakInspectionStatus.Unavailable,
                "keycloak_request_timeout");
        }
        catch (UnauthorizedAccessException)
        {
            return KeycloakAdminInspectionResult.Failure(
                KeycloakInspectionStatus.Unauthorized,
                "keycloak_admin_forbidden");
        }
        catch (HttpRequestException)
        {
            return KeycloakAdminInspectionResult.Failure(
                KeycloakInspectionStatus.Unavailable,
                "keycloak_request_unavailable");
        }
        catch (JsonException)
        {
            return KeycloakAdminInspectionResult.Failure(
                KeycloakInspectionStatus.InvalidResponse,
                "keycloak_response_invalid");
        }
    }

    private TimeSpan GetRequestTimeout()
    {
        if (hostEnvironment.IsEnvironment("Testing")
            && int.TryParse(
                configuration["Keycloak:AdminRequestTimeoutMilliseconds"],
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out int milliseconds))
        {
            return TimeSpan.FromMilliseconds(Math.Clamp(milliseconds, 10, 1000));
        }

        return TimeSpan.FromSeconds(30);
    }

    private bool TryValidateTarget(
        KeycloakAdminInspectionRequest request,
        out Uri? serverBase)
    {
        serverBase = null;
        Uri authority = request.Authority;
        if (!IsAllowedAdminAuthority(authority)
            || !string.IsNullOrEmpty(authority.UserInfo)
            || !string.IsNullOrEmpty(authority.Query)
            || !string.IsNullOrEmpty(authority.Fragment)
            || string.IsNullOrWhiteSpace(request.Realm)
            || string.IsNullOrWhiteSpace(request.BlazorClientId))
        {
            return false;
        }

        const string marker = "/realms/";
        int markerIndex = authority.AbsolutePath.LastIndexOf(
            marker,
            StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return false;
        }

        string authorityRealm = Uri.UnescapeDataString(
            authority.AbsolutePath[(markerIndex + marker.Length)..].Trim('/'));
        if (!string.Equals(authorityRealm, request.Realm, StringComparison.Ordinal))
        {
            return false;
        }

        var builder = new UriBuilder(authority)
        {
            Path = authority.AbsolutePath[..markerIndex].TrimEnd('/'),
            Query = string.Empty,
            Fragment = string.Empty
        };
        serverBase = builder.Uri;
        return true;
    }

    private bool IsAllowedAdminAuthority(Uri authority)
    {
        if (authority.Scheme == Uri.UriSchemeHttps)
        {
            return true;
        }

        bool allowedLoopbackHttp =
            authority.Scheme == Uri.UriSchemeHttp
            && authority.IsLoopback
            && configuration.GetValue(
                "Keycloak:AllowDevelopmentLoopbackHttp",
                false)
            && (hostEnvironment.IsDevelopment()
                || hostEnvironment.IsEnvironment("Testing"));
        if (allowedLoopbackHttp)
        {
            return true;
        }

        if (authority.Scheme != Uri.UriSchemeHttp
            || !configuration.GetValue(
                "Keycloak:AllowManagedLocalHttp",
                false)
            || !Uri.TryCreate(
                configuration[
                    "Keycloak:ManagedLocalHttpOrigin"],
                UriKind.Absolute,
                out Uri? allowedOrigin)
            || allowedOrigin.Scheme != Uri.UriSchemeHttp
            || !string.IsNullOrEmpty(allowedOrigin.UserInfo)
            || !string.IsNullOrEmpty(allowedOrigin.Query)
            || !string.IsNullOrEmpty(allowedOrigin.Fragment))
        {
            return false;
        }

        const string marker = "/realms/";
        int markerIndex = authority.AbsolutePath.LastIndexOf(
            marker,
            StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return false;
        }

        return string.Equals(
                authority.Host,
                allowedOrigin.Host,
                StringComparison.OrdinalIgnoreCase)
            && authority.Port == allowedOrigin.Port
            && string.Equals(
                authority.AbsolutePath[..markerIndex]
                    .TrimEnd('/'),
                allowedOrigin.AbsolutePath.TrimEnd('/'),
                StringComparison.Ordinal);
    }

    private async Task<string?> RequestAccessTokenAsync(
        Uri serverBase,
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(
        [
            new("grant_type", "password"),
            new("client_id", "admin-cli"),
            new("username", username),
            new("password", password)
        ]);
        using HttpResponseMessage response = await httpClient.PostAsync(
            BuildUri(serverBase, "/realms/master/protocol/openid-connect/token"),
            content,
            cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        TokenResponse? payload = await ReadBoundedJsonAsync<TokenResponse>(
            response.Content,
            cancellationToken);
        return payload?.AccessToken;
    }

    private async Task<bool> RealmExistsAsync(
        Uri serverBase,
        string realm,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            BuildUri(
                serverBase,
                $"/admin/realms/{Uri.EscapeDataString(realm)}"));
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                accessToken);
        using HttpResponseMessage response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }

    private async Task<KeycloakClientInspectionSnapshot>
        InspectClientAsync(
        Uri serverBase,
        string realm,
        string clientId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        Uri uri = BuildUri(
            serverBase,
            $"/admin/realms/{Uri.EscapeDataString(realm)}/clients?clientId={Uri.EscapeDataString(clientId)}");
        IReadOnlyList<ClientRepresentation> clients =
            await GetJsonAsync<ClientRepresentation[]>(uri, accessToken, cancellationToken)
            ?? [];
        ClientRepresentation[] exact = clients
            .Where(client => string.Equals(
                client.ClientId,
                clientId,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        ClientRepresentation? client =
            exact.Length == 1 ? exact[0] : null;
        return new KeycloakClientInspectionSnapshot(
            clientId,
            exact.Length,
            client?.Id,
            client is null
                ? null
                : new KeycloakClientShape(
                    client.Enabled ?? false,
                    client.BearerOnly ?? false,
                    client.PublicClient ?? false,
                    client.StandardFlowEnabled ?? false,
                    client.DirectAccessGrantsEnabled ?? false,
                    client.ServiceAccountsEnabled ?? false));
    }

    private async Task<IReadOnlyList<KeycloakEffectiveMapperSnapshot>> ReadEffectiveMappersAsync(
        Uri serverBase,
        string realm,
        string clientUuid,
        string accessToken,
        IReadOnlyCollection<string> requestedScopes,
        CancellationToken cancellationToken)
    {
        var mappers = new List<KeycloakEffectiveMapperSnapshot>();
        Uri directMapperUri = BuildUri(
            serverBase,
            $"/admin/realms/{Uri.EscapeDataString(realm)}/clients/{Uri.EscapeDataString(clientUuid)}/protocol-mappers/models");
        MapperRepresentation[] direct =
            await GetJsonAsync<MapperRepresentation[]>(
                directMapperUri,
                accessToken,
                cancellationToken)
            ?? [];
        AddSemanticMappers(
            mappers,
            direct,
            KeycloakMapperOrigin.Direct,
            isEffective: true);

        foreach ((string scopePath, bool isDefault) in new[]
                 {
                     ("default-client-scopes", true),
                     ("optional-client-scopes", false)
                 })
        {
            Uri scopesUri = BuildUri(
                serverBase,
                $"/admin/realms/{Uri.EscapeDataString(realm)}/clients/{Uri.EscapeDataString(clientUuid)}/{scopePath}");
            ClientScopeRepresentation[] scopes =
                await GetJsonAsync<ClientScopeRepresentation[]>(
                    scopesUri,
                    accessToken,
                    cancellationToken)
                ?? [];
            foreach (ClientScopeRepresentation scope in scopes.Where(scope =>
                         !string.IsNullOrWhiteSpace(scope.Id)))
            {
                Uri scopeMappersUri = BuildUri(
                    serverBase,
                    $"/admin/realms/{Uri.EscapeDataString(realm)}/client-scopes/{Uri.EscapeDataString(scope.Id!)}/protocol-mappers/models");
                MapperRepresentation[] inherited =
                    await GetJsonAsync<MapperRepresentation[]>(
                        scopeMappersUri,
                        accessToken,
                        cancellationToken)
                    ?? [];
                AddSemanticMappers(
                    mappers,
                    inherited,
                    KeycloakMapperOrigin.Inherited,
                    isDefault
                    || (!string.IsNullOrWhiteSpace(scope.Name)
                        && requestedScopes.Contains(
                            scope.Name,
                            StringComparer.Ordinal)));
            }
        }

        return mappers;
    }

    private async Task<T?> GetJsonAsync<T>(
        Uri uri,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        using HttpResponseMessage response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new UnauthorizedAccessException();
        }

        response.EnsureSuccessStatusCode();
        return await ReadBoundedJsonAsync<T>(response.Content, cancellationToken);
    }

    private static void AddSemanticMappers(
        ICollection<KeycloakEffectiveMapperSnapshot> destination,
        IEnumerable<MapperRepresentation> mappers,
        KeycloakMapperOrigin origin,
        bool isEffective)
    {
        foreach (MapperRepresentation mapper in mappers)
        {
            IReadOnlyDictionary<string, string> config =
                mapper.Config ?? new Dictionary<string, string>(StringComparer.Ordinal);
            bool accessToken = IsTrue(config, "access.token.claim");
            bool idToken = IsTrue(config, "id.token.claim");
            string? audience = Value(config, "included.client.audience");
            bool nativeSubject =
                string.Equals(mapper.ProtocolMapper, "oidc-sub-mapper", StringComparison.Ordinal);
            bool mappedSubject =
                string.Equals(Value(config, "claim.name"), "sub", StringComparison.Ordinal)
                && (accessToken || idToken);
            if (nativeSubject || mappedSubject)
            {
                destination.Add(new KeycloakEffectiveMapperSnapshot(
                    mapper.Id ?? mapper.Name ?? "subject",
                    KeycloakMapperSemantic.Subject,
                    nativeSubject ? KeycloakMapperOrigin.Native : origin,
                    Audience: null,
                    AddsToAccessToken: accessToken,
                    AddsToIdToken: idToken,
                    IsEffective: isEffective && nativeSubject && accessToken,
                    IsConflicting: isEffective && mappedSubject && !nativeSubject,
                    Name: mapper.Name,
                    Protocol: mapper.Protocol,
                    MapperType: mapper.ProtocolMapper,
                    ClaimName: Value(config, "claim.name")));
            }

            if (string.Equals(
                    mapper.ProtocolMapper,
                    "oidc-audience-mapper",
                    StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(audience))
            {
                destination.Add(new KeycloakEffectiveMapperSnapshot(
                    mapper.Id ?? mapper.Name ?? "audience",
                    KeycloakMapperSemantic.Audience,
                    origin,
                    audience,
                    accessToken,
                    idToken,
                    IsEffective: isEffective && accessToken,
                    Name: mapper.Name,
                    Protocol: mapper.Protocol,
                    MapperType: mapper.ProtocolMapper,
                    ClaimName: Value(config, "claim.name")));
            }
        }
    }

    private static string? Value(
        IReadOnlyDictionary<string, string> values,
        string key) =>
        values.TryGetValue(key, out string? value) ? value : null;

    private static bool IsTrue(
        IReadOnlyDictionary<string, string> values,
        string key) =>
        bool.TryParse(Value(values, key), out bool result) && result;

    private static Uri BuildUri(Uri serverBase, string relativePath)
    {
        int queryIndex = relativePath.IndexOf('?', StringComparison.Ordinal);
        string path = queryIndex < 0
            ? relativePath
            : relativePath[..queryIndex];
        string query = queryIndex < 0
            ? string.Empty
            : relativePath[(queryIndex + 1)..];
        var builder = new UriBuilder(serverBase)
        {
            Path = $"{serverBase.AbsolutePath.TrimEnd('/')}/{path.TrimStart('/')}",
            Query = query,
            Fragment = string.Empty
        };
        return builder.Uri;
    }

    private static bool SameIssuer(Uri actual, Uri expected) =>
        string.Equals(
            actual.AbsoluteUri.TrimEnd('/'),
            expected.AbsoluteUri.TrimEnd('/'),
            StringComparison.Ordinal);

    private static async Task<T?> ReadBoundedJsonAsync<T>(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        await content.LoadIntoBufferAsync(MaximumResponseBytes, cancellationToken);
        return await content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken);

    private sealed record DiscoveryDocument(string? Issuer);

    private sealed record ClientRepresentation(
        string? Id,
        string? ClientId,
        bool? Enabled,
        bool? PublicClient,
        bool? BearerOnly,
        bool? StandardFlowEnabled,
        bool? DirectAccessGrantsEnabled,
        bool? ServiceAccountsEnabled);

    private sealed record ClientScopeRepresentation(string? Id, string? Name);

    private sealed record MapperRepresentation(
        string? Id,
        string? Name,
        string? Protocol,
        string? ProtocolMapper,
        IReadOnlyDictionary<string, string>? Config);
}
