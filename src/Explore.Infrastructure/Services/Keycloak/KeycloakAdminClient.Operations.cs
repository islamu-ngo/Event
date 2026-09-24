using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Domain.Keycloak;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Explore.Infrastructure.Services.Keycloak;

public sealed partial class KeycloakAdminClient
{
    public Task<KeycloakMapperOperationResult> ApplyApprovedMapperAsync(
        KeycloakMapperOperationRequest request,
        CancellationToken cancellationToken) =>
        ExecuteMapperAsync(request, allowWrite: true, cancellationToken);

    public Task<KeycloakMapperOperationResult> InspectApprovedMapperAsync(
        KeycloakMapperOperationRequest request,
        CancellationToken cancellationToken) =>
        ExecuteMapperAsync(request, allowWrite: false, cancellationToken);

    public Task<KeycloakProvisioningOperationResult>
        ApplyApprovedProvisioningAsync(
            KeycloakProvisioningOperationRequest request,
            CancellationToken cancellationToken) =>
        ExecuteProvisioningAsync(
            request,
            allowWrite: true,
            cancellationToken);

    public Task<KeycloakProvisioningOperationResult>
        InspectApprovedProvisioningAsync(
            KeycloakProvisioningOperationRequest request,
            CancellationToken cancellationToken) =>
        ExecuteProvisioningAsync(
            request,
            allowWrite: false,
            cancellationToken);

    private async Task<KeycloakProvisioningOperationResult>
        ExecuteProvisioningAsync(
            KeycloakProvisioningOperationRequest request,
            bool allowWrite,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!TryGetServerBase(
                request.Authority,
                request.Realm,
                out Uri? serverBase)
            || request.Step.Desired is null
            || request.Step.Kind is not (
                KeycloakStep.CreateRealm
                or KeycloakStep.CreateClient))
        {
            return ProvisioningFailed("keycloak_target_invalid");
        }

        string? accessToken = await RequestAccessTokenAsync(
            serverBase!,
            request.AdministratorUsername,
            request.AdministratorPassword,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return ProvisioningFailed(
                "keycloak_administrator_unauthorized");
        }

        return request.Step.Kind == KeycloakStep.CreateRealm
            ? await ExecuteRealmCreateAsync(
                request,
                serverBase!,
                accessToken,
                allowWrite,
                cancellationToken)
            : await ExecuteClientCreateAsync(
                request,
                serverBase!,
                accessToken,
                allowWrite,
                cancellationToken);
    }

    private async Task<KeycloakProvisioningOperationResult>
        ExecuteRealmCreateAsync(
            KeycloakProvisioningOperationRequest request,
            Uri serverBase,
            string accessToken,
            bool allowWrite,
            CancellationToken cancellationToken)
    {
        Uri resourceUri = BuildUri(
            serverBase,
            $"/admin/realms/{Uri.EscapeDataString(request.Realm)}");
        (HttpStatusCode Status, JsonObject? Resource) existing =
            await GetObjectResponseAsync(
                resourceUri,
                accessToken,
                cancellationToken);
        if (!allowWrite)
        {
            if (request.ProviderResourceId is null)
            {
                return ProvisioningUnknown();
            }

            return existing.Status == HttpStatusCode.OK
                && existing.Resource is not null
                && string.Equals(
                    StringValue(existing.Resource, "id"),
                    request.ProviderResourceId,
                    StringComparison.Ordinal)
                && string.Equals(
                    request.Step.Desired!.ProviderResourceId,
                    request.ProviderResourceId,
                    StringComparison.Ordinal)
                && MatchesDesiredRealm(
                    request.Step.Desired,
                    existing.Resource)
                ? ProvisioningVerified(
                    request.ProviderResourceId,
                    request.Step.DesiredFingerprint)
                : ProvisioningConflict(
                    "keycloak_realm_reconciliation_conflict");
        }

        if (existing.Status != HttpStatusCode.NotFound)
        {
            return existing.Status == HttpStatusCode.OK
                ? ProvisioningConflict("keycloak_realm_already_exists")
                : ProvisioningFailed("keycloak_realm_preflight_failed");
        }

        var payload = new JsonObject
        {
            ["id"] = request.Step.Desired!.ProviderResourceId,
            ["realm"] = request.Step.Desired!.ResourceName,
            ["enabled"] = true
        };
        KeycloakProvisioningOperationResult send =
            await SendCreateAsync(
                BuildUri(serverBase, "/admin/realms"),
                payload,
                accessToken,
                providerResourceId:
                    request.Step.Desired.ProviderResourceId,
                cancellationToken);
        if (send.Outcome != KeycloakStepOutcomeKind.Applied)
        {
            return send;
        }

        try
        {
            (HttpStatusCode Status, JsonObject? Resource) verified =
                await GetObjectResponseAsync(
                    resourceUri,
                    accessToken,
                    cancellationToken);
            if (verified.Status != HttpStatusCode.OK
                || verified.Resource is null
                || !MatchesDesiredRealm(
                    request.Step.Desired,
                    verified.Resource))
            {
                return ProvisioningUnknown(
                    request.Step.Desired.ProviderResourceId);
            }

            string? providerResourceId =
                StringValue(verified.Resource, "id");
            if (string.IsNullOrWhiteSpace(providerResourceId)
                || !string.Equals(
                    providerResourceId,
                    request.Step.Desired.ProviderResourceId,
                    StringComparison.Ordinal))
            {
                return ProvisioningUnknown(
                    request.Step.Desired.ProviderResourceId);
            }

            return ProvisioningApplied(
                providerResourceId,
                request.Step.DesiredFingerprint);
        }
        catch (OperationCanceledException)
        {
            return ProvisioningUnknown(
                request.Step.Desired.ProviderResourceId);
        }
        catch (HttpRequestException)
        {
            return ProvisioningUnknown(
                request.Step.Desired.ProviderResourceId);
        }
    }

    private async Task<KeycloakProvisioningOperationResult>
        ExecuteClientCreateAsync(
            KeycloakProvisioningOperationRequest request,
            Uri serverBase,
            string accessToken,
            bool allowWrite,
            CancellationToken cancellationToken)
    {
        KeycloakDesiredProjection desired = request.Step.Desired!;
        if (desired.Kind
                == KeycloakDesiredKind.ConfidentialBffClient
            && string.IsNullOrEmpty(request.RuntimeClientSecret))
        {
            return ProvisioningFailed(
                "keycloak_runtime_client_secret_unavailable");
        }

        if (!allowWrite)
        {
            if (request.ProviderResourceId is null)
            {
                return ProvisioningUnknown();
            }

            (HttpStatusCode Status, JsonObject? Resource) observed =
                await GetObjectResponseAsync(
                    BuildUri(
                        serverBase,
                        $"/admin/realms/{Uri.EscapeDataString(request.Realm)}"
                        + $"/clients/{Uri.EscapeDataString(request.ProviderResourceId)}"),
                    accessToken,
                    cancellationToken);
            return observed.Status == HttpStatusCode.OK
                && observed.Resource is not null
                && string.Equals(
                    StringValue(observed.Resource, "id"),
                    desired.ProviderResourceId,
                    StringComparison.Ordinal)
                && MatchesDesiredClient(desired, observed.Resource)
                ? ProvisioningVerified(
                    request.ProviderResourceId,
                    request.Step.DesiredFingerprint)
                : ProvisioningConflict(
                    "keycloak_client_reconciliation_conflict");
        }

        JsonObject[] candidates = await GetObjectsAsync(
            BuildUri(
                serverBase,
                $"/admin/realms/{Uri.EscapeDataString(request.Realm)}"
                + $"/clients?clientId={Uri.EscapeDataString(desired.ResourceName)}"),
            accessToken,
            cancellationToken);
        if (candidates.Any(candidate =>
                string.Equals(
                    StringValue(candidate, "clientId"),
                    desired.ResourceName,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return ProvisioningConflict("keycloak_client_already_exists");
        }

        JsonObject payload = BuildClientPayload(
            desired,
            request.RuntimeClientSecret);
        KeycloakProvisioningOperationResult send =
            await SendCreateAsync(
                BuildUri(
                    serverBase,
                    $"/admin/realms/{Uri.EscapeDataString(request.Realm)}/clients"),
                payload,
                accessToken,
                providerResourceId:
                    desired.ProviderResourceId,
                cancellationToken);
        if (send.Outcome != KeycloakStepOutcomeKind.Applied
            || send.ProviderResourceId is null)
        {
            return send.Outcome == KeycloakStepOutcomeKind.Applied
                ? ProvisioningUnknown()
                : send;
        }

        try
        {
            (HttpStatusCode Status, JsonObject? Resource) verified =
                await GetObjectResponseAsync(
                    BuildUri(
                        serverBase,
                        $"/admin/realms/{Uri.EscapeDataString(request.Realm)}"
                        + $"/clients/{Uri.EscapeDataString(send.ProviderResourceId)}"),
                    accessToken,
                    cancellationToken);
            return verified.Status == HttpStatusCode.OK
                && verified.Resource is not null
                && string.Equals(
                    StringValue(verified.Resource, "id"),
                    desired.ProviderResourceId,
                    StringComparison.Ordinal)
                && MatchesDesiredClient(desired, verified.Resource)
                ? ProvisioningApplied(
                    send.ProviderResourceId,
                    request.Step.DesiredFingerprint)
                : ProvisioningUnknown(send.ProviderResourceId);
        }
        catch (OperationCanceledException)
        {
            return ProvisioningUnknown(
                send.ProviderResourceId);
        }
        catch (HttpRequestException)
        {
            return ProvisioningUnknown(send.ProviderResourceId);
        }
    }

    private async Task<KeycloakMapperOperationResult> ExecuteMapperAsync(
        KeycloakMapperOperationRequest request,
        bool allowWrite,
        CancellationToken cancellationToken)
    {
        if (!TryGetServerBase(
                request.Authority,
                request.Realm,
                out Uri? serverBase)
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
            return request.Step.Kind
                == KeycloakStep.CreateMapper
                ? Unknown(
                    request.Step.Desired.ProviderResourceId)
                : Failed("keycloak_mapper_not_effective");
        }

        JsonObject payload = request.Step.Kind == KeycloakStep.CreateMapper
            ? CreateMapperPayload(request)
            : MergeMapperPayload(match.Mapper!, request);
        string? mutationProviderId =
            request.Step.Kind == KeycloakStep.CreateMapper
                ? request.Step.Desired.ProviderResourceId
                : match.ProviderResourceId;
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
                    return Unknown(mutationProviderId);
                }

                return Failed("keycloak_mapper_write_rejected");
            }
        }
        catch (OperationCanceledException)
        {
            return Unknown(mutationProviderId);
        }
        catch (HttpRequestException)
        {
            return Unknown(mutationProviderId);
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
            return Unknown(mutationProviderId);
        }
        catch (HttpRequestException)
        {
            return Unknown(mutationProviderId);
        }

        JsonObject? applied = FindVerifiedMapper(request, verified);
        if (applied is null)
        {
            return Unknown(mutationProviderId);
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
        if (request.Semantic == KeycloakMapperSemantic.Subject
            && mappers.Any(IsConflictingSubjectProducer))
        {
            return Conflict("keycloak_mapper_collision");
        }

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
            JsonObject? byPlannedId =
                mappers.SingleOrDefault(mapper =>
                    string.Equals(
                        ProviderResourceId(mapper),
                        request.Step.Desired.ProviderResourceId,
                        StringComparison.Ordinal));
            if (byPlannedId is not null)
            {
                if (allowWrite
                    || !string.Equals(
                        StringValue(byPlannedId, "name"),
                        request.MapperName,
                        StringComparison.Ordinal)
                    || !IsDesiredMapper(
                        request,
                        byPlannedId))
                {
                    return Conflict(
                        "keycloak_mapper_collision");
                }

                KeycloakEffectiveMapperSnapshot projection =
                    ProjectMapper(
                        byPlannedId,
                        KeycloakMapperOrigin.Direct);
                return new MapperMatch(
                    byPlannedId,
                    ProviderResourceId(byPlannedId),
                    new KeycloakMapperOperationResult(
                        KeycloakStepOutcomeKind.Verified,
                        "keycloak_mapper_verified",
                        ProviderResourceId(byPlannedId),
                        KeycloakOperationService.MapperFingerprint(projection)));
            }

            if (semanticallyEffective.Length > 0 || sameName.Length > 0)
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
        if (!string.Equals(
                request.Step.ExpectedIdentityFingerprint,
                KeycloakOperationService.MapperIdentityFingerprint(current),
                StringComparison.Ordinal))
        {
            return Conflict("keycloak_mapper_identity_changed");
        }

        bool desired = IsDesiredMapper(request, byId);
        if (!allowWrite && desired)
        {
            return new MapperMatch(
                byId,
                ProviderResourceId(byId),
                new KeycloakMapperOperationResult(
                    KeycloakStepOutcomeKind.Verified,
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

        if (desired)
        {
            return new MapperMatch(
                byId,
                ProviderResourceId(byId),
                new KeycloakMapperOperationResult(
                    KeycloakStepOutcomeKind.NoChange,
                    "keycloak_mapper_already_effective",
                    ProviderResourceId(byId),
                    currentFingerprint));
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
            ["id"] = request.Step.Desired.ProviderResourceId,
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
                KeycloakOperationService.MapperSemanticFingerprint(projected),
                request.Step.DesiredFingerprint,
                StringComparison.Ordinal);
    }

    private static bool IsConflictingSubjectProducer(JsonObject mapper)
    {
        JsonObject config = mapper["config"] as JsonObject ?? [];
        return !string.Equals(
                StringValue(mapper, "protocolMapper"),
                "oidc-sub-mapper",
                StringComparison.Ordinal)
            && string.Equals(
                StringValue(config, "claim.name"),
                "sub",
                StringComparison.Ordinal)
            && (IsTrue(config, "access.token.claim")
                || IsTrue(config, "id.token.claim"));
    }

    private static JsonObject? FindVerifiedMapper(
        KeycloakMapperOperationRequest request,
        IEnumerable<JsonObject> mappers) =>
        mappers.SingleOrDefault(mapper =>
            IsDesiredMapper(request, mapper)
            && (request.Step.Kind == KeycloakStep.CreateMapper
                ? string.Equals(
                    ProviderResourceId(mapper),
                    request.Step.Desired.ProviderResourceId,
                    StringComparison.Ordinal)
                  && string.Equals(
                      StringValue(mapper, "name"),
                      request.MapperName,
                      StringComparison.Ordinal)
                : string.Equals(
                    ProviderResourceId(mapper),
                    request.Step.TargetId,
                    StringComparison.Ordinal)
                  && string.Equals(
                      request.Step.ExpectedIdentityFingerprint,
                      KeycloakOperationService.MapperIdentityFingerprint(
                          ProjectMapper(
                              mapper,
                              KeycloakMapperOrigin.Direct)),
                      StringComparison.Ordinal)));

    private static KeycloakEffectiveMapperSnapshot ProjectMapper(
        JsonObject mapper,
        KeycloakMapperOrigin origin)
    {
        string mapperType = StringValue(mapper, "protocolMapper") ?? string.Empty;
        JsonObject config = mapper["config"] as JsonObject ?? [];
        string? claimName = StringValue(config, "claim.name");
        bool mappedSubject = string.Equals(
            claimName,
            "sub",
            StringComparison.Ordinal);
        KeycloakMapperSemantic semantic =
            mapperType == "oidc-sub-mapper" || mappedSubject
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
            IsEffective: true,
            Name: StringValue(mapper, "name"),
            Protocol: StringValue(mapper, "protocol"),
            MapperType: mapperType,
            ClaimName: claimName);
    }

    private async Task<KeycloakProvisioningOperationResult>
        SendCreateAsync(
            Uri uri,
            JsonObject payload,
            string accessToken,
            string? providerResourceId,
            CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        };
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using HttpResponseMessage response =
                await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
            string? createdId = providerResourceId
                ?? ResourceIdFromLocation(response.Headers.Location);
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                return ProvisioningConflict(
                    "keycloak_resource_already_exists");
            }

            if ((int)response.StatusCode >= 500
                || response.StatusCode == HttpStatusCode.RequestTimeout)
            {
                return ProvisioningUnknown(createdId);
            }

            return response.IsSuccessStatusCode
                ? new KeycloakProvisioningOperationResult(
                    KeycloakStepOutcomeKind.Applied,
                    "keycloak_resource_create_accepted",
                    createdId)
                : ProvisioningFailed(
                    "keycloak_resource_create_rejected");
        }
        catch (OperationCanceledException)
        {
            return ProvisioningUnknown(providerResourceId);
        }
        catch (HttpRequestException)
        {
            return ProvisioningUnknown(providerResourceId);
        }
    }

    private async Task<(HttpStatusCode Status, JsonObject? Resource)>
        GetObjectResponseAsync(
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
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return (response.StatusCode, null);
        }

        await response.Content.LoadIntoBufferAsync(
            MaximumResponseBytes,
            cancellationToken);
        JsonObject? resource =
            await response.Content.ReadFromJsonAsync<JsonObject>(
                JsonOptions,
                cancellationToken);
        return (response.StatusCode, resource);
    }

    private static JsonObject BuildClientPayload(
        KeycloakDesiredProjection desired,
        string? runtimeClientSecret)
    {
        bool confidential =
            desired.Kind == KeycloakDesiredKind.ConfidentialBffClient;
        var payload = new JsonObject
        {
            ["id"] = desired.ProviderResourceId,
            ["clientId"] = desired.ResourceName,
            ["name"] = desired.ResourceName,
            ["enabled"] = true,
            ["publicClient"] = false,
            ["bearerOnly"] = !confidential,
            ["standardFlowEnabled"] = confidential,
            ["directAccessGrantsEnabled"] = false,
            ["serviceAccountsEnabled"] = false,
            ["redirectUris"] = new JsonArray(
                desired.RedirectUris
                    .Select(uri => JsonValue.Create(uri))
                    .ToArray()),
            ["webOrigins"] = new JsonArray(
                desired.WebOrigins
                    .Select(origin => JsonValue.Create(origin))
                    .ToArray())
        };
        if (confidential)
        {
            payload["secret"] = runtimeClientSecret;
        }

        return payload;
    }

    private static bool MatchesDesiredRealm(
        KeycloakDesiredProjection desired,
        JsonObject resource) =>
        desired.Kind == KeycloakDesiredKind.Realm
        && string.Equals(
            StringValue(resource, "realm"),
            desired.ResourceName,
            StringComparison.Ordinal)
        && BoolValue(resource, "enabled");

    private static bool MatchesDesiredClient(
        KeycloakDesiredProjection desired,
        JsonObject resource)
    {
        bool confidential =
            desired.Kind == KeycloakDesiredKind.ConfidentialBffClient;
        return desired.Kind is KeycloakDesiredKind.ConfidentialBffClient
                or KeycloakDesiredKind.BearerOnlyApiClient
            && string.Equals(
                StringValue(resource, "clientId"),
                desired.ResourceName,
                StringComparison.Ordinal)
            && BoolValue(resource, "enabled")
            && !BoolValue(resource, "publicClient")
            && BoolValue(resource, "bearerOnly") == !confidential
            && BoolValue(resource, "standardFlowEnabled") == confidential
            && !BoolValue(resource, "directAccessGrantsEnabled")
            && !BoolValue(resource, "serviceAccountsEnabled")
            && JsonStrings(resource, "redirectUris")
                .SequenceEqual(
                    desired.RedirectUris,
                    StringComparer.Ordinal)
            && JsonStrings(resource, "webOrigins")
                .SequenceEqual(
                    desired.WebOrigins,
                    StringComparer.Ordinal);
    }

    private static string? ResourceIdFromLocation(Uri? location)
    {
        if (location is null)
        {
            return null;
        }

        string segment = location.IsAbsoluteUri
            ? location.Segments[^1]
            : location.OriginalString.Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries)[^1];
        return Uri.UnescapeDataString(segment.Trim('/'));
    }

    private static bool BoolValue(JsonObject source, string key) =>
        source[key] is JsonValue value
        && value.TryGetValue(out bool result)
        && result;

    private static IReadOnlyList<string> JsonStrings(
        JsonObject source,
        string key) =>
        source[key] is JsonArray values
            ? values
                .Select(value => value?.GetValue<string>() ?? string.Empty)
                .ToArray()
            : [];

    private static KeycloakProvisioningOperationResult ProvisioningApplied(
        string providerResourceId,
        string fingerprint) =>
        new(
            KeycloakStepOutcomeKind.Applied,
            "keycloak_resource_verified",
            providerResourceId,
            fingerprint);

    private static KeycloakProvisioningOperationResult ProvisioningVerified(
        string providerResourceId,
        string fingerprint) =>
        new(
            KeycloakStepOutcomeKind.Verified,
            "keycloak_resource_verified",
            providerResourceId,
            fingerprint);

    private static KeycloakProvisioningOperationResult ProvisioningConflict(
        string reasonCode) =>
        new(KeycloakStepOutcomeKind.Conflict, reasonCode);

    private static KeycloakProvisioningOperationResult ProvisioningFailed(
        string reasonCode) =>
        new(KeycloakStepOutcomeKind.FailedBeforeWrite, reasonCode);

    private static KeycloakProvisioningOperationResult ProvisioningUnknown(
        string? providerResourceId = null) =>
        new(
            KeycloakStepOutcomeKind.OutcomeUnknown,
            "keycloak_resource_outcome_unknown",
            providerResourceId);

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
        Uri authority,
        string realm,
        out Uri? serverBase)
    {
        serverBase = null;
        if (!authority.IsAbsoluteUri
            || !IsAllowedAdminAuthority(authority)
            || !string.IsNullOrEmpty(authority.UserInfo)
            || !string.IsNullOrEmpty(authority.Query)
            || !string.IsNullOrEmpty(authority.Fragment))
        {
            return false;
        }

        const string marker = "/realms/";
        int markerIndex = authority.AbsolutePath.LastIndexOf(
            marker,
            StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0
            || !string.Equals(
                Uri.UnescapeDataString(
                    authority.AbsolutePath[
                        (markerIndex + marker.Length)..].Trim('/')),
                realm,
                StringComparison.Ordinal))
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
