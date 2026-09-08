using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Webhooks;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Infrastructure.Configuration;
using Explore.Infrastructure.Webhooks;
using Explore.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace Event.API.IntegrationTests.Features;

public sealed class WebhookPortalHttpContainmentTests
{
    private static readonly Guid TenantId = PlatformDefaults.DefaultTenantId;
    private static readonly Guid ConsumerId = Guid.Parse("0190f8c6-5031-7000-8000-000000000010");
    private const string PortalUrl = "https://svix.example/sensitive-portal";
    private const string PortalToken = "sensitive-portal-token";

    [Test]
    public async Task VerifiedPersistedSuccess_IsAuditedNoStoreAndMatchesHal()
    {
        var svixClient = Substitute.For<ISvixWebhookClient>();
        await using var factory = CreateFactory(svixClient);
        using var client = factory.CreateClient();
        var binding = await SeedConsumerAsync(factory, includeBinding: true);
        ConfigureVerifiedProvider(svixClient, binding!);

        using var response = await SendPortalRequestAsync(client);
        var body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(body).Contains(PortalUrl, StringComparison.Ordinal);
        await Assert.That(body).Contains(PortalToken, StringComparison.Ordinal);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var audit = await dbContext.WebhookAuditEvents
                .IgnoreQueryFilters()
                .SingleAsync(auditEvent =>
                    auditEvent.TargetKindId == (int)WebhookAuditTargetKind.Consumer &&
                    auditEvent.TargetId == ConsumerId &&
                    auditEvent.ActionId == (int)WebhookAuditAction.PortalAccessIssued);
            await Assert.That(audit.Outcome).IsEqualTo(WebhookAuditOutcome.Succeeded);
            await Assert.That(audit.ReasonCode).IsEqualTo("portal_access_requested");
            await Assert.That(audit.CorrelationId).IsNotNull();
            await Assert.That(audit.SafeAfterJson).Contains(binding!.Id.ToString("D"), StringComparison.OrdinalIgnoreCase);
            await Assert.That(audit.SafeAfterJson).DoesNotContain(PortalUrl, StringComparison.Ordinal);
            await Assert.That(audit.SafeAfterJson).DoesNotContain(PortalToken, StringComparison.Ordinal);
        }

        using var halResponse = await SendConsumerRequestAsync(client);
        var halBody = await halResponse.Content.ReadAsStringAsync();
        await Assert.That(halResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(halBody).Contains("open-provider-portal", StringComparison.Ordinal);
    }

    [Test]
    public async Task MissingBinding_ReturnsSafeConflictWithoutPortalDataOrHal()
    {
        var svixClient = Substitute.For<ISvixWebhookClient>();
        await using var factory = CreateFactory(svixClient);
        using var client = factory.CreateClient();
        await SeedConsumerAsync(factory, includeBinding: false);

        using var response = await SendPortalRequestAsync(client);
        var body = await response.Content.ReadAsStringAsync();
        using var halResponse = await SendConsumerRequestAsync(client);
        var halBody = await halResponse.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await AssertSafeProblemAsync(response, body);
        await Assert.That(halBody).DoesNotContain("open-provider-portal", StringComparison.Ordinal);
    }

    [Test]
    public async Task ProviderProfileMismatch_ReturnsSafeConflictWithoutPortalData()
    {
        var svixClient = Substitute.For<ISvixWebhookClient>();
        await using var factory = CreateFactory(svixClient);
        using var client = factory.CreateClient();
        await SeedConsumerAsync(
            factory,
            includeBinding: true,
            providerVersion: "unsupported");

        using var response = await SendPortalRequestAsync(client);
        var body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await AssertSafeProblemAsync(response, body);
        await svixClient.DidNotReceiveWithAnyArgs().CreateAppPortalAccessAsync(default!, default);
    }

    [Test]
    public async Task ProviderFailure_ReturnsAuditedSafeProblemWithoutPortalData()
    {
        var svixClient = Substitute.For<ISvixWebhookClient>();
        await using var factory = CreateFactory(svixClient);
        using var client = factory.CreateClient();
        var binding = await SeedConsumerAsync(factory, includeBinding: true);
        svixClient.GetApplicationAsync(binding!.ExternalApplicationId!, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<SvixApplicationBindingResult>(
                new InvalidOperationException("provider transport failed")));

        using var response = await SendPortalRequestAsync(client);
        var body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await AssertSafeProblemAsync(response, body);
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var audit = await dbContext.WebhookAuditEvents
            .IgnoreQueryFilters()
            .SingleAsync(auditEvent =>
                auditEvent.TargetId == ConsumerId &&
                auditEvent.ActionId == (int)WebhookAuditAction.PortalAccessRejected);
        await Assert.That(audit.Outcome).IsEqualTo(WebhookAuditOutcome.Failed);
        await Assert.That(audit.ReasonCode).IsEqualTo("svix_app_portal_failed");
        await Assert.That(audit.SafeAfterJson).Contains("provider_failure", StringComparison.Ordinal);
        await Assert.That(audit.SafeAfterJson).DoesNotContain("provider transport failed", StringComparison.Ordinal);
    }

    [Test]
    public async Task AuditFailure_ReturnsNoPortalData()
    {
        var svixClient = Substitute.For<ISvixWebhookClient>();
        var auditWriter = Substitute.For<IWebhookAuditEventWriter>();
        auditWriter.AppendAsync(
                Arg.Any<WebhookAuditWriteRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<WebhookAuditEvent>(new InvalidOperationException("audit unavailable")));
        await using var factory = CreateFactory(svixClient, auditWriter);
        using var client = factory.CreateClient();
        var binding = await SeedConsumerAsync(factory, includeBinding: true);
        ConfigureVerifiedProvider(svixClient, binding!);

        using var response = await SendPortalRequestAsync(client);
        var body = await response.Content.ReadAsStringAsync();

        await Assert.That((int)response.StatusCode).IsGreaterThanOrEqualTo(500);
        await AssertSafeProblemAsync(response, body);
    }

    private static PortalFactory CreateFactory(
        ISvixWebhookClient svixClient,
        IWebhookAuditEventWriter? auditWriter = null)
    {
        var authorizationProvider = Substitute.For<IAuthorizationProvider>();
        authorizationProvider.AuthorizeAsync(
                Arg.Any<AuthorizationRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(AuthorizationDecision.Allow(AuthorizationProviderMetadata.Local));
        authorizationProvider.AuthorizeBatchAsync(
                Arg.Any<IReadOnlyList<AuthorizationRequest>>(),
                Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<AuthorizationDecision>>(
                call.ArgAt<IReadOnlyList<AuthorizationRequest>>(0)
                    .Select(_ => AuthorizationDecision.Allow(AuthorizationProviderMetadata.Local))
                    .ToArray()));

        var factory = new PortalFactory(svixClient, auditWriter)
        {
            AuthorizationProviderOverride = authorizationProvider
        };
        factory.AdditionalConfiguration["Webhooks:Provider"] = WebhookOptions.ProviderSvix;
        factory.AdditionalConfiguration["Webhooks:Svix:BaseUrl"] = "http://svix.test";
        factory.AdditionalConfiguration["Webhooks:Svix:Environment"] =
            SvixConformanceProfileRegistry.SelfHostedEnvironment;
        factory.AdditionalConfiguration["Webhooks:Svix:ProviderVersion"] =
            SvixConformanceProfileRegistry.SelfHostedProviderVersion;
        factory.AdditionalConfiguration["Webhooks:Svix:CapabilityPolicyVersion"] =
            SvixConformanceProfileRegistry.SelfHostedCapabilityPolicyVersion;
        factory.AdditionalConfiguration["Webhooks:Svix:AppPortalEnabled"] = "true";
        return factory;
    }

    private static async Task<WebhookConsumerProviderBinding?> SeedConsumerAsync(
        PortalFactory factory,
        bool includeBinding,
        string providerVersion = SvixConformanceProfileRegistry.SelfHostedProviderVersion)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        dbContext.WebhookConsumers.Add(new WebhookConsumer
        {
            Id = ConsumerId,
            TenantId = TenantId,
            ConsumerKind = WebhookConsumerKind.Tenant,
            Name = "HTTP portal containment consumer",
            Status = WebhookConsumerStatus.Active,
            ProviderMode = WebhookProviderMode.Svix,
            CreatedAt = DateTime.UtcNow
        });

        if (!includeBinding)
        {
            await dbContext.SaveChangesAsync();
            return null;
        }

        var profile = WebhookProviderCapabilityProfile.Create(
            WebhookProviderKind.Svix,
            providerVersion,
            WebhookProviderCapability.AppPortal | WebhookProviderCapability.EndpointManagement,
            SvixConformanceProfileRegistry.SelfHostedCapabilityPolicyVersion,
            DateTimeOffset.UtcNow);
        var binding = WebhookConsumerProviderBinding.CreatePending(
            TenantId,
            ConsumerId,
            Guid.CreateVersion7(),
            SvixConformanceProfileRegistry.SelfHostedEnvironment,
            profile,
            WebhookProviderCapability.AppPortal | WebhookProviderCapability.EndpointManagement);
        binding.VerifyOwnership(TenantId, ConsumerId, "app_http_containment", DateTimeOffset.UtcNow);
        dbContext.WebhookConsumerProviderBindings.Add(binding);
        await dbContext.SaveChangesAsync();
        return binding;
    }

    private static void ConfigureVerifiedProvider(
        ISvixWebhookClient svixClient,
        WebhookConsumerProviderBinding binding)
    {
        svixClient.GetApplicationAsync(binding.ExternalApplicationId!, Arg.Any<CancellationToken>())
            .Returns(new SvixApplicationBindingResult(
                binding.ExternalApplicationId!,
                binding.ApplicationUid,
                new Dictionary<string, string>
                {
                    ["islamu.tenant_id"] = TenantId.ToString("D"),
                    ["islamu.consumer_id"] = ConsumerId.ToString("D"),
                    ["islamu.owner_id"] = TenantId.ToString("D"),
                    ["islamu.owner_kind_id"] = ((int)WebhookConsumerKind.Tenant)
                        .ToString(System.Globalization.CultureInfo.InvariantCulture)
                }));
        svixClient.CreateAppPortalAccessAsync(
                Arg.Any<SvixAppPortalAccessRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(new SvixAppPortalAccessResult(PortalUrl, PortalToken));
    }

    private static async Task<HttpResponseMessage> SendPortalRequestAsync(HttpClient client)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/svix/app-portal")
        {
            Content = JsonContent.Create(new
            {
                consumerId = ConsumerId,
                expiresInSeconds = 300
            })
        };
        request.Headers.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(Guid.CreateVersion7()));
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendConsumerRequestAsync(HttpClient client)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/webhooks/consumers/{ConsumerId:D}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/hal+json"));
        request.Headers.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(Guid.CreateVersion7()));
        return await client.SendAsync(request);
    }

    private static async Task AssertSafeProblemAsync(HttpResponseMessage response, string body)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        await Assert.That(mediaType is "application/problem+json" or "application/json").IsTrue();
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        await Assert.That(root.TryGetProperty("status", out var status)).IsTrue();
        await Assert.That(status.GetInt32()).IsEqualTo((int)response.StatusCode);
        await Assert.That(root.TryGetProperty("title", out _)).IsTrue();
        await Assert.That(root.TryGetProperty("type", out _)).IsTrue();
        await Assert.That(root.TryGetProperty("traceId", out _)).IsTrue();
        await Assert.That(root.TryGetProperty("timestamp", out _)).IsTrue();
        await Assert.That(body).DoesNotContain(PortalUrl, StringComparison.Ordinal);
        await Assert.That(body).DoesNotContain(PortalToken, StringComparison.Ordinal);
        await Assert.That(body).DoesNotContain("app_http_containment", StringComparison.Ordinal);
    }

    private sealed class PortalFactory(
        ISvixWebhookClient svixClient,
        IWebhookAuditEventWriter? auditWriter) : AuthenticatedWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISvixWebhookClient>();
                services.AddSingleton(svixClient);

                if (auditWriter is not null)
                {
                    services.RemoveAll<IWebhookAuditEventWriter>();
                    services.AddScoped(_ => auditWriter);
                }
            });
        }
    }
}
