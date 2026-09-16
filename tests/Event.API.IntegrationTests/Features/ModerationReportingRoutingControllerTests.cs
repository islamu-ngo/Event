using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventReporting;
using Explore.Application.Features.EventReporting.Models;
using Explore.Application.Features.EventReporting.Requests.Commands;
using Explore.Application.Features.EventReporting.Requests.Queries;
using Explore.Application.Features.Users.Requests.Queries;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
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
        var handler = new UpdateRoutingStubHandler();
        var controller = CreateController(handler);
        var dto = new UpdateReportingRoutingSettingsDto
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
        };

        var actionResult = await controller.UpdateRoutingSettings(dto, CancellationToken.None);

        var okResult = actionResult.Result as OkObjectResult;
        await Assert.That(okResult).IsNotNull();
        await Assert.That(okResult!.StatusCode).IsEqualTo(StatusCodes.Status200OK);
        await Assert.That(handler.LastCommand).IsNotNull();
        await Assert.That(handler.LastCommand!.Settings.Osprey!.Credentials!.ApiKey).IsEqualTo(SecretApiKey);
        var json = JsonSerializer.Serialize(okResult.Value);
        await Assert.That(json).DoesNotContain(SecretApiKey);
        await Assert.That(json).DoesNotContain(SecretEndpoint);
    }

    [Test]
    public async Task TestProvider_WithAuth_ShouldSendCommandWithoutEchoingSecrets()
    {
        var handler = new UpdateRoutingStubHandler();
        var controller = CreateController(handler);

        var actionResult = await controller.TestProvider(EventReportExternalProvider.Osprey, CancellationToken.None);

        var okResult = actionResult.Result as OkObjectResult;
        await Assert.That(okResult).IsNotNull();
        await Assert.That(okResult!.StatusCode).IsEqualTo(StatusCodes.Status200OK);
        await Assert.That(handler.LastTestCommand).IsNotNull();
        await Assert.That(handler.LastTestCommand!.Provider).IsEqualTo(EventReportExternalProvider.Osprey);
        var json = JsonSerializer.Serialize(okResult.Value);
        await Assert.That(json).DoesNotContain(SecretApiKey);
        await Assert.That(json).DoesNotContain(SecretEndpoint);
    }

    [Test]
    public async Task TestProvider_WhenLocked_ShouldReturnForbidden()
    {
        var handler = new UpdateRoutingStubHandler(providerTestLocked: true);
        var controller = CreateController(handler);

        var actionResult = await controller.TestProvider(EventReportExternalProvider.Osprey, CancellationToken.None);

        var problem = actionResult.Result as ObjectResult;
        await Assert.That(problem).IsNotNull();
        await Assert.That(problem!.StatusCode).IsEqualTo(StatusCodes.Status403Forbidden);
    }

    private static ModerationReportingRoutingController CreateController(UpdateRoutingStubHandler stubHandler)
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(tenantId);
        var identityQuery = Substitute.For<IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>>();
        identityQuery.QueryAsync(Arg.Any<ResolveCurrentUserIdByIdentityRequest>(), Arg.Any<CancellationToken>())
            .Returns(userId);
        var routingStateAssembler = Substitute.For<IResourceAssembler<ReportingRoutingStateDto, ReportingRoutingStateDto>>();
        var getRoutingStateHandler = Substitute.For<IQueryHandler<GetReportingRoutingStateRequest, ReportingRoutingStateDto>>();

        var controller = new ModerationReportingRoutingController(
            getRoutingStateHandler,
            stubHandler,
            stubHandler,
            identityQuery,
            tenantContext,
            routingStateAssembler);

        var httpContext = new DefaultHttpContext();
        var claims = new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, userId.ToString()) };
        httpContext.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(claims, "TestAuth"));
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
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

    private sealed class UpdateRoutingStubHandler(bool providerTestLocked = false)
        : ICommandHandler<UpdateReportingRoutingSettingsCommand, BaseCommandResponse<Guid>>,
          ICommandHandler<TestReportingProviderTargetCommand, BaseCommandResponse<Guid>>
    {
        public UpdateReportingRoutingSettingsCommand? LastCommand { get; private set; }
        public TestReportingProviderTargetCommand? LastTestCommand { get; private set; }

        public Task<BaseCommandResponse<Guid>> ExecuteAsync(
            UpdateReportingRoutingSettingsCommand command,
            CancellationToken cancellationToken = default)
        {
            LastCommand = command;

            return Task.FromResult(BaseCommandResponse.Success(command.TenantId, "Updated"));
        }

        public Task<BaseCommandResponse<Guid>> ExecuteAsync(
            TestReportingProviderTargetCommand command,
            CancellationToken cancellationToken = default)
        {
            LastTestCommand = command;

            var response = providerTestLocked
                ? BaseCommandResponse.Failure<Guid>(
                    "ReportingTenantOverridesLocked",
                    "Tenant Osprey reporting provider tests are locked by instance policy.")
                : BaseCommandResponse.Success(command.TenantId, "Ready");

            return Task.FromResult(response);
        }
    }
}
