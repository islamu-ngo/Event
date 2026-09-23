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

public sealed class KeycloakProvisioningOperationTests
{
    [Test]
    public async Task CreateRealm_WhenRealmExists_ConflictsWithoutWrite()
    {
        var handler = new ProvisioningHandler
        {
            RealmExists = true
        };

        KeycloakProvisioningOperationResult result =
            await CreateClient(handler).ApplyApprovedProvisioningAsync(
                Request(RealmStep()),
                CancellationToken.None);

        await Assert.That(result.Outcome)
            .IsEqualTo(KeycloakStepOutcomeKind.Conflict);
        await Assert.That(handler.MutationPaths).IsEmpty();
    }

    [Test]
    public async Task CreateRealm_CapturesImmutableProviderId()
    {
        var handler = new ProvisioningHandler
        {
            RealmExists = false
        };

        KeycloakProvisioningOperationResult result =
            await CreateClient(handler).ApplyApprovedProvisioningAsync(
                Request(RealmStep()),
                CancellationToken.None);

        await Assert.That(result.Outcome)
            .IsEqualTo(KeycloakStepOutcomeKind.Applied);
        await Assert.That(result.ProviderResourceId)
            .IsEqualTo(
                "11111111-1111-7111-8111-111111111111");
        await Assert.That(
                handler.LastMutation!["id"]!.GetValue<string>())
            .IsEqualTo(
                "11111111-1111-7111-8111-111111111111");
    }

    [Test]
    public async Task ReconcileRealm_WhenCapturedIdDiffers_Conflicts()
    {
        var handler = new ProvisioningHandler
        {
            RealmExists = true,
            RealmId =
                "33333333-3333-7333-8333-333333333333"
        };

        KeycloakProvisioningOperationResult result =
            await CreateClient(handler).InspectApprovedProvisioningAsync(
                Request(
                    RealmStep(),
                    providerResourceId:
                        "22222222-2222-7222-8222-222222222222"),
                CancellationToken.None);

        await Assert.That(result.Outcome)
            .IsEqualTo(KeycloakStepOutcomeKind.Conflict);
        await Assert.That(handler.MutationPaths).IsEmpty();
    }

    [Test]
    public async Task CreateClient_WhenNameAppearsAfterPreview_DoesNotAdopt()
    {
        var handler = new ProvisioningHandler
        {
            ExactClient = ClientRepresentation(
                "external-uuid",
                "event-bff",
                bearerOnly: false)
        };

        KeycloakProvisioningOperationResult result =
            await CreateClient(handler).ApplyApprovedProvisioningAsync(
                Request(BffStep(), "runtime-secret-canary"),
                CancellationToken.None);

        await Assert.That(result.Outcome)
            .IsEqualTo(KeycloakStepOutcomeKind.Conflict);
        await Assert.That(handler.MutationPaths).IsEmpty();
    }

    [Test]
    public async Task CreateBffClient_UsesRuntimeSecretWithoutReturningIt()
    {
        const string canary = "runtime-secret-canary";
        var handler = new ProvisioningHandler();
        KeycloakProvisioningOperationRequest request =
            Request(BffStep(), canary);

        KeycloakProvisioningOperationResult result =
            await CreateClient(handler).ApplyApprovedProvisioningAsync(
                request,
                CancellationToken.None);

        await Assert.That(result.Outcome)
            .IsEqualTo(KeycloakStepOutcomeKind.Applied);
        await Assert.That(handler.LastMutation!["secret"]!
                .GetValue<string>())
            .IsEqualTo(canary);
        string providerId =
            handler.LastMutation["id"]!.GetValue<string>();
        await Assert.That(Guid.TryParse(
                providerId,
                out _))
            .IsTrue();
        await Assert.That(result.ProviderResourceId)
            .IsEqualTo(providerId);
        await Assert.That(
                request.Step.Desired.ProviderResourceId)
            .IsEqualTo(providerId);
        await Assert.That(result.ToString()).DoesNotContain(canary);
        await Assert.That(request.ToString()).DoesNotContain(canary);
        await AssertForbiddenEndpointsAbsent(handler);
    }

    [Test]
    public async Task CreateBearerOnlyApiClient_OmitsSecretProperty()
    {
        var handler = new ProvisioningHandler();

        KeycloakProvisioningOperationResult result =
            await CreateClient(handler).ApplyApprovedProvisioningAsync(
                Request(ApiStep(), "must-not-be-used"),
                CancellationToken.None);

        await Assert.That(result.Outcome)
            .IsEqualTo(KeycloakStepOutcomeKind.Applied);
        await Assert.That(handler.LastMutation!.ContainsKey("secret"))
            .IsFalse();
        await Assert.That(handler.LastMutation["bearerOnly"]!
                .GetValue<bool>())
            .IsTrue();
        await AssertForbiddenEndpointsAbsent(handler);
    }

    [Test]
    public async Task CreateAcceptedThenTimeout_RemainsOutcomeUnknown()
    {
        var handler = new ProvisioningHandler
        {
            TimeoutAfterAccept = true
        };

        KeycloakProvisioningOperationResult result =
            await CreateClient(handler).ApplyApprovedProvisioningAsync(
                Request(BffStep(), "runtime-secret-canary"),
                CancellationToken.None);

        await Assert.That(result.Outcome)
            .IsEqualTo(KeycloakStepOutcomeKind.OutcomeUnknown);
        await Assert.That(handler.MutationPaths).HasCount().EqualTo(1);
    }

    [Test]
    public async Task CreateAcceptedThenCallerCancels_ReturnsPlannedUnknown()
    {
        using var callerCancellation =
            new CancellationTokenSource();
        var handler = new ProvisioningHandler
        {
            CancelCallerAfterAccept =
                callerCancellation
        };
        KeycloakProvisioningOperationRequest request =
            Request(
                BffStep(),
                "runtime-secret-canary");

        KeycloakProvisioningOperationResult result =
            await CreateClient(handler)
                .ApplyApprovedProvisioningAsync(
                    request,
                    callerCancellation.Token);

        await Assert.That(result.Outcome)
            .IsEqualTo(
                KeycloakStepOutcomeKind.OutcomeUnknown);
        await Assert.That(result.ProviderResourceId)
            .IsEqualTo(
                request.Step.Desired.ProviderResourceId);
        await Assert.That(handler.MutationPaths)
            .HasCount().EqualTo(1);
    }

    [Test]
    public async Task ReconcileCreateWithoutCapturedUuid_DoesNotAdoptByName()
    {
        var handler = new ProvisioningHandler
        {
            ExactClient = ClientRepresentation(
                "external-uuid",
                "event-bff",
                bearerOnly: false)
        };

        KeycloakProvisioningOperationResult result =
            await CreateClient(handler).InspectApprovedProvisioningAsync(
                Request(BffStep(), "runtime-secret-canary"),
                CancellationToken.None);

        await Assert.That(result.Outcome)
            .IsEqualTo(KeycloakStepOutcomeKind.OutcomeUnknown);
        await Assert.That(handler.MutationPaths).IsEmpty();
    }

    private static async Task AssertForbiddenEndpointsAbsent(
        ProvisioningHandler handler)
    {
        await Assert.That(handler.MutationPaths.Any(path =>
                path.Contains("/users", StringComparison.Ordinal)
                || path.Contains("/roles", StringComparison.Ordinal)
                || path.Contains("/client-scopes", StringComparison.Ordinal)
                || path.Contains("/client-secret", StringComparison.Ordinal)
                || path.Contains("/sessions", StringComparison.Ordinal)))
            .IsFalse();
    }

    private static KeycloakAdminClient CreateClient(
        HttpMessageHandler handler)
    {
        IHostEnvironment environment =
            Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Production");
        return new KeycloakAdminClient(
            new HttpClient(handler),
            environment,
            new ConfigurationBuilder().Build());
    }

    private static KeycloakProvisioningOperationRequest Request(
        KeycloakChangeStep step,
        string? runtimeSecret = null,
        string? providerResourceId = null) =>
        new(
            new Uri(
                "https://identity.example.test/auth/realms/operators"),
            "operators",
            "event-bff",
            "event-api",
            step,
            runtimeSecret,
            $"admin-{Guid.CreateVersion7():N}",
            $"password-{Guid.CreateVersion7():N}",
            providerResourceId);

    private static KeycloakChangeStep RealmStep()
    {
        KeycloakDesiredProjection desired =
            KeycloakDesiredProjection.Realm(
                "operators",
                "11111111-1111-7111-8111-111111111111");
        return Step(
            "realm:create",
            KeycloakStep.CreateRealm,
            KeycloakResourceKind.Realm,
            "operators",
            desired);
    }

    private static KeycloakChangeStep BffStep()
    {
        KeycloakDesiredProjection desired =
            KeycloakDesiredProjection.ConfidentialClient(
                "event-bff",
                ["https://event.example.test/signin-oidc"],
                ["https://event.example.test"]);
        return Step(
            "client:bff",
            KeycloakStep.CreateClient,
            KeycloakResourceKind.Client,
            "event-bff",
            desired);
    }

    private static KeycloakChangeStep ApiStep()
    {
        KeycloakDesiredProjection desired =
            KeycloakDesiredProjection.BearerOnlyClient("event-api");
        return Step(
            "client:api",
            KeycloakStep.CreateClient,
            KeycloakResourceKind.Client,
            "event-api",
            desired);
    }

    private static KeycloakChangeStep Step(
        string stepId,
        KeycloakStep kind,
        KeycloakResourceKind resourceKind,
        string targetId,
        KeycloakDesiredProjection desired) =>
        new(
            stepId,
            kind,
            resourceKind,
            targetId,
            KeycloakStepPrecondition.MustBeAbsent,
            expectedFingerprint: null,
            expectedIdentityFingerprint: null,
            KeycloakOperationService.DesiredProvisioningFingerprint(
                desired),
            bindingFingerprint: "binding-fingerprint",
            desired);

    private static JsonObject ClientRepresentation(
        string id,
        string clientId,
        bool bearerOnly) =>
        new()
        {
            ["id"] = id,
            ["clientId"] = clientId,
            ["name"] = clientId,
            ["enabled"] = true,
            ["publicClient"] = false,
            ["bearerOnly"] = bearerOnly,
            ["standardFlowEnabled"] = !bearerOnly,
            ["directAccessGrantsEnabled"] = false,
            ["serviceAccountsEnabled"] = false,
            ["redirectUris"] = bearerOnly
                ? new JsonArray()
                : new JsonArray(
                    "https://event.example.test/signin-oidc"),
            ["webOrigins"] = bearerOnly
                ? new JsonArray()
                : new JsonArray("https://event.example.test")
        };

    private sealed class ProvisioningHandler : HttpMessageHandler
    {
        public bool RealmExists { get; set; } = true;
        public string RealmId { get; init; } =
            "11111111-1111-7111-8111-111111111111";
        public bool TimeoutAfterAccept { get; init; }
        public CancellationTokenSource?
            CancelCallerAfterAccept
        {
            get;
            init;
        }
        public JsonObject? ExactClient { get; init; }
        public JsonObject? LastMutation { get; private set; }
        public List<string> MutationPaths { get; } = [];

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
                return Json(
                    HttpStatusCode.OK,
                    """{"access_token":"admin-token"}""");
            }

            if (request.Method == HttpMethod.Get
                && path.EndsWith(
                    "/admin/realms/operators",
                    StringComparison.Ordinal))
            {
                return RealmExists
                    ? Json(
                        HttpStatusCode.OK,
                        $$"""{"id":"{{RealmId}}","realm":"operators","enabled":true}""")
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (request.Method == HttpMethod.Get
                && path.EndsWith(
                    "/admin/realms/operators/clients",
                    StringComparison.Ordinal))
            {
                var clients = new JsonArray();
                if (ExactClient is not null)
                {
                    clients.Add(ExactClient.DeepClone());
                }

                return Json(HttpStatusCode.OK, clients.ToJsonString());
            }

            if (request.Method == HttpMethod.Get
                && path.Contains(
                    "/admin/realms/operators/clients/",
                    StringComparison.Ordinal))
            {
                return LastMutation is null
                    ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : Json(
                        HttpStatusCode.OK,
                        LastMutation.ToJsonString());
            }

            if (request.Method == HttpMethod.Post
                && (path.EndsWith(
                        "/admin/realms",
                        StringComparison.Ordinal)
                    || path.EndsWith(
                        "/admin/realms/operators/clients",
                        StringComparison.Ordinal)))
            {
                MutationPaths.Add(path);
                LastMutation = JsonNode.Parse(
                    await request.Content!.ReadAsStringAsync(
                        cancellationToken))!.AsObject();
                if (CancelCallerAfterAccept is not null)
                {
                    CancelCallerAfterAccept.Cancel();
                    throw new TaskCanceledException(
                        "Provider accepted before caller cancellation.");
                }

                if (TimeoutAfterAccept)
                {
                    throw new TaskCanceledException(
                        "Provider accepted before response loss.");
                }

                var response =
                    new HttpResponseMessage(HttpStatusCode.Created);
                if (path.EndsWith(
                        "/clients",
                        StringComparison.Ordinal))
                {
                    response.Headers.Location = new Uri(
                        "/auth/admin/realms/operators/clients/created-uuid",
                        UriKind.Relative);
                }
                else
                {
                    RealmExists = true;
                }

                return response;
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
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
