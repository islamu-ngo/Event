using System.Net;
using System.Text;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Helpers;
using Explore.Application;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventTicketing;
using Explore.Application.Features.EventTicketing.Handlers.Commands;
using Explore.Application.Features.EventTicketing.Handlers.Queries;
using Explore.Application.Features.EventTicketing.Requests.Commands;
using Explore.Application.Features.EventTicketing.Requests.Queries;
using Explore.Application.Operations;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Features;

[Category(TestCategories.Phase43Ticketing)]
public sealed class EventTicketingHalRuntimeTests
{
    [Test]
    public async Task EmptyCatalog_ReturnsHalCreateDraftAndPrivateNoStore()
    {
        var dto = CreateCatalog(status: null, includeItems: false);
        var provider = new TicketingAuthorizationProvider();
        await using var factory = new TicketingFactory(dto, provider);
        using var client = factory.CreateClient();
        using var request = Authenticated(HttpMethod.Get, dto.EventId);

        using HttpResponseMessage response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/hal+json");
        await Assert.That(response.Headers.CacheControl?.Private).IsTrue();
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement links = document.RootElement.GetProperty("_links");
        await Assert.That(links.TryGetProperty("self", out _)).IsTrue();
        await Assert.That(links.TryGetProperty("event", out _)).IsTrue();
        await Assert.That(links.TryGetProperty("create-draft", out _)).IsTrue();
        await Assert.That(document.RootElement.GetProperty("_embedded").GetProperty("ticket-types").GetArrayLength()).IsEqualTo(0);
        await Assert.That(provider.BatchCalls).IsEqualTo(1);
    }

    [Test]
    public async Task DraftCatalog_EmbedsWritableItemsWithoutDuplicateRootArrays()
    {
        var dto = CreateCatalog(TicketCatalogStatusEnum.Draft, includeItems: true);
        var provider = new TicketingAuthorizationProvider();
        await using var factory = new TicketingFactory(dto, provider);
        using var client = factory.CreateClient();
        using var request = Authenticated(HttpMethod.Get, dto.EventId);

        using HttpResponseMessage response = await client.SendAsync(request);
        using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement root = document.RootElement;
        JsonElement links = root.GetProperty("_links");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(links.TryGetProperty("create-type", out _)).IsTrue();
        await Assert.That(links.TryGetProperty("create-pool", out _)).IsTrue();
        await Assert.That(links.TryGetProperty("publish", out _)).IsTrue();
        await Assert.That(root.TryGetProperty("ticketTypes", out _)).IsFalse();
        await Assert.That(root.TryGetProperty("capacityPools", out _)).IsFalse();
        JsonElement embedded = root.GetProperty("_embedded");
        await Assert.That(embedded.GetProperty("ticket-types")[0].GetProperty("_links").TryGetProperty("edit", out _)).IsTrue();
        await Assert.That(embedded.GetProperty("ticket-types")[0].GetProperty("_links").TryGetProperty("delete", out _)).IsTrue();
        await Assert.That(embedded.GetProperty("capacity-pools")[0].GetProperty("_links").TryGetProperty("edit", out _)).IsTrue();
        await Assert.That(provider.BatchCalls).IsEqualTo(1);
        await Assert.That(provider.LastBatchSize).IsEqualTo(2);
    }

    [Test]
    public async Task PublishedCatalog_EmitsCloneAndReadOnlyItems()
    {
        var dto = CreateCatalog(TicketCatalogStatusEnum.Published, includeItems: true);
        await using var factory = new TicketingFactory(dto, new TicketingAuthorizationProvider());
        using var client = factory.CreateClient();
        using var request = Authenticated(HttpMethod.Get, dto.EventId);

        using HttpResponseMessage response = await client.SendAsync(request);
        using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement root = document.RootElement;
        JsonElement links = root.GetProperty("_links");

        await Assert.That(links.TryGetProperty("clone-draft", out _)).IsTrue();
        await Assert.That(links.TryGetProperty("publish", out _)).IsFalse();
        await Assert.That(root.GetProperty("_embedded").GetProperty("ticket-types")[0].TryGetProperty("_links", out _)).IsFalse();
        await Assert.That(root.GetProperty("_embedded").GetProperty("capacity-pools")[0].TryGetProperty("_links", out _)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PermissionDeniedOrProviderFailure_OmitsAllWriteLinks(bool providerFails)
    {
        var dto = CreateCatalog(TicketCatalogStatusEnum.Draft, includeItems: true);
        var provider = new TicketingAuthorizationProvider
        {
            AllowBatch = false,
            ThrowFromBatch = providerFails
        };
        await using var factory = new TicketingFactory(dto, provider);
        using var client = factory.CreateClient();
        using var request = Authenticated(HttpMethod.Get, dto.EventId);

        using HttpResponseMessage response = await client.SendAsync(request);
        using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement root = document.RootElement;
        JsonElement links = root.GetProperty("_links");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(links.TryGetProperty("self", out _)).IsTrue();
        await Assert.That(links.TryGetProperty("event", out _)).IsTrue();
        await Assert.That(links.TryGetProperty("create-type", out _)).IsFalse();
        await Assert.That(links.TryGetProperty("create-pool", out _)).IsFalse();
        await Assert.That(links.TryGetProperty("publish", out _)).IsFalse();
        await Assert.That(root.GetProperty("_embedded").GetProperty("ticket-types")[0].TryGetProperty("_links", out _)).IsFalse();
    }

    [Test]
    public async Task PreferMinimal_RemovesLinksAndEmbeddedResources()
    {
        var dto = CreateCatalog(TicketCatalogStatusEnum.Draft, includeItems: true);
        var provider = new TicketingAuthorizationProvider();
        await using var factory = new TicketingFactory(dto, provider);
        using var client = factory.CreateClient();
        using var request = Authenticated(HttpMethod.Get, dto.EventId);
        request.Headers.Add("Prefer", "return=minimal");

        using HttpResponseMessage response = await client.SendAsync(request);
        using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        await Assert.That(response.Headers.GetValues("Preference-Applied")).Contains("return=minimal");
        await Assert.That(document.RootElement.TryGetProperty("_links", out _)).IsFalse();
        await Assert.That(document.RootElement.TryGetProperty("_embedded", out _)).IsFalse();
        await Assert.That(provider.BatchCalls).IsEqualTo(0);
    }

    [Test]
    public async Task TicketingPipeline_ReturnsProblemDetailsFor400401403404And409()
    {
        var eventId = Guid.CreateVersion7();
        var dto = CreateCatalog(TicketCatalogStatusEnum.Draft, includeItems: false, eventId);

        await AssertProblemAsync(new TicketingFactory(dto, new TicketingAuthorizationProvider()),
            new HttpRequestMessage(HttpMethod.Get, $"/api/events/{eventId:D}/ticketing"), HttpStatusCode.Unauthorized, "Authentication required");

        await AssertProblemAsync(new TicketingFactory(dto, new TicketingAuthorizationProvider { AllowRequest = false }),
            Authenticated(HttpMethod.Get, eventId), HttpStatusCode.Forbidden, "Forbidden");

        await AssertProblemAsync(new TicketingFactory(null, new TicketingAuthorizationProvider()),
            Authenticated(HttpMethod.Get, eventId), HttpStatusCode.NotFound, "Resource not found");

        var malformed = Authenticated(HttpMethod.Post, eventId, "ticket-types");
        malformed.Content = new StringContent("{", Encoding.UTF8, "application/json");
        await AssertProblemAsync(new TicketingFactory(dto, new TicketingAuthorizationProvider()),
            malformed, HttpStatusCode.BadRequest, "Validation failed");

        var conflict = Authenticated(HttpMethod.Post, eventId, "publish");
        await AssertProblemAsync(new TicketingFactory(
                dto,
                new TicketingAuthorizationProvider(),
                BaseCommandResponse.Failure<Guid>(
                    "event_ticketing_concurrency_conflict",
                    "Ticketing configuration was updated by another request.",
                    id: eventId)),
            conflict,
            HttpStatusCode.Conflict,
            "Event ticketing conflict");
    }

    private static async Task AssertProblemAsync(
        TicketingFactory factory,
        HttpRequestMessage request,
        HttpStatusCode expectedStatus,
        string expectedTitle)
    {
        await using (factory)
        using (request)
        using (HttpClient client = factory.CreateClient())
        using (HttpResponseMessage response = await client.SendAsync(request))
        {
            await ProblemDetailsAssertions.AssertProblemDetailsAsync(response, expectedStatus, expectedTitle);
        }
    }

    private static HttpRequestMessage Authenticated(HttpMethod method, Guid eventId, string? suffix = null)
    {
        string path = $"/api/events/{eventId:D}/ticketing" + (suffix is null ? string.Empty : $"/{suffix}");
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(Guid.CreateVersion7()));
        return request;
    }

    private static EventTicketCatalogManagementDto CreateCatalog(
        TicketCatalogStatusEnum? status,
        bool includeItems,
        Guid? eventId = null)
    {
        Guid resolvedEventId = eventId ?? Guid.CreateVersion7();
        return new EventTicketCatalogManagementDto
        {
            TenantId = Guid.CreateVersion7(),
            EventId = resolvedEventId,
            ActorId = Guid.CreateVersion7(),
            OrganizerActorId = Guid.CreateVersion7(),
            OrganizerUserId = Guid.CreateVersion7(),
            CatalogId = status.HasValue ? Guid.CreateVersion7() : null,
            VersionNumber = status.HasValue ? 1 : null,
            CurrencyCode = status.HasValue ? "USD" : string.Empty,
            StatusId = status.HasValue ? (int)status.Value : null,
            StatusCode = status?.ToString().ToUpperInvariant(),
            StatusName = status?.ToString(),
            TicketTypes = includeItems
                ? [new EventTicketTypeDto { Id = Guid.CreateVersion7(), Name = "General", TicketPricingModeId = 2 }]
                : [],
            CapacityPools = includeItems
                ? [new EventCapacityPoolDto { Id = Guid.CreateVersion7(), Name = "Main", CapacityOversellPolicyId = 1 }]
                : []
        };
    }

    private sealed class TicketingState
    {
        public EventTicketCatalogManagementDto? QueryResult { get; init; }
        public BaseCommandResponse<Guid> PublishResult { get; init; } = BaseCommandResponse.Success(Guid.Empty);
    }

    private sealed class TicketingFactory(
        EventTicketCatalogManagementDto? queryResult,
        IAuthorizationProvider authorizationProvider,
        BaseCommandResponse<Guid>? publishResult = null) : AuthenticatedWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            AuthorizationProviderOverride = authorizationProvider;
            SeedActiveDefaultTenant = true;
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(new TicketingState
                {
                    QueryResult = queryResult,
                    PublishResult = publishResult ?? BaseCommandResponse.Success(Guid.Empty)
                });

                var catalog = services.SingleOrDefault(d => d.ServiceType == typeof(NativeOperationCatalog))?.ImplementationInstance as NativeOperationCatalog;
                Type[] prodHandlers =
                [
                    typeof(GetEventTicketCatalogManagementQueryHandler),
                    typeof(PublishEventTicketCatalogCommandHandler)
                ];

                if (catalog is not null)
                {
                    foreach (var prod in prodHandlers)
                    {
                        var entries = catalog.Registrations.Where(r => r.Implementation == prod).ToArray();
                        foreach (var entry in entries)
                        {
                            catalog.Registrations.Remove(entry);
                            services.Remove(entry.PublicDescriptor);
                            services.Remove(entry.ConcreteDescriptor);
                        }
                    }
                }

                services.AddNativeOperations([
                    typeof(GetEventTicketCatalogManagementQuery),
                    typeof(QueryHandler),
                    typeof(PublishEventTicketCatalogCommand),
                    typeof(PublishHandler)
                ]);
            });
        }
    }

    private sealed class QueryHandler(TicketingState state)
        : IQueryHandler<GetEventTicketCatalogManagementQuery, EventTicketCatalogManagementDto?>
    {
        public Task<EventTicketCatalogManagementDto?> QueryAsync(
            GetEventTicketCatalogManagementQuery query,
            CancellationToken cancellationToken = default) => Task.FromResult(state.QueryResult);
    }

    private sealed class PublishHandler(TicketingState state)
        : ICommandHandler<PublishEventTicketCatalogCommand, BaseCommandResponse<Guid>>
    {
        public Task<BaseCommandResponse<Guid>> ExecuteAsync(
            PublishEventTicketCatalogCommand command,
            CancellationToken cancellationToken = default) => Task.FromResult(state.PublishResult);
    }

    private sealed class TicketingAuthorizationProvider : IAuthorizationProvider
    {
        public bool AllowRequest { get; init; } = true;
        public bool AllowBatch { get; init; } = true;
        public bool ThrowFromBatch { get; init; }
        public int BatchCalls { get; private set; }
        public int LastBatchSize { get; private set; }

        public Task<AuthorizationDecision> AuthorizeAsync(
            AuthorizationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(AllowRequest
                ? AuthorizationDecision.Allow(AuthorizationProviderMetadata.Local)
                : AuthorizationDecision.Deny(AuthorizationProviderMetadata.Local));

        public Task<IReadOnlyList<AuthorizationDecision>> AuthorizeBatchAsync(
            IReadOnlyList<AuthorizationRequest> requests,
            CancellationToken cancellationToken = default)
        {
            BatchCalls++;
            LastBatchSize = requests.Count;
            return ThrowFromBatch
                ? throw new InvalidOperationException("Provider unavailable")
                : Task.FromResult<IReadOnlyList<AuthorizationDecision>>(requests
                    .Select(_ => AllowBatch
                        ? AuthorizationDecision.Allow(AuthorizationProviderMetadata.Local)
                        : AuthorizationDecision.Deny(AuthorizationProviderMetadata.Local))
                    .ToArray());
        }
    }
}
