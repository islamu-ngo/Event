using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Domain.Keycloak;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Explore.Infrastructure.Services.Keycloak;

public sealed class KeycloakAdminOperationClient(
    HttpClient httpClient,
    IHostEnvironment hostEnvironment,
    IConfiguration configuration)
    : IKeycloakAdminOperationClient
{
    private const int MaximumResponseBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public Task<KeycloakMapperOperationResult> ApplyApprovedMapperAsync(
        KeycloakMapperOperationRequest request,
        CancellationToken cancellationToken) =>
        ExecuteMapperAsync(request, allowWrite: true, cancellationToken);

    public Task<KeycloakMapperOperationResult> InspectApprovedMapperAsync(
        KeycloakMapperOperationRequest request,
        CancellationToken cancellationToken) =>
        ExecuteMapperAsync(request, allowWrite: false, cancellationToken);

    private async Task<KeycloakMapperOperationResult> ExecuteMapperAsync(
        KeycloakMapperOperationRequest request,
        bool allowWrite,
        CancellationToken cancellationToken)
    {
        if (!TryGetServerBase(request, out Uri? serverBase)
            || request.Step.Kind is not (
                KeycloakStep.CreateMapper or KeycloakStep.UpdateMapper))
        {
            return Failed("keycloak_mapper_target_invalid");
        }

        string? accessToken = await RequestAccessTokenAsync(
            serverBase!,
            request.AdministratorUsername,
            request.AdministratorPassword,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return Failed("keycloak_admin_unauthorized");
        }

        ClientResource[] clients = await GetAsync<ClientResource[]>(
            BuildUri(
                serverBase!,
                $"/admin/realms/{Uri.EscapeDataString(request.Realm)}"
                + $"/clients?clientId={Uri.EscapeDataString(request.ClientId)}"),
            accessToken,
            cancellationToken)
            ?? [];
        ClientResource[] exactClients = clients
            .Where(client => string.Equals(
                client.ClientId,
                request.ClientId,
                StringComparison.Ordinal))
            .ToArray();
        if (exactClients.Length != 1
            || string.IsNullOrWhiteSpace(exactClients[0].Id))
        {
            return Failed(
                exactClients.Length > 1
                    ? "keycloak_client_ambiguous"
                    : "keycloak_client_absent");
        }

        string clientUuid = exactClients[0].Id!;
        Uri mapperCollection = BuildUri(
            serverBase!,
            $"/admin/realms/{Uri.EscapeDataString(request.Realm)}"
            + $"/clients/{Uri.EscapeDataString(clientUuid)}"
            + "/protocol-mappers/models");
        JsonObject[] mappers = await GetObjectsAsync(
            mapperCollection,
            accessToken,
            cancellationToken);
        MapperMatch match = MatchMapper(
            request,
            mappers,
            allowWrite);
        if (match.Result is not null)
        {
            return match.Result;
        }

        if (!allowWrite)
        {
            return Failed("keycloak_mapper_not_effective");
        }

        JsonObject payload = request.Step.Kind == KeycloakStep.CreateMapper
            ? CreateMapperPayload(request)
            : MergeMapperPayload(match.Mapper!, request);
        cancellationToken.ThrowIfCancellationRequested();

        HttpResponseMessage? mutationResponse = null;
        try
        {
            using var mutation = new HttpRequestMessage(
                request.Step.Kind == KeycloakStep.CreateMapper
                    ? HttpMethod.Post
                    : HttpMethod.Put,
                request.Step.Kind == KeycloakStep.CreateMapper
                    ? mapperCollection
                    : BuildUri(
                        serverBase!,
                        $"/admin/realms/{Uri.EscapeDataString(request.Realm)}"
                        + $"/clients/{Uri.EscapeDataString(clientUuid)}"
                        + "/protocol-mappers/models/"
                        + Uri.EscapeDataString(match.ProviderResourceId!)))
            {
                Content = JsonContent.Create(payload, options: JsonOptions)
            };
            mutation.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken);
            mutationResponse = await httpClient.SendAsync(
                mutation,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!mutationResponse.IsSuccessStatusCode)
            {
                if ((int)mutationResponse.StatusCode >= 500
                    || mutationResponse.StatusCode
                        == HttpStatusCode.RequestTimeout)
                {
                    return Unknown(match.ProviderResourceId);
                }

                return Failed("keycloak_mapper_write_rejected");
            }
        }
        catch (OperationCanceledException)
        {
            return Unknown(match.ProviderResourceId);
        }
        catch (HttpRequestException)
        {
            return Unknown(match.ProviderResourceId);
        }
        finally
        {
            mutationResponse?.Dispose();
        }

        JsonObject[] verified;
        try
        {
            verified = await GetObjectsAsync(
                mapperCollection,
                accessToken,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return Unknown(match.ProviderResourceId);
        }
        catch (HttpRequestException)
        {
            return Unknown(match.ProviderResourceId);
        }

        JsonObject? applied = FindVerifiedMapper(request, verified);
        if (applied is null)
        {
            return Unknown(match.ProviderResourceId);
        }

        KeycloakEffectiveMapperSnapshot projection =
            ProjectMapper(applied, KeycloakMapperOrigin.Direct);
        return new KeycloakMapperOperationResult(
            KeycloakStepOutcomeKind.Applied,
            "keycloak_mapper_verified",
            ProviderResourceId(applied),
            KeycloakOperationService.MapperFingerprint(projection));
    }

    private static MapperMatch MatchMapper(
        KeycloakMapperOperationRequest request,
        IReadOnlyCollection<JsonObject> mappers,
        bool allowWrite)
    {
        JsonObject[] sameName = mappers
            .Where(mapper => string.Equals(
                StringValue(mapper, "name"),
                request.MapperName,
                StringComparison.Ordinal))
            .ToArray();
        JsonObject[] semanticallyEffective = mappers
            .Where(mapper => IsDesiredMapper(request, mapper))
            .ToArray();

        if (request.Step.Kind == KeycloakStep.CreateMapper)
        {
            if (semanticallyEffective.Length == 1)
            {
                JsonObject existing = semanticallyEffective[0];
                KeycloakEffectiveMapperSnapshot projection =
                    ProjectMapper(existing, KeycloakMapperOrigin.Direct);
                return new MapperMatch(
                    existing,
                    ProviderResourceId(existing),
                    new KeycloakMapperOperationResult(
                        allowWrite
                            ? KeycloakStepOutcomeKind.NoChange
                            : KeycloakStepOutcomeKind.Verified,
                        "keycloak_mapper_already_effective",
                        ProviderResourceId(existing),
                        KeycloakOperationService.MapperFingerprint(projection)));
            }

            if (semanticallyEffective.Length > 1 || sameName.Length > 0)
            {
                return Conflict("keycloak_mapper_collision");
            }

            return new MapperMatch(null, null, null);
        }

        JsonObject? byId = mappers.SingleOrDefault(mapper =>
            string.Equals(
                ProviderResourceId(mapper),
                request.Step.TargetId,
                StringComparison.Ordinal));
        if (byId is null
            || sameName.Any(mapper => !ReferenceEquals(mapper, byId)))
        {
            return Conflict("keycloak_mapper_identity_changed");
        }

        KeycloakEffectiveMapperSnapshot current =
            ProjectMapper(byId, KeycloakMapperOrigin.Direct);
        string currentFingerprint =
            KeycloakOperationService.MapperFingerprint(current);
        if (IsDesiredMapper(request, byId))
        {
            return new MapperMatch(
                byId,
                ProviderResourceId(byId),
                new KeycloakMapperOperationResult(
                    allowWrite
                        ? KeycloakStepOutcomeKind.NoChange
                        : KeycloakStepOutcomeKind.Verified,
                    "keycloak_mapper_already_effective",
                    ProviderResourceId(byId),
                    currentFingerprint));
        }

        if (!string.Equals(
                request.Step.ExpectedFingerprint,
                currentFingerprint,
                StringComparison.Ordinal))
        {
            return Conflict("keycloak_mapper_precondition_failed");
        }

        return new MapperMatch(
            byId,
            ProviderResourceId(byId),
            Result: null);
    }

    private static JsonObject CreateMapperPayload(
        KeycloakMapperOperationRequest request) =>
        new()
        {
            ["name"] = request.MapperName,
            ["protocol"] = "openid-connect",
            ["protocolMapper"] = MapperType(request.Semantic),
            ["config"] = DesiredConfig(request)
        };

    private static JsonObject MergeMapperPayload(
        JsonObject source,
        KeycloakMapperOperationRequest request)
    {
        var merged = (JsonObject)source.DeepClone();
        JsonObject config = merged["config"] as JsonObject ?? [];
        foreach ((string key, JsonNode? value) in DesiredConfig(request))
        {
            config[key] = value?.DeepClone();
        }

        merged["config"] = config;
        return merged;
    }

    private static JsonObject DesiredConfig(
        KeycloakMapperOperationRequest request) =>
        request.Semantic == KeycloakMapperSemantic.Subject
            ? new JsonObject
            {
                ["access.token.claim"] = "true",
                ["id.token.claim"] = "true"
            }
            : new JsonObject
            {
                ["included.client.audience"] = request.ApiClientId,
                ["access.token.claim"] = "true",
                ["id.token.claim"] = "false"
            };

    private static string MapperType(KeycloakMapperSemantic semantic) =>
        semantic == KeycloakMapperSemantic.Subject
            ? "oidc-sub-mapper"
            : "oidc-audience-mapper";

    private static bool IsDesiredMapper(
        KeycloakMapperOperationRequest request,
        JsonObject mapper)
    {
        KeycloakEffectiveMapperSnapshot projected =
            ProjectMapper(mapper, KeycloakMapperOrigin.Direct);
        return string.Equals(
                StringValue(mapper, "protocol"),
                "openid-connect",
                StringComparison.Ordinal)
            && string.Equals(
                StringValue(mapper, "protocolMapper"),
                MapperType(request.Semantic),
                StringComparison.Ordinal)
            && projected.Semantic == request.Semantic
            && string.Equals(
                KeycloakOperationService.MapperFingerprint(projected),
                request.Step.DesiredFingerprint,
                StringComparison.Ordinal);
    }

    private static JsonObject? FindVerifiedMapper(
        KeycloakMapperOperationRequest request,
        IEnumerable<JsonObject> mappers) =>
        mappers.SingleOrDefault(mapper =>
            IsDesiredMapper(request, mapper)
            && (request.Step.Kind == KeycloakStep.CreateMapper
                ? string.Equals(
                    StringValue(mapper, "name"),
                    request.MapperName,
                    StringComparison.Ordinal)
                : string.Equals(
                    ProviderResourceId(mapper),
                    request.Step.TargetId,
                    StringComparison.Ordinal)));

    private static KeycloakEffectiveMapperSnapshot ProjectMapper(
        JsonObject mapper,
        KeycloakMapperOrigin origin)
    {
        string mapperType = StringValue(mapper, "protocolMapper") ?? string.Empty;
        JsonObject config = mapper["config"] as JsonObject ?? [];
        KeycloakMapperSemantic semantic =
            mapperType == "oidc-sub-mapper"
                ? KeycloakMapperSemantic.Subject
                : KeycloakMapperSemantic.Audience;
        string? audience = semantic == KeycloakMapperSemantic.Audience
            ? StringValue(config, "included.client.audience")
            : null;
        return new KeycloakEffectiveMapperSnapshot(
            ProviderResourceId(mapper) ?? string.Empty,
            semantic,
            origin,
            audience,
            IsTrue(config, "access.token.claim"),
            IsTrue(config, "id.token.claim"),
            IsEffective: true);
    }

    private async Task<string?> RequestAccessTokenAsync(
        Uri serverBase,
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            BuildUri(
                serverBase,
                "/realms/master/protocol/openid-connect/token"))
        {
            Content = new FormUrlEncodedContent(
            [
                new("grant_type", "password"),
                new("client_id", "admin-cli"),
                new("username", username),
                new("password", password)
            ])
        };
        using HttpResponseMessage response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized
            or HttpStatusCode.Forbidden)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(
            MaximumResponseBytes,
            cancellationToken);
        using JsonDocument payload = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        return payload.RootElement.TryGetProperty(
            "access_token",
            out JsonElement token)
            ? token.GetString()
            : null;
    }

    private async Task<T?> GetAsync<T>(
        Uri uri,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);
        using HttpResponseMessage response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(
            MaximumResponseBytes,
            cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(
            JsonOptions,
            cancellationToken);
    }

    private async Task<JsonObject[]> GetObjectsAsync(
        Uri uri,
        string accessToken,
        CancellationToken cancellationToken) =>
        await GetAsync<JsonObject[]>(
            uri,
            accessToken,
            cancellationToken)
        ?? [];

    private bool TryGetServerBase(
        KeycloakMapperOperationRequest request,
        out Uri? serverBase)
    {
        serverBase = null;
        bool secure = request.Authority.Scheme == Uri.UriSchemeHttps;
        bool allowedLoopbackHttp =
            request.Authority.Scheme == Uri.UriSchemeHttp
            && request.Authority.IsLoopback
            && configuration.GetValue(
                "Keycloak:AllowDevelopmentLoopbackHttp",
                false)
            && (hostEnvironment.IsDevelopment()
                || hostEnvironment.IsEnvironment("Testing"));
        if (!request.Authority.IsAbsoluteUri
            || (!secure && !allowedLoopbackHttp)
            || !string.IsNullOrEmpty(request.Authority.UserInfo)
            || !string.IsNullOrEmpty(request.Authority.Query)
            || !string.IsNullOrEmpty(request.Authority.Fragment))
        {
            return false;
        }

        const string marker = "/realms/";
        int markerIndex = request.Authority.AbsolutePath.LastIndexOf(
            marker,
            StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0
            || !string.Equals(
                Uri.UnescapeDataString(
                    request.Authority.AbsolutePath[
                        (markerIndex + marker.Length)..].Trim('/')),
                request.Realm,
                StringComparison.Ordinal))
        {
            return false;
        }

        var builder = new UriBuilder(request.Authority)
        {
            Path = request.Authority.AbsolutePath[..markerIndex].TrimEnd('/'),
            Query = string.Empty,
            Fragment = string.Empty
        };
        serverBase = builder.Uri;
        return true;
    }

    private static Uri BuildUri(Uri serverBase, string relativePath)
    {
        int queryIndex = relativePath.IndexOf('?', StringComparison.Ordinal);
        string path = queryIndex < 0
            ? relativePath
            : relativePath[..queryIndex];
        string query = queryIndex < 0
            ? string.Empty
            : relativePath[(queryIndex + 1)..];
        return new UriBuilder(serverBase)
        {
            Path = $"{serverBase.AbsolutePath.TrimEnd('/')}/{path.TrimStart('/')}",
            Query = query,
            Fragment = string.Empty
        }.Uri;
    }

    private static string? StringValue(JsonObject source, string key) =>
        source[key]?.GetValue<string>();

    private static string? ProviderResourceId(JsonObject mapper) =>
        StringValue(mapper, "id");

    private static bool IsTrue(JsonObject config, string key) =>
        bool.TryParse(StringValue(config, key), out bool result) && result;

    private static KeycloakMapperOperationResult Failed(string reasonCode) =>
        new(
            KeycloakStepOutcomeKind.FailedBeforeWrite,
            reasonCode);

    private static KeycloakMapperOperationResult Unknown(
        string? providerResourceId) =>
        new(
            KeycloakStepOutcomeKind.OutcomeUnknown,
            "keycloak_mapper_outcome_unknown",
            providerResourceId);

    private static MapperMatch Conflict(string reasonCode) =>
        new(
            Mapper: null,
            ProviderResourceId: null,
            Result: new KeycloakMapperOperationResult(
                KeycloakStepOutcomeKind.Conflict,
                reasonCode));

    private sealed record ClientResource(string? Id, string? ClientId);

    private sealed record MapperMatch(
        JsonObject? Mapper,
        string? ProviderResourceId,
        KeycloakMapperOperationResult? Result);
}
