using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Domain.Keycloak;
using Explore.Infrastructure.Services.Keycloak;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class KeycloakOperationRepairTests
{
    [Test]
    public async Task CreateSubjectMapper_UsesOnlyNarrowMapperEndpoint()
    {
        var handler = new SemanticKeycloakHandler([]);
        KeycloakMapperOperationRequest request = CreateRequest(
            PlanSubjectCreate(),
            KeycloakMapperSemantic.Subject);

        KeycloakMapperOperationResult result =
            await CreateClient(handler).ApplyApprovedMapperAsync(
                request,
                CancellationToken.None);

        await Assert.That(result.Outcome)
            .IsEqualTo(KeycloakStepOutcomeKind.Applied);
        await Assert.That(handler.MutationCount).IsEqualTo(1);
        await Assert.That(handler.ForbiddenMutationObserved).IsFalse();
        await Assert.That(handler.Mappers.Single()["protocolMapper"]!
                .GetValue<string>())
            .IsEqualTo("oidc-sub-mapper");
        await Assert.That(request.ToString())
            .DoesNotContain(request.AdministratorPassword);
    }

    [Test]
    public async Task UpdateAudienceMapper_PreservesUnknownFields()
    {
        JsonObject existing = AudienceMapper(
            id: "mapper-42",
            name: "operator-owned-audience",
            audience: "old-api");
        existing["unknownRoot"] = "preserve-root";
        ((JsonObject)existing["config"]!)["unknown.config"] =
            "preserve-config";
        var handler = new SemanticKeycloakHandler([existing]);
        KeycloakEffectiveMapperSnapshot drifted =
            ProjectAudience("mapper-42", "old-api");
        KeycloakChangeStep step = PlanAudienceUpdate(drifted);

        KeycloakMapperOperationResult result =
            await CreateClient(handler).ApplyApprovedMapperAsync(
                CreateRequest(step, KeycloakMapperSemantic.Audience),
                CancellationToken.None);

        JsonObject updated = handler.Mappers.Single();
        await Assert.That(result.Outcome)
            .IsEqualTo(KeycloakStepOutcomeKind.Applied);
        await Assert.That(updated["unknownRoot"]!.GetValue<string>())
            .IsEqualTo("preserve-root");
        await Assert.That(updated["name"]!.GetValue<string>())
            .IsEqualTo("operator-owned-audience");
        await Assert.That(
                ((JsonObject)updated["config"]!)["unknown.config"]!
                .GetValue<string>())
            .IsEqualTo("preserve-config");
        await Assert.That(
                ((JsonObject)updated["config"]!)
                ["included.client.audience"]!
                .GetValue<string>())
            .IsEqualTo("event-api");
        await Assert.That(handler.ForbiddenMutationObserved).IsFalse();
    }

    [Test]
    public async Task SameNameWrongMapperType_FailsWithoutMutation()
    {
        JsonObject collision = AudienceMapper(
            id: "wrong-type",
            name: "event-api-audience",
            audience: "event-api");
        collision["protocolMapper"] = "oidc-usermodel-attribute-mapper";
        var handler = new SemanticKeycloakHandler([collision]);

        KeycloakMapperOperationResult result =
            await CreateClient(handler).ApplyApprovedMapperAsync(
                CreateRequest(
                    PlanAudienceCreate(),
                    KeycloakMapperSemantic.Audience),
                CancellationToken.None);

        await Assert.That(result.Outcome)
            .IsEqualTo(KeycloakStepOutcomeKind.Conflict);
        await Assert.That(handler.MutationCount).IsEqualTo(0);
    }

    [Test]
    public async Task ChangedMapperUuid_FailsWithoutMutation()
    {
        JsonObject existing = AudienceMapper(
            id: "new-uuid",
            name: "event-api-audience",
            audience: "old-api");
        var handler = new SemanticKeycloakHandler([existing]);
        KeycloakChangeStep staleStep = PlanAudienceUpdate(
            ProjectAudience("old-uuid", "old-api"));

        KeycloakMapperOperationResult result =
            await CreateClient(handler).ApplyApprovedMapperAsync(
                CreateRequest(
                    staleStep,
                    KeycloakMapperSemantic.Audience),
                CancellationToken.None);

        await Assert.That(result.Outcome)
            .IsEqualTo(KeycloakStepOutcomeKind.Conflict);
        await Assert.That(handler.MutationCount).IsEqualTo(0);
    }

    [Test]
    public async Task ExternalEditBeforeWrite_FailsFingerprintPrecondition()
    {
        JsonObject externallyEdited = AudienceMapper(
            id: "mapper-42",
            name: "event-api-audience",
            audience: "external-api");
        var handler = new SemanticKeycloakHandler([externallyEdited]);
        KeycloakChangeStep approved = PlanAudienceUpdate(
            ProjectAudience("mapper-42", "old-api"));

        KeycloakMapperOperationResult result =
            await CreateClient(handler).ApplyApprovedMapperAsync(
                CreateRequest(
                    approved,
                    KeycloakMapperSemantic.Audience),
                CancellationToken.None);

        await Assert.That(result.Outcome)
            .IsEqualTo(KeycloakStepOutcomeKind.Conflict);
        await Assert.That(handler.MutationCount).IsEqualTo(0);
    }

    [Test]
    public async Task AcceptedThenTimeout_ReturnsOutcomeUnknownWithoutReplay()
    {
        var handler = new SemanticKeycloakHandler([])
        {
            AcceptMutationThenTimeout = true
        };

        KeycloakMapperOperationResult result =
            await CreateClient(handler).ApplyApprovedMapperAsync(
                CreateRequest(
                    PlanSubjectCreate(),
                    KeycloakMapperSemantic.Subject),
                CancellationToken.None);

        await Assert.That(result.Outcome)
            .IsEqualTo(KeycloakStepOutcomeKind.OutcomeUnknown);
        await Assert.That(handler.MutationCount).IsEqualTo(1);
        await Assert.That(handler.Mappers).HasCount().EqualTo(1);
    }

    private static KeycloakAdminOperationClient CreateClient(
        HttpMessageHandler handler) =>
        new(new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(5),
            MaxResponseContentBufferSize = 1024 * 1024
        });

    private static KeycloakMapperOperationRequest CreateRequest(
        KeycloakChangeStep step,
        KeycloakMapperSemantic semantic) =>
        new(
            new Uri(
                "https://identity.example.test/auth/realms/operators"),
            "operators",
            "event-bff",
            "event-api",
            semantic == KeycloakMapperSemantic.Subject
                ? "subject"
                : "event-api-audience",
            semantic,
            step,
            $"admin-{Guid.CreateVersion7():N}",
            $"password-{Guid.CreateVersion7():N}");

    private static KeycloakChangeStep PlanSubjectCreate() =>
        new KeycloakOperationService()
            .Plan(Snapshot(
            [
                new KeycloakEffectiveMapperSnapshot(
                    "inherited-audience",
                    KeycloakMapperSemantic.Audience,
                    KeycloakMapperOrigin.Inherited,
                    "event-api",
                    AddsToAccessToken: true,
                    AddsToIdToken: false,
                    IsEffective: true)
            ]))!
            .Steps
            .Single();

    private static KeycloakChangeStep PlanAudienceCreate() =>
        new KeycloakOperationService()
            .Plan(Snapshot(
            [
                new KeycloakEffectiveMapperSnapshot(
                    "native-subject",
                    KeycloakMapperSemantic.Subject,
                    KeycloakMapperOrigin.Native,
                    Audience: null,
                    AddsToAccessToken: true,
                    AddsToIdToken: true,
                    IsEffective: true)
            ]))!
            .Steps
            .Single();

    private static KeycloakChangeStep PlanAudienceUpdate(
        KeycloakEffectiveMapperSnapshot mapper) =>
        new KeycloakOperationService()
            .Plan(Snapshot(
            [
                new KeycloakEffectiveMapperSnapshot(
                    "native-subject",
                    KeycloakMapperSemantic.Subject,
                    KeycloakMapperOrigin.Native,
                    Audience: null,
                    AddsToAccessToken: true,
                    AddsToIdToken: true,
                    IsEffective: true),
                mapper
            ]))!
            .Steps
            .Single();

    private static KeycloakInspectionSnapshot Snapshot(
        IReadOnlyList<KeycloakEffectiveMapperSnapshot> mappers) =>
        new(
            "operators",
            "event-bff",
            "event-api",
            realmExists: true,
            mappers);

    private static KeycloakEffectiveMapperSnapshot ProjectAudience(
        string id,
        string audience) =>
        new(
            id,
            KeycloakMapperSemantic.Audience,
            KeycloakMapperOrigin.Direct,
            audience,
            AddsToAccessToken: true,
            AddsToIdToken: false,
            IsEffective: true);

    private static JsonObject AudienceMapper(
        string id,
        string name,
        string audience) =>
        new()
        {
            ["id"] = id,
            ["name"] = name,
            ["protocol"] = "openid-connect",
            ["protocolMapper"] = "oidc-audience-mapper",
            ["config"] = new JsonObject
            {
                ["included.client.audience"] = audience,
                ["access.token.claim"] = "true",
                ["id.token.claim"] = "false",
                ["introspection.token.claim"] = "true"
            }
        };

    private sealed class SemanticKeycloakHandler(
        IEnumerable<JsonObject> initialMappers) : HttpMessageHandler
    {
        public List<JsonObject> Mappers { get; } =
            initialMappers.Select(mapper =>
                (JsonObject)mapper.DeepClone()).ToList();

        public int MutationCount { get; private set; }

        public bool ForbiddenMutationObserved { get; private set; }

        public bool AcceptMutationThenTimeout { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post
                && path.EndsWith(
                    "/realms/master/protocol/openid-connect/token",
                    StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, """
                    { "access_token": "admin-token" }
                    """);
            }

            if (request.Method == HttpMethod.Get
                && path.EndsWith(
                    "/admin/realms/operators/clients",
                    StringComparison.Ordinal))
            {
                return Json(
                    HttpStatusCode.OK,
                    """[{ "id": "client-uuid", "clientId": "event-bff" }]""");
            }

            const string mapperPath =
                "/admin/realms/operators/clients/client-uuid/"
                + "protocol-mappers/models";
            if (request.Method == HttpMethod.Get
                && path.EndsWith(mapperPath, StringComparison.Ordinal))
            {
                return Json(
                    HttpStatusCode.OK,
                    new JsonArray(Mappers
                        .Select(mapper => mapper.DeepClone())
                        .ToArray()).ToJsonString());
            }

            if (request.Method == HttpMethod.Post
                && path.EndsWith(mapperPath, StringComparison.Ordinal))
            {
                JsonObject payload = await ReadPayloadAsync(
                    request,
                    cancellationToken);
                payload["id"] = "mapper-new";
                Mappers.Add(payload);
                MutationCount++;
                if (AcceptMutationThenTimeout)
                {
                    throw new TaskCanceledException(
                        "Provider accepted the mapper before the response was lost.");
                }

                return new HttpResponseMessage(HttpStatusCode.Created);
            }

            string mapperPrefix = $"{mapperPath}/";
            if (request.Method == HttpMethod.Put
                && path.Contains(mapperPrefix, StringComparison.Ordinal))
            {
                string id = path[(path.LastIndexOf('/') + 1)..];
                JsonObject payload = await ReadPayloadAsync(
                    request,
                    cancellationToken);
                int index = Mappers.FindIndex(mapper =>
                    string.Equals(
                        mapper["id"]?.GetValue<string>(),
                        id,
                        StringComparison.Ordinal));
                if (index < 0)
                {
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }

                Mappers[index] = payload;
                MutationCount++;
                if (AcceptMutationThenTimeout)
                {
                    throw new TaskCanceledException(
                        "Provider accepted the mapper before the response was lost.");
                }

                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (request.Method != HttpMethod.Get)
            {
                ForbiddenMutationObserved = true;
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static async Task<JsonObject> ReadPayloadAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string json = await request.Content!
                .ReadAsStringAsync(cancellationToken);
            return JsonNode.Parse(json)!.AsObject();
        }

        private static HttpResponseMessage Json(
            HttpStatusCode statusCode,
            string json) =>
            new(statusCode)
            {
                Content = new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json")
            };
    }
}
