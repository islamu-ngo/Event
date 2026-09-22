using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Domain.Keycloak;
using Explore.Infrastructure.Services.Keycloak;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NSubstitute;

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
            audience: "event-api",
            accessToken: false,
            introspectionToken: false);
        existing["unknownRoot"] = "preserve-root";
        ((JsonObject)existing["config"]!)["unknown.config"] =
            "preserve-config";
        var handler = new SemanticKeycloakHandler([existing]);
        KeycloakEffectiveMapperSnapshot drifted =
            ProjectAudience(
                "mapper-42",
                "event-api",
                accessToken: false,
                name: "operator-owned-audience");
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
        await Assert.That(
                ((JsonObject)updated["config"]!)
                ["introspection.token.claim"]!
                .GetValue<string>())
            .IsEqualTo("false");
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
    public async Task CreateAudienceMapper_WhenDifferentNamedEquivalentAppears_Fails()
    {
        JsonObject external = AudienceMapper(
            id: "external-audience",
            name: "operator-owned-audience",
            audience: "event-api");
        var handler = new SemanticKeycloakHandler([external]);

        KeycloakMapperOperationResult result =
            await CreateClient(handler).ApplyApprovedMapperAsync(
                CreateRequest(
                    PlanAudienceCreate(),
                    KeycloakMapperSemantic.Audience),
                CancellationToken.None);

        await Assert.That(result.Outcome)
            .IsEqualTo(KeycloakStepOutcomeKind.Conflict);
        await Assert.That(result.ProviderResourceId).IsNull();
        await Assert.That(handler.MutationCount).IsEqualTo(0);
    }

    [Test]
    public async Task ChangedMapperUuid_FailsWithoutMutation()
    {
        JsonObject existing = AudienceMapper(
            id: "new-uuid",
            name: "event-api-audience",
            audience: "event-api",
            accessToken: false);
        var handler = new SemanticKeycloakHandler([existing]);
        KeycloakChangeStep staleStep = PlanAudienceUpdate(
            ProjectAudience(
                "old-uuid",
                "event-api",
                accessToken: false));

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
            audience: "event-api",
            accessToken: false,
            idToken: true);
        var handler = new SemanticKeycloakHandler([externallyEdited]);
        KeycloakChangeStep approved = PlanAudienceUpdate(
            ProjectAudience(
                "mapper-42",
                "event-api",
                accessToken: false,
                name: "operator-owned-audience"));

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
    public async Task ExternalMapperTypeDrift_FailsFingerprintPrecondition()
    {
        JsonObject externallyEdited = AudienceMapper(
            id: "mapper-42",
            name: "event-api-audience",
            audience: "event-api",
            accessToken: false);
        externallyEdited["protocolMapper"] =
            "oidc-usermodel-attribute-mapper";
        var handler = new SemanticKeycloakHandler([externallyEdited]);
        KeycloakChangeStep approved = PlanAudienceUpdate(
            ProjectAudience(
                "mapper-42",
                "event-api",
                accessToken: false,
                name: "operator-owned-audience"));

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
    public async Task ExternalMapperRenameAtDesiredState_FailsBeforeWrite()
    {
        JsonObject externallyRenamed = AudienceMapper(
            id: "mapper-42",
            name: "renamed-after-preview",
            audience: "event-api");
        var handler = new SemanticKeycloakHandler([externallyRenamed]);
        KeycloakChangeStep approved = PlanAudienceUpdate(
            ProjectAudience(
                "mapper-42",
                "event-api",
                accessToken: false,
                name: "operator-owned-audience"));

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
    public async Task ExternalDesiredStateBeforeApply_RequiresNewApproval()
    {
        JsonObject externallyCompleted = AudienceMapper(
            id: "mapper-42",
            name: "operator-owned-audience",
            audience: "event-api");
        var handler = new SemanticKeycloakHandler([externallyCompleted]);
        KeycloakChangeStep approved = PlanAudienceUpdate(
            ProjectAudience(
                "mapper-42",
                "event-api",
                accessToken: false,
                name: "operator-owned-audience"));

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
    public async Task NewDifferentlyNamedSubjectClaimProducer_FailsCreate()
    {
        var conflictingProducer = new JsonObject
        {
            ["id"] = "external-sub",
            ["name"] = "operator-subject-claim",
            ["protocol"] = "openid-connect",
            ["protocolMapper"] = "oidc-usermodel-attribute-mapper",
            ["config"] = new JsonObject
            {
                ["claim.name"] = "sub",
                ["access.token.claim"] = "true",
                ["id.token.claim"] = "true"
            }
        };
        var handler = new SemanticKeycloakHandler(
            [conflictingProducer]);

        KeycloakMapperOperationResult result =
            await CreateClient(handler).ApplyApprovedMapperAsync(
                CreateRequest(
                    PlanSubjectCreate(),
                    KeycloakMapperSemantic.Subject),
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

    [Test]
    public async Task AcceptedThenServerError_ReturnsOutcomeUnknown()
    {
        var handler = new SemanticKeycloakHandler([])
        {
            AcceptMutationThenServerError = true
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

    [Test]
    public async Task ReadBackIdentityDrift_AfterAcceptedUpdate_RemainsUnknown()
    {
        JsonObject existing = AudienceMapper(
            id: "mapper-42",
            name: "operator-owned-audience",
            audience: "event-api",
            accessToken: false);
        var handler = new SemanticKeycloakHandler([existing])
        {
            RenameAfterMutation = true
        };
        KeycloakChangeStep approved = PlanAudienceUpdate(
            ProjectAudience(
                "mapper-42",
                "event-api",
                accessToken: false,
                name: "operator-owned-audience"));

        KeycloakMapperOperationResult result =
            await CreateClient(handler).ApplyApprovedMapperAsync(
                CreateRequest(
                    approved,
                    KeycloakMapperSemantic.Audience),
                CancellationToken.None);

        await Assert.That(result.Outcome)
            .IsEqualTo(KeycloakStepOutcomeKind.OutcomeUnknown);
        await Assert.That(handler.MutationCount).IsEqualTo(1);
    }

    [Test]
    public async Task ReconcileCompletedUpdate_ReportsVerifiedWithoutMutation()
    {
        JsonObject applied = AudienceMapper(
            id: "mapper-42",
            name: "operator-owned-audience",
            audience: "event-api");
        var handler = new SemanticKeycloakHandler([applied]);
        KeycloakChangeStep approved = PlanAudienceUpdate(
            ProjectAudience(
                "mapper-42",
                "event-api",
                accessToken: false,
                name: "operator-owned-audience"));

        KeycloakMapperOperationResult result =
            await CreateClient(handler).InspectApprovedMapperAsync(
                CreateRequest(
                    approved,
                    KeycloakMapperSemantic.Audience),
                CancellationToken.None);

        await Assert.That(result.Outcome)
            .IsEqualTo(KeycloakStepOutcomeKind.Verified);
        await Assert.That(result.ProviderResourceId)
            .IsEqualTo("mapper-42");
        await Assert.That(handler.MutationCount).IsEqualTo(0);
    }

    [Test]
    public async Task ReconcileCompletedUpdate_WhenMapperRenamed_ReportsConflict()
    {
        JsonObject appliedButRenamed = AudienceMapper(
            id: "mapper-42",
            name: "renamed-after-preview",
            audience: "event-api");
        var handler = new SemanticKeycloakHandler(
            [appliedButRenamed]);
        KeycloakChangeStep approved = PlanAudienceUpdate(
            ProjectAudience(
                "mapper-42",
                "event-api",
                accessToken: false,
                name: "operator-owned-audience"));

        KeycloakMapperOperationResult result =
            await CreateClient(handler).InspectApprovedMapperAsync(
                CreateRequest(
                    approved,
                    KeycloakMapperSemantic.Audience),
                CancellationToken.None);

        await Assert.That(result.Outcome)
            .IsEqualTo(KeycloakStepOutcomeKind.Conflict);
        await Assert.That(handler.MutationCount).IsEqualTo(0);
    }

    [Test]
    public async Task ProductionLoopbackHttp_IsRejectedBeforeCredentialForwarding()
    {
        var handler = new SemanticKeycloakHandler([]);

        KeycloakMapperOperationResult result =
            await CreateClient(handler).ApplyApprovedMapperAsync(
                CreateRequest(
                    PlanSubjectCreate(),
                    KeycloakMapperSemantic.Subject,
                    new Uri(
                        "http://127.0.0.1:8080/auth/realms/operators")),
                CancellationToken.None);

        await Assert.That(result.Outcome)
            .IsEqualTo(KeycloakStepOutcomeKind.FailedBeforeWrite);
        await Assert.That(handler.RequestCount).IsEqualTo(0);
    }

    [Test]
    public async Task TestingLoopbackHttp_RequiresExplicitOptIn()
    {
        var handler = new SemanticKeycloakHandler([]);

        KeycloakMapperOperationResult result =
            await CreateClient(
                    handler,
                    Environments.Development,
                    allowLoopbackHttp: true)
                .ApplyApprovedMapperAsync(
                    CreateRequest(
                        PlanSubjectCreate(),
                        KeycloakMapperSemantic.Subject,
                        new Uri(
                            "http://127.0.0.1:8080/auth/realms/operators")),
                    CancellationToken.None);

        await Assert.That(result.Outcome)
            .IsEqualTo(KeycloakStepOutcomeKind.Applied);
        await Assert.That(handler.MutationCount).IsEqualTo(1);
    }

    private static KeycloakAdminClient CreateClient(
        HttpMessageHandler handler,
        string environmentName = "Production",
        bool allowLoopbackHttp = false)
    {
        IHostEnvironment environment =
            Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(environmentName);
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Keycloak:AllowDevelopmentLoopbackHttp"] =
                    allowLoopbackHttp.ToString()
            })
            .Build();
        return new KeycloakAdminClient(
            new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(5),
                MaxResponseContentBufferSize = 1024 * 1024
            },
            environment,
            configuration);
    }

    private static KeycloakMapperOperationRequest CreateRequest(
        KeycloakChangeStep step,
        KeycloakMapperSemantic semantic,
        Uri? authority = null) =>
        new(
            authority ?? new Uri(
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
        string audience,
        bool accessToken = true,
        bool idToken = false,
        string name = "event-api-audience") =>
        new(
            id,
            KeycloakMapperSemantic.Audience,
            KeycloakMapperOrigin.Direct,
            audience,
            AddsToAccessToken: accessToken,
            AddsToIdToken: idToken,
            IsEffective: true,
            Name: name,
            Protocol: "openid-connect",
            MapperType: "oidc-audience-mapper");

    private static JsonObject AudienceMapper(
        string id,
        string name,
        string audience,
        bool accessToken = true,
        bool idToken = false,
        bool introspectionToken = true) =>
        new()
        {
            ["id"] = id,
            ["name"] = name,
            ["protocol"] = "openid-connect",
            ["protocolMapper"] = "oidc-audience-mapper",
            ["config"] = new JsonObject
            {
                ["included.client.audience"] = audience,
                ["access.token.claim"] =
                    accessToken ? "true" : "false",
                ["id.token.claim"] =
                    idToken ? "true" : "false",
                ["introspection.token.claim"] =
                    introspectionToken ? "true" : "false"
            }
        };

    private sealed class SemanticKeycloakHandler(
        IEnumerable<JsonObject> initialMappers) : HttpMessageHandler
    {
        public List<JsonObject> Mappers { get; } =
            initialMappers.Select(mapper =>
                (JsonObject)mapper.DeepClone()).ToList();

        public int MutationCount { get; private set; }

        public int RequestCount { get; private set; }

        public bool ForbiddenMutationObserved { get; private set; }

        public bool AcceptMutationThenTimeout { get; init; }

        public bool AcceptMutationThenServerError { get; init; }

        public bool RenameAfterMutation { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
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

                if (AcceptMutationThenServerError)
                {
                    return new HttpResponseMessage(
                        HttpStatusCode.InternalServerError);
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
                if (RenameAfterMutation)
                {
                    Mappers[index]["name"] = "renamed-after-write";
                }

                if (AcceptMutationThenTimeout)
                {
                    throw new TaskCanceledException(
                        "Provider accepted the mapper before the response was lost.");
                }

                if (AcceptMutationThenServerError)
                {
                    return new HttpResponseMessage(
                        HttpStatusCode.InternalServerError);
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
