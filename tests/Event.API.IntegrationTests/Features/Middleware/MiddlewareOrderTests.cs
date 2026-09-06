// ABOUTME: Integration tests verifying middleware ordering and tenant-exempt path behavior.
// ABOUTME: Ensures ForwardedHeaders is respected, tenant exemptions work, and exception handler catches errors.

using System.Net;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Helpers;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Documents;
using Explore.Domain.Settings.Documents.Payloads;
using Explore.Persistence;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Features.Middleware;

[NotInParallel("ApiTestFixture")]
[ClassDataSource<ApiTestFixture>(Shared = SharedType.PerAssembly)]
public class MiddlewareOrderTests
{
    private readonly ApiTestFixture _fixture;

    public MiddlewareOrderTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Test]
    public async Task TenantResolution_SkipsExemptPath_InstanceOnboarding()
    {
        // /api/InstanceOnboarding is tenant-exempt — should not return 404 for missing tenant
        var response = await _fixture.Client.GetAsync("/api/instanceonboarding/status");

        await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.NotFound);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task PublicExperienceSettings_SingleTenant_UsesDefaultTenantWithout404()
    {
        // Single-tenant mode should resolve the configured default tenant automatically.
        var response = await _fixture.Client.GetAsync("/api/PublicExperience/settings");

        await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task PublicExperienceSettings_MultiTenant_ResolvesTenantFromSlugHeader()
    {
        var tenantId = Guid.NewGuid();

        var app = _fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Deployment:Mode"] = "MultiTenant"
                });
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDeploymentModeProvider>();
                services.AddSingleton<IDeploymentModeProvider>(new TestDeploymentModeProvider(DeploymentMode.MultiTenant));

                services.RemoveAll<ITenantSlugCache>();
                services.AddSingleton<ITenantSlugCache>(new TestTenantSlugCache(tenantId));
            });
        });

        using var client = app.CreateClient();
        using (var scope = app.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            context.Tenants.Add(new Tenant
            {
                Id = tenantId,
                FullName = "Alpha directory",
                Slug = "alpha",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!
            });
            context.Set<TenantSettingsDocument>().Add(
                TenantDirectoryOperatorIdentityDocumentDefaults.Create(tenantId,
                    new TenantDirectoryOperatorIdentitySettings
                    {
                        PublicName = "Alpha Operator",
                        LegalName = "Alpha Operator",
                        OperatorKindCode = "registered_organization",
                        JurisdictionCountryCode = "BE",
                        PublicContactEmail = "operator@alpha.test",
                        LegalNoticeUrl = "https://alpha.test/legal",
                        PrivacyUrl = "https://alpha.test/privacy"
                    }));
            await context.SaveChangesAsync();
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/PublicExperience/settings");
        request.Headers.Add("X-Tenant-Slug", "alpha");

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var responseTenantId = payload.RootElement.GetProperty("tenantId").GetGuid();
        await Assert.That(responseTenantId).IsEqualTo(tenantId);
    }

    [Test]
    public async Task ControlPlane_MultiTenant_SkipsTenantResolutionAndReachesAuthentication()
    {
        var app = _fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDeploymentModeProvider>();
                services.AddSingleton<IDeploymentModeProvider>(new TestDeploymentModeProvider(DeploymentMode.MultiTenant));
            });
        });

        using var client = app.CreateClient();
        var response = await client.GetAsync("/api/admin/control-plane/tenants");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task ManagedProviderProvisioning_MultiTenant_SkipsTenantResolutionAndReachesAuthentication()
    {
        var app = _fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDeploymentModeProvider>();
                services.AddSingleton<IDeploymentModeProvider>(new TestDeploymentModeProvider(DeploymentMode.MultiTenant));
            });
        });

        using var client = app.CreateClient();
        var response = await client.PostAsync(
            "/api/managed-provider-provisioning/clients:ensure",
            content: null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task ExceptionHandler_CatchesControllerExceptions_ReturnsProblemDetails()
    {
        // When a controller action throws (via MediatR), the exception handler should
        // catch it and return ProblemDetails — not a raw 500 or empty body
        using var client = CreateClientThatThrows(new BadRequestException("Test validation error"));

        var response = await client.GetAsync($"/api/actor/{Guid.NewGuid()}");

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(response, HttpStatusCode.BadRequest, "Bad request");
    }

    [Test]
    public async Task ExceptionHandler_CatchesNotFound_ReturnsProblemDetails()
    {
        using var client = CreateClientThatThrows(new NotFoundException("Event", Guid.NewGuid()));

        var response = await client.GetAsync($"/api/actor/{Guid.NewGuid()}");

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(response, HttpStatusCode.NotFound, "Resource not found");
    }

    [Test]
    public async Task ExceptionHandler_CatchesUnexpected_ReturnsSanitized500()
    {
        using var client = CreateClientThatThrows(new InvalidOperationException("Internal details"));

        var response = await client.GetAsync($"/api/actor/{Guid.NewGuid()}");

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(response, HttpStatusCode.InternalServerError, "Internal server error");

        // No sensitive info leaked
        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).DoesNotContain("Internal details");
    }

    private HttpClient CreateClientThatThrows(Exception exception)
    {
        var app = _fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IMediator>();
                services.AddSingleton<IMediator>(new ThrowingMediator(exception));
            });
        });

        return app.CreateClient();
    }

    private sealed class ThrowingMediator(Exception exception) : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
            => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw exception;

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest
            => throw exception;

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw exception;

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw exception;

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw exception;
    }

    private sealed class TestTenantSlugCache(Guid tenantId) : ITenantSlugCache
    {
        public Task WarmAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask<Guid?> GetTenantIdBySlugAsync(string slug, CancellationToken cancellationToken = default)
        {
            Guid? resolved = string.Equals(slug, "alpha", StringComparison.OrdinalIgnoreCase)
                ? tenantId
                : null;
            return ValueTask.FromResult(resolved);
        }

        public ValueTask<Guid?> GetTenantIdByDomainAsync(string domain, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<Guid?>(null);
    }

    private sealed class TestDeploymentModeProvider(DeploymentMode mode) : IDeploymentModeProvider
    {
        public Task<DeploymentMode> GetCurrentModeAsync(CancellationToken ct = default) => Task.FromResult(mode);

        public Task<DeploymentMode> GetConfiguredOnboardingModeAsync(CancellationToken ct = default) => Task.FromResult(mode);

        public Task<bool> IsSingleTenantAsync(CancellationToken ct = default) => Task.FromResult(mode == DeploymentMode.SingleTenant);

        public Task<bool> IsMultiTenantAsync(CancellationToken ct = default) => Task.FromResult(mode == DeploymentMode.MultiTenant);

        public Task InvalidateCacheAsync() => Task.CompletedTask;
    }
}
