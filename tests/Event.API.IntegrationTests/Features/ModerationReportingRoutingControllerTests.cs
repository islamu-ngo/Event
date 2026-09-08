using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.EventReporting;
using Explore.Application.Features.EventReporting.Models;
using Explore.Application.Features.EventReporting.Requests.Commands;
using Explore.Application.Features.EventReporting.Requests.Queries;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
[ClassDataSource<ApiTestFixture>(Shared = SharedType.PerAssembly)]
public sealed class ModerationReportingRoutingControllerAnonymousTests
{
    private const string RoutingStatePath = "/api/tenant/settings/moderation-reporting/routing-state";
    private const string OspreyTestPath = "/api/tenant/settings/moderation-reporting/routing-state/test/Osprey";
    private readonly ApiTestFixture _fixture;

    public ModerationReportingRoutingControllerAnonymousTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Test]
    public async Task GetRoutingState_WithoutAuth_ShouldReturnUnauthorized()
    {
        var response = await _fixture.Client.GetAsync(RoutingStatePath);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task UpdateRoutingState_WithoutAuth_ShouldReturnUnauthorized()
    {
        var response = await _fixture.Client.PatchAsJsonAsync(RoutingStatePath, new UpdateReportingRoutingSettingsDto());

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task TestProvider_WithoutAuth_ShouldReturnUnauthorized()
    {
        var response = await _fixture.Client.PostAsync(OspreyTestPath, null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }
}

public sealed class ModerationReportingRoutingControllerAuthorizedTests
{
    private const string RoutingStatePath = "/api/tenant/settings/moderation-reporting/routing-state";
    private const string OspreyTestPath = "/api/tenant/settings/moderation-reporting/routing-state/test/Osprey";
    private const string SecretApiKey = "super-secret-routing-api-key";
    private const string SecretEndpoint = "https://tenant-secret.example.test/moderation";

    [Test]
    public async Task GetRoutingState_WhenAuthorizationDenied_ShouldReturnForbidden()
    {
        using var factory = CreateFactory(allowAuthorization: false);
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest();

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task GetRoutingState_WithAuth_ShouldReturnRedactedHalDocument()
    {
        using var factory = CreateFactory(allowAuthorization: true);
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest();

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        await Assert.That(json).Contains("apiKeyConfigured");
        await Assert.That(json).DoesNotContain(SecretApiKey);
        await Assert.That(json).DoesNotContain(SecretEndpoint);
        await Assert.That(json).DoesNotContain("endpointUrl");
        await Assert.That(json).DoesNotContain("\"apiKey\"");

        using var body = JsonDocument.Parse(json);
        var root = body.RootElement;
        await Assert.That(root.GetProperty("localCanonicalRequired").GetBoolean()).IsTrue();
        await Assert.That(root.GetProperty("externalSyncEnabled").GetBoolean()).IsTrue();
        await Assert.That(root.GetProperty("osprey").GetProperty("tenantEnabled").GetBoolean()).IsTrue();
        await Assert.That(root.GetProperty("osprey").GetProperty("targets")[0].GetProperty("apiKeyConfigured").GetBoolean()).IsTrue();
        await Assert.That(root.GetProperty("osprey").GetProperty("targets")[0].GetProperty("endpointConfigured").GetBoolean()).IsTrue();
        await Assert.That(root.GetProperty("_links").TryGetProperty("self", out _)).IsTrue();
        await Assert.That(root.GetProperty("_links").TryGetProperty("edit", out _)).IsTrue();
        await Assert.That(root.GetProperty("_links").TryGetProperty("test-osprey-provider", out _)).IsTrue();
        await Assert.That(root.GetProperty("_links").TryGetProperty("test-coop-provider", out _)).IsTrue();
    }

    [Test]
    public async Task UpdateRoutingState_WithAuth_ShouldSendCommandWithoutEchoingSecrets()
    {
        var mediator = new UpdateRoutingMediator();
        using var factory = CreateFactoryWithMediator(mediator);
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(HttpMethod.Patch);
        request.Content = JsonContent.Create(new UpdateReportingRoutingSettingsDto
        {
            Policy = new ReportingRoutingPolicyUpdateDto { ExternalSyncEnabled = true },
            Osprey = new ReportingProviderRoutingUpdateDto
            {
                Enabled = true,
                EndpointUrl = SecretEndpoint,
                Credentials = new ReportingProviderCredentialsUpdateDto { ApiKey = SecretApiKey }
            },
            Coop = new ReportingProviderRoutingUpdateDto
            {
                Enabled = true,
                RoutingMode = "tenant"
            }
        });

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(mediator.LastCommand).IsNotNull();
        await Assert.That(mediator.LastCommand!.Settings.Osprey!.Credentials!.ApiKey).IsEqualTo(SecretApiKey);
        var json = await response.Content.ReadAsStringAsync();
        await Assert.That(json).DoesNotContain(SecretApiKey);
        await Assert.That(json).DoesNotContain(SecretEndpoint);
    }

    [Test]
    public async Task TestProvider_WithAuth_ShouldSendCommandWithoutEchoingSecrets()
    {
        var mediator = new UpdateRoutingMediator();
        using var factory = CreateFactoryWithMediator(mediator);
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(HttpMethod.Post, OspreyTestPath);

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(mediator.LastTestCommand).IsNotNull();
        await Assert.That(mediator.LastTestCommand!.Provider).IsEqualTo(EventReportExternalProvider.Osprey);
        var json = await response.Content.ReadAsStringAsync();
        await Assert.That(json).DoesNotContain(SecretApiKey);
        await Assert.That(json).DoesNotContain(SecretEndpoint);
    }

    [Test]
    public async Task TestProvider_WhenLocked_ShouldReturnForbidden()
    {
        var mediator = new UpdateRoutingMediator(providerTestLocked: true);
        using var factory = CreateFactoryWithMediator(mediator);
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(HttpMethod.Post, OspreyTestPath);

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    private static WebApplicationFactory<Program> CreateFactory(bool allowAuthorization)
    {
        var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider { AllowAll = allowAuthorization }
        };

        return factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IReportingRoutingPolicyResolver>();
                services.AddSingleton<IReportingRoutingPolicyResolver>(new StubRoutingPolicyResolver());
            });
        });
    }

    private static WebApplicationFactory<Program> CreateFactoryWithMediator(IMediator mediator)
    {
        var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider { AllowAll = true }
        };

        return factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IMediator>();
                services.AddSingleton(mediator);
            });
        });
    }

    private static HttpRequestMessage CreateAuthenticatedRequest(HttpMethod? method = null, string? path = null)
    {
        var request = new HttpRequestMessage(method ?? HttpMethod.Get, path ?? RoutingStatePath);
        request.Headers.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(Guid.NewGuid()));
        return request;
    }

    private sealed class StubRoutingPolicyResolver : IReportingRoutingPolicyResolver
    {
        public Task<ReportingRoutingPolicy> ResolveAsync(CancellationToken cancellationToken = default)
        {
            var policy = new ReportingRoutingPolicy(
                LocalCanonicalRequired: true,
                ExternalSyncEnabled: true,
                InstanceOspreyEnabled: true,
                TenantOspreyEnabled: true,
                InstanceCoopEnabled: false,
                TenantCoopEnabled: true,
                TenantProviderConfigurationLocked: false,
                TenantOspreyProviderLocked: false,
                TenantCoopProviderLocked: false,
                OspreyRoutingMode: "both",
                CoopRoutingMode: "tenant",
                EvidenceMode: EventReportProviderEvidenceMode.MetadataOnly,
                OspreyTargets:
                [
                    new ReportingProviderTarget(
                        EventReportExternalProvider.Osprey,
                        EventReportProviderTargetScope.Tenant,
                        "tenant-target",
                        SecretEndpoint,
                        SecretApiKey)
                ],
                CoopTargets:
                [
                    new ReportingProviderTarget(
                        EventReportExternalProvider.Coop,
                        EventReportProviderTargetScope.Instance,
                        "instance")
                ]);

            return Task.FromResult(policy);
        }
    }

    private sealed class UpdateRoutingMediator(bool providerTestLocked = false) : IMediator
    {
        public UpdateReportingRoutingSettingsCommand? LastCommand { get; private set; }
        public TestReportingProviderTargetCommand? LastTestCommand { get; private set; }

        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
            => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            object response = request switch
            {
                UpdateReportingRoutingSettingsCommand command => Update(command),
                TestReportingProviderTargetCommand command => Test(command),
                GetReportingRoutingStateRequest => throw new InvalidOperationException("Routing-state query is not expected in the update test."),
                _ => throw new InvalidOperationException($"Unexpected request type {request.GetType().Name}.")
            };

            return Task.FromResult((TResponse)response);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
            => Task.CompletedTask;

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => request switch
            {
                UpdateReportingRoutingSettingsCommand command => Task.FromResult<object?>(Update(command)),
                TestReportingProviderTargetCommand command => Task.FromResult<object?>(Test(command)),
                _ => throw new InvalidOperationException($"Unexpected request type {request.GetType().Name}.")
            };

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        private BaseCommandResponse<Guid> Update(UpdateReportingRoutingSettingsCommand command)
        {
            LastCommand = command;

            return BaseCommandResponse.Success(command.TenantId, "Updated");
        }

        private BaseCommandResponse<Guid> Test(TestReportingProviderTargetCommand command)
        {
            LastTestCommand = command;

            return providerTestLocked
                ? BaseCommandResponse.Failure<Guid>(
                    "ReportingTenantOverridesLocked",
                    "Tenant Osprey reporting provider tests are locked by instance policy.")
                : BaseCommandResponse.Success(command.TenantId, "Ready");
        }
    }
}
