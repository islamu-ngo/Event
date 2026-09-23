using System.Net;
using System.Text;
using Explore.Application.Contracts.Services;
using Explore.Domain.Keycloak;
using Explore.Infrastructure.Services.Keycloak;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class KeycloakAdminClientTests
{
    [Test]
    public async Task InspectAsync_UsesQueryStringAndTreatsOptionalScopeMapperAsNotEffective()
    {
        string passwordCanary = $"password-{Guid.CreateVersion7():N}";
        var handler = new OrderedHandler(
            Expect(HttpMethod.Get, "/base/realms/operators/.well-known/openid-configuration", """
                { "issuer": "https://identity.example.test/base/realms/operators" }
                """),
            Expect(HttpMethod.Post, "/base/realms/master/protocol/openid-connect/token", """
                { "access_token": "admin-token" }
                """),
            Expect(
                HttpMethod.Get,
                "/base/admin/realms/operators",
                """{"realm":"operators","enabled":true}"""),
            Expect(
                HttpMethod.Get,
                "/base/admin/realms/operators/clients?clientId=event-bff",
                """[{ "id": "client-uuid", "clientId": "event-bff" }]"""),
            Expect(
                HttpMethod.Get,
                "/base/admin/realms/operators/clients?clientId=event-api",
                "[]"),
            Expect(
                HttpMethod.Get,
                "/base/admin/realms/operators/clients/client-uuid/protocol-mappers/models",
                """
                [{
                  "id": "subject-id",
                  "name": "subject",
                  "protocolMapper": "oidc-sub-mapper",
                  "config": {
                    "access.token.claim": "true",
                    "id.token.claim": "true"
                  }
                }]
                """),
            Expect(
                HttpMethod.Get,
                "/base/admin/realms/operators/clients/client-uuid/default-client-scopes",
                "[]"),
            Expect(
                HttpMethod.Get,
                "/base/admin/realms/operators/clients/client-uuid/optional-client-scopes",
                """[{ "id": "optional-audience", "name": "optional-audience" }]"""),
            Expect(
                HttpMethod.Get,
                "/base/admin/realms/operators/client-scopes/optional-audience/protocol-mappers/models",
                """
                [{
                  "id": "audience-id",
                  "name": "audience",
                  "protocolMapper": "oidc-audience-mapper",
                  "config": {
                    "included.client.audience": "event-api",
                    "access.token.claim": "true",
                    "id.token.claim": "false"
                  }
                }, {
                  "id": "optional-conflicting-subject",
                  "name": "subject",
                  "protocolMapper": "oidc-usermodel-attribute-mapper",
                  "config": {
                    "claim.name": "sub",
                    "user.attribute": "department",
                    "access.token.claim": "true"
                  }
                }]
                """));
        var client = CreateClient(handler);

        KeycloakAdminInspectionResult result = await client.InspectAsync(
            new KeycloakAdminInspectionRequest(
                new Uri("https://identity.example.test/base/realms/operators"),
                "operators",
                "event-bff",
                "event-api",
                $"admin-{Guid.CreateVersion7():N}",
                passwordCanary),
            CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(KeycloakInspectionStatus.Inspected);
        await Assert.That(result.Snapshot).IsNotNull();
        IReadOnlyList<KeycloakMapperSemantic> repairs =
            new KeycloakOperationPolicy().GetRequiredMapperRepairs(result.Snapshot!);
        await Assert.That(repairs).DoesNotContain(KeycloakMapperSemantic.Subject);
        await Assert.That(repairs).Contains(KeycloakMapperSemantic.Audience);
        await Assert.That(new KeycloakOperationPolicy().HasConflictingMapper(
            result.Snapshot!,
            KeycloakMapperSemantic.Subject)).IsFalse();
        await Assert.That(result.ToString()).DoesNotContain(passwordCanary);
        await Assert.That(handler.AllRequestsConsumed).IsTrue();
    }

    [Test]
    public async Task InspectAsync_IdTokenOnlySubjectProducerIsConflicting()
    {
        var handler = new OrderedHandler(
            Expect(HttpMethod.Get, "/realms/operators/.well-known/openid-configuration", """
                { "issuer": "https://identity.example.test/realms/operators" }
                """),
            Expect(HttpMethod.Post, "/realms/master/protocol/openid-connect/token", """
                { "access_token": "admin-token" }
                """),
            Expect(
                HttpMethod.Get,
                "/admin/realms/operators",
                """{"realm":"operators","enabled":true}"""),
            Expect(
                HttpMethod.Get,
                "/admin/realms/operators/clients?clientId=event-bff",
                """[{ "id": "client-uuid", "clientId": "event-bff" }]"""),
            Expect(
                HttpMethod.Get,
                "/admin/realms/operators/clients/client-uuid/protocol-mappers/models",
                """
                [{
                  "id": "conflicting-subject",
                  "name": "subject",
                  "protocolMapper": "oidc-usermodel-attribute-mapper",
                  "config": {
                    "claim.name": "sub",
                    "user.attribute": "department",
                    "access.token.claim": "false",
                    "id.token.claim": "true"
                  }
                }]
                """),
            Expect(
                HttpMethod.Get,
                "/admin/realms/operators/clients/client-uuid/default-client-scopes",
                "[]"),
            Expect(
                HttpMethod.Get,
                "/admin/realms/operators/clients/client-uuid/optional-client-scopes",
                "[]"));
        var client = CreateClient(handler);

        KeycloakAdminInspectionResult result = await client.InspectAsync(
            new KeycloakAdminInspectionRequest(
                new Uri("https://identity.example.test/realms/operators"),
                "operators",
                "event-bff",
                apiClientId: null,
                $"admin-{Guid.CreateVersion7():N}",
                $"password-{Guid.CreateVersion7():N}"),
            CancellationToken.None);

        KeycloakEffectiveMapperSnapshot mapper = result.Snapshot!.EffectiveMappers.Single();
        await Assert.That(mapper.IsConflicting).IsTrue();
        await Assert.That(mapper.IsEffective).IsFalse();
        await Assert.That(new KeycloakOperationPolicy()
            .GetRequiredMapperRepairs(result.Snapshot))
            .Contains(KeycloakMapperSemantic.Subject);
    }

    [Test]
    public async Task InspectAsync_RejectsSuccessfulNonOidcDocument()
    {
        var handler = new OrderedHandler(
            new ExpectedRequest(
                HttpMethod.Get,
                "/realms/operators/.well-known/openid-configuration",
                () => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "<html>not oidc</html>",
                        Encoding.UTF8,
                        "text/html")
                }));
        var client = CreateClient(handler);

        KeycloakAdminInspectionResult result = await client.InspectAsync(
            new KeycloakAdminInspectionRequest(
                new Uri("https://identity.example.test/realms/operators"),
                "operators",
                "event-bff",
                "event-api"),
            CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(KeycloakInspectionStatus.InvalidResponse);
    }

    [Test]
    public async Task InspectAsync_BoundsStalledDiscoveryBody()
    {
        var handler = new OrderedHandler(
            new ExpectedRequest(
                HttpMethod.Get,
                "/realms/operators/.well-known/openid-configuration",
                () => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StallingContent()
                }));
        var client = CreateClient(handler, requestTimeoutMilliseconds: 50);

        KeycloakAdminInspectionResult result = await client.InspectAsync(
            new KeycloakAdminInspectionRequest(
                new Uri("https://identity.example.test/realms/operators"),
                "operators",
                "event-bff",
                "event-api"),
            CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(KeycloakInspectionStatus.Unavailable);
        await Assert.That(result.ReasonCode).IsEqualTo("keycloak_request_timeout");
    }

    [Test]
    public async Task InspectAsync_RejectsOversizedDiscoveryBody()
    {
        string oversized = $$"""
            {
              "issuer": "https://identity.example.test/realms/operators",
              "padding": "{{new string('x', (1024 * 1024) + 1)}}"
            }
            """;
        var handler = new OrderedHandler(
            Expect(
                HttpMethod.Get,
                "/realms/operators/.well-known/openid-configuration",
                oversized));
        var client = CreateClient(handler);

        KeycloakAdminInspectionResult result = await client.InspectAsync(
            new KeycloakAdminInspectionRequest(
                new Uri("https://identity.example.test/realms/operators"),
                "operators",
                "event-bff",
                "event-api"),
            CancellationToken.None);

        await Assert.That(result.Status).IsNotEqualTo(KeycloakInspectionStatus.PublicOnly);
        await Assert.That(result.Snapshot).IsNull();
    }

    [Test]
    public async Task InspectAsync_RejectsExternalPlainHttpBeforeSendingCredentials()
    {
        var handler = new RejectingHandler();
        var client = CreateClient(handler, allowLoopbackHttp: true);

        KeycloakAdminInspectionResult result = await client.InspectAsync(
            new KeycloakAdminInspectionRequest(
                new Uri("http://identity.example.test/realms/operators"),
                "operators",
                "event-bff",
                "event-api",
                $"admin-{Guid.CreateVersion7():N}",
                $"password-{Guid.CreateVersion7():N}"),
            CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(KeycloakInspectionStatus.InvalidTarget);
        await Assert.That(handler.WasCalled).IsFalse();
    }

    [Test]
    public async Task InspectAsync_AllowsExactManagedLocalHttpOrigin()
    {
        var handler = new OrderedHandler(
            Expect(
                HttpMethod.Get,
                "/realms/operators/.well-known/openid-configuration",
                """
                {
                  "issuer": "http://keycloak:8080/realms/operators"
                }
                """));
        var client = CreateClient(
            handler,
            managedLocalOrigin: "http://keycloak:8080");

        KeycloakAdminInspectionResult result =
            await client.InspectAsync(
                new KeycloakAdminInspectionRequest(
                    new Uri(
                        "http://keycloak:8080/realms/operators"),
                    "operators",
                    "event-bff",
                    "event-api"),
                CancellationToken.None);

        await Assert.That(result.Status)
            .IsEqualTo(
                KeycloakInspectionStatus.PublicOnly);
        await Assert.That(handler.AllRequestsConsumed).IsTrue();
    }

    [Test]
    public async Task InspectAsync_RejectsDifferentManagedLocalHttpOrigin()
    {
        var handler = new RejectingHandler();
        var client = CreateClient(
            handler,
            managedLocalOrigin: "http://keycloak:8080");

        KeycloakAdminInspectionResult result =
            await client.InspectAsync(
                new KeycloakAdminInspectionRequest(
                    new Uri(
                        "http://identity:8080/realms/operators"),
                    "operators",
                    "event-bff",
                    "event-api",
                    $"admin-{Guid.CreateVersion7():N}",
                    $"password-{Guid.CreateVersion7():N}"),
                CancellationToken.None);

        await Assert.That(result.Status)
            .IsEqualTo(
                KeycloakInspectionStatus.InvalidTarget);
        await Assert.That(handler.WasCalled).IsFalse();
    }

    [Test]
    public async Task InspectAsync_WithAdminCredentials_ProvesRealmAbsence()
    {
        var handler = new OrderedHandler(
            new ExpectedRequest(
                HttpMethod.Get,
                "/auth/realms/operators/.well-known/openid-configuration",
                () => new HttpResponseMessage(HttpStatusCode.NotFound)),
            Expect(
                HttpMethod.Post,
                "/auth/realms/master/protocol/openid-connect/token",
                """{"access_token":"admin-token"}"""),
            new ExpectedRequest(
                HttpMethod.Get,
                "/auth/admin/realms/operators",
                () => new HttpResponseMessage(HttpStatusCode.NotFound)));

        KeycloakAdminInspectionResult result =
            await CreateClient(handler).InspectAsync(
                new KeycloakAdminInspectionRequest(
                    new Uri(
                        "https://identity.example.test/auth/realms/operators"),
                    "operators",
                    "event-bff",
                    "event-api",
                    "admin",
                    "password"),
                CancellationToken.None);

        await Assert.That(result.Status)
            .IsEqualTo(KeycloakInspectionStatus.Inspected);
        await Assert.That(result.Snapshot!.RealmExists).IsFalse();
        await Assert.That(result.Snapshot.BlazorClient.IsProvenAbsent)
            .IsTrue();
        await Assert.That(result.Snapshot.ApiClient!.IsProvenAbsent)
            .IsTrue();
        await Assert.That(handler.AllRequestsConsumed).IsTrue();
    }

    private static KeycloakAdminClient CreateClient(
        HttpMessageHandler handler,
        bool allowLoopbackHttp = false,
        string? managedLocalOrigin = null,
        int? requestTimeoutMilliseconds = null)
    {
        IHostEnvironment environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Testing");
        var values = new Dictionary<string, string?>
        {
            ["Keycloak:AllowDevelopmentLoopbackHttp"] =
                allowLoopbackHttp.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        if (managedLocalOrigin is not null)
        {
            values["Keycloak:AllowManagedLocalHttp"] = "true";
            values["Keycloak:ManagedLocalHttpOrigin"] =
                managedLocalOrigin;
        }

        if (requestTimeoutMilliseconds.HasValue)
        {
            values["Keycloak:AdminRequestTimeoutMilliseconds"] =
                requestTimeoutMilliseconds.Value.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
        }

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
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

    private static ExpectedRequest Expect(
        HttpMethod method,
        string pathAndQuery,
        string json) =>
        new(
            method,
            pathAndQuery,
            () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });

    private sealed record ExpectedRequest(
        HttpMethod Method,
        string PathAndQuery,
        Func<HttpResponseMessage> Response);

    private sealed class OrderedHandler(params ExpectedRequest[] expected) : HttpMessageHandler
    {
        private readonly Queue<ExpectedRequest> _expected = new(expected);

        public bool AllRequestsConsumed => _expected.Count == 0;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            ExpectedRequest next = _expected.Dequeue();
            if (request.Method != next.Method
                || request.RequestUri?.PathAndQuery != next.PathAndQuery)
            {
                throw new InvalidOperationException(
                    $"Expected {next.Method} {next.PathAndQuery}, received {request.Method} {request.RequestUri?.PathAndQuery}.");
            }

            return Task.FromResult(next.Response());
        }
    }

    private sealed class RejectingHandler : HttpMessageHandler
    {
        public bool WasCalled { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            throw new InvalidOperationException("Unsafe target reached the transport.");
        }
    }

    private sealed class StallingContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context) =>
            Task.Delay(Timeout.InfiniteTimeSpan);

        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context,
            CancellationToken cancellationToken) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
