
using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel]
public sealed class AnonymousRegistrationChallengeHttpTests
{
    [Test]
    public async Task GuestStartWithoutProofFailsBeforeAllocationOrReplay()
    {
        await using var host = await NativeHost.CreateAsync();
        using HttpResponseMessage response = await host.StartAsync(host.Key, proof: null);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "anonymous_registration_challenge_invalid");
        await AssertNoDisclosureAsync(response);
        await Assert.That(await host.OrdersAsync()).Count().IsEqualTo(0);
    }

    [Test]
    public async Task ResponseStoreFailureAfterObservedCommitRecoversExactAllocationAcrossReplica()
    {
        var barrier = new DatabaseBarrier(responseStore: true);
        await using var host = await NativeHost.CreateAsync(barrier);
        SolvedProof proof = await host.IssueAsync(host.Key);
        await Assert.That(await host.OrdersAsync()).Count().IsEqualTo(0);
        Task<HttpResponseMessage> pending = host.StartAsync(host.Key, proof);
        await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        RegistrationOrder committed;
        IReadOnlyList<RegistrationInventoryHold> holds;
        try
        {
            committed = (await host.OrdersAsync()).Single();
            holds = await host.HoldsAsync(committed.Id);
            await Assert.That(holds.Count).IsEqualTo(1);
        }
        finally
        {
            barrier.Release.TrySetResult();
        }
        using HttpResponseMessage failed = await pending.WaitAsync(TimeSpan.FromSeconds(30));
        await AssertProblemAsync(failed, HttpStatusCode.ServiceUnavailable, "idempotency_unavailable");
        await AssertNoDisclosureAsync(failed);
        await using (AsyncServiceScope scope = host.Factory.Services.CreateAsyncScope())
        {
            IdempotencyRecord record = (await scope.ServiceProvider.GetRequiredService<IIdempotencyRepository>()
                .FindAsync(host.Key, PlatformDefaults.DefaultTenantId))!;
            await Assert.That(record.StatusCode).IsEqualTo(0);
            await Assert.That(record.ResponseBody).IsNull();
        }

        await using WebApplicationFactory<Program> replica = host.CreateReplica();
        using HttpClient replicaClient = replica.CreateClient(new() { AllowAutoRedirect = false });
        using HttpResponseMessage recovered = await host.StartAsync(host.Key, proof, client: replicaClient);
        await Assert.That(recovered.StatusCode).IsEqualTo(HttpStatusCode.Created);
        await Assert.That(await ReadIdAsync(recovered)).IsEqualTo(committed.Id);
        string capability = recovered.Headers.GetValues("X-Registration-Order-Capability").Single();
        await Assert.That(recovered.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(recovered.Headers.Location!.AbsolutePath).Contains(committed.Id.ToString("D"));

        using HttpResponseMessage repeated = await host.StartAsync(host.Key, proof);
        await Assert.That(await ReadIdAsync(repeated)).IsEqualTo(committed.Id);
        await Assert.That(string.Equals(repeated.Headers.GetValues("X-Registration-Order-Capability").Single(),
            capability, StringComparison.Ordinal)).IsTrue();
        await Assert.That(await host.OrdersAsync()).Count().IsEqualTo(1);
        var finalHolds = await host.HoldsAsync(committed.Id);
        await Assert.That(finalHolds.Single().Id).IsEqualTo(holds.Single().Id);
        await Assert.That(finalHolds.Single().ExpiresAt).IsEqualTo(holds.Single().ExpiresAt);

        using HttpResponseMessage wrongKey = await host.StartAsync(Guid.CreateVersion7().ToString("N"), proof);
        using HttpResponseMessage wrongBody = await host.StartAsync(host.Key, proof, body: host.Body.Replace("\"quantity\":1", "\"quantity\":2", StringComparison.Ordinal));
        using HttpResponseMessage wrongEvent = await host.StartAsync(host.Key, proof, eventId: Guid.CreateVersion7());
        foreach (HttpResponseMessage denied in new[] { wrongKey, wrongBody, wrongEvent })
        {
            await AssertProblemAsync(denied, HttpStatusCode.BadRequest, "anonymous_registration_challenge_invalid");
            await AssertNoDisclosureAsync(denied);
        }
        await Assert.That(await host.OrdersAsync()).Count().IsEqualTo(1);
    }

    [Test]
    public async Task CachedReplayRequiresOriginalEnvelopeAndProofAndBoundsHistoricalRecovery()
    {
        await using var host = await NativeHost.CreateAsync();
        SolvedProof original = await host.IssueAsync(host.Key);
        SolvedProof alternate = await host.IssueAsync(host.Key);
        using HttpResponseMessage started = await host.StartAsync(host.Key, original);
        await Assert.That(started.StatusCode).IsEqualTo(HttpStatusCode.Created);
        Guid orderId = await ReadIdAsync(started);
        string capability = started.Headers.GetValues("X-Registration-Order-Capability").Single();
        await using (AsyncServiceScope scope = host.Factory.Services.CreateAsyncScope())
        {
            IdempotencyRecord record = (await scope.ServiceProvider.GetRequiredService<IIdempotencyRepository>()
                .FindAsync(host.Key, PlatformDefaults.DefaultTenantId))!;
            await Assert.That(record.ResponseBody!).StartsWith("dp:v1:");
            await Assert.That(record.ResponseBody!.Contains(capability, StringComparison.Ordinal)).IsFalse();
        }
        using HttpResponseMessage substituted = await host.StartAsync(host.Key, alternate);
        await AssertProblemAsync(substituted, HttpStatusCode.Conflict, "idempotency_key_reuse");
        await AssertNoDisclosureAsync(substituted);
        using HttpResponseMessage invalid = await host.StartAsync(host.Key, original with { Nonce = "not-a-valid-proof" });
        await AssertProblemAsync(invalid, HttpStatusCode.BadRequest, "anonymous_registration_challenge_invalid");
        await AssertNoDisclosureAsync(invalid);
        using HttpResponseMessage missing = await host.StartAsync(host.Key, null);
        await AssertNoDisclosureAsync(missing);

        string neverStartedKey = Guid.CreateVersion7().ToString("N");
        SolvedProof neverStarted = await host.IssueAsync(neverStartedKey);
        host.Clock.Advance(TimeSpan.FromMinutes(3));
        using HttpResponseMessage expiredFresh = await host.StartAsync(neverStartedKey, neverStarted);
        await AssertProblemAsync(expiredFresh, HttpStatusCode.BadRequest, "anonymous_registration_challenge_invalid");
        await AssertNoDisclosureAsync(expiredFresh);
        using HttpResponseMessage historical = await host.StartAsync(host.Key, original);
        await Assert.That(historical.StatusCode).IsEqualTo(HttpStatusCode.Created);
        await Assert.That(await ReadIdAsync(historical)).IsEqualTo(orderId);
        await Assert.That(string.Equals(historical.Headers.GetValues("X-Registration-Order-Capability").Single(),
            capability, StringComparison.Ordinal)).IsTrue();
        await Assert.That(await host.OrdersAsync()).Count().IsEqualTo(1);
        host.Clock.Advance(TimeSpan.FromHours(24) - TimeSpan.FromMinutes(2.5));
        using HttpResponseMessage afterCacheExpiry = await host.StartAsync(host.Key, original);
        await Assert.That(afterCacheExpiry.StatusCode).IsEqualTo(HttpStatusCode.Created);
        await Assert.That(await ReadIdAsync(afterCacheExpiry)).IsEqualTo(orderId);
        await using (AsyncServiceScope scope = host.Factory.Services.CreateAsyncScope())
        {
            IdempotencyRecord record = (await scope.ServiceProvider.GetRequiredService<IIdempotencyRepository>()
                .FindAsync(host.Key, PlatformDefaults.DefaultTenantId))!;
            await Assert.That(record.ExpiresAt - host.Clock.GetUtcNow().UtcDateTime).IsEqualTo(TimeSpan.FromSeconds(90));
        }
        await Assert.That(await host.OrdersAsync()).Count().IsEqualTo(1);
        host.Clock.Advance(TimeSpan.FromMinutes(2));
        using HttpResponseMessage outsideRetention = await host.StartAsync(host.Key, original);
        await AssertProblemAsync(outsideRetention, HttpStatusCode.BadRequest, "anonymous_registration_challenge_invalid");
        await AssertNoDisclosureAsync(outsideRetention);
    }

    [Test]
    public async Task LiveClaimWithoutCommittedOrderCannotBeTakenOver()
    {
        var barrier = new DatabaseBarrier(responseStore: false);
        await using var host = await NativeHost.CreateAsync(barrier);
        SolvedProof proof = await host.IssueAsync(host.Key);
        Task<HttpResponseMessage> owner = host.StartAsync(host.Key, proof);
        await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        try
        {
            using HttpResponseMessage overlapping = await host.StartAsync(host.Key, proof);
            await AssertProblemAsync(overlapping, HttpStatusCode.Conflict, "idempotency_request_in_progress");
            await AssertNoDisclosureAsync(overlapping);
            await Assert.That(await host.OrdersAsync()).Count().IsEqualTo(0);
        }
        finally
        {
            barrier.Release.TrySetResult();
        }
        using HttpResponseMessage completed = await owner.WaitAsync(TimeSpan.FromSeconds(30));
        await Assert.That(completed.StatusCode).IsEqualTo(HttpStatusCode.Created);
        await Assert.That(await host.OrdersAsync()).Count().IsEqualTo(1);
    }

    [Test]
    public async Task ChangedTenantAndCapabilityScopeCannotDiscloseCommittedReplay()
    {
        await using var host = await NativeHost.CreateAsync(pathBase: "/mounted");
        SolvedProof proof = await host.IssueAsync(host.Key);
        using HttpResponseMessage started = await host.StartAsync(host.Key, proof);
        await Assert.That(started.StatusCode).IsEqualTo(HttpStatusCode.Created);
        Guid otherTenant = await host.SeedTenantAsync();
        await using WebApplicationFactory<Program> replica = host.CreateReplica(otherTenant);
        using HttpClient client = replica.CreateClient(new() { AllowAutoRedirect = false });
        using HttpResponseMessage wrongTenant = await host.StartAsync(host.Key, proof, client: client);
        await AssertProblemAsync(wrongTenant, HttpStatusCode.BadRequest, "anonymous_registration_challenge_invalid");
        await AssertNoDisclosureAsync(wrongTenant);
        using HttpResponseMessage changedCapability = await host.StartAsync(host.Key, proof,
            capability: Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        using HttpResponseMessage changedQuery = await host.StartAsync(host.Key, proof, query: "?different=1");
        using HttpResponseMessage changedContentType = await host.StartAsync(host.Key, proof, mediaType: "application/json; charset=us-ascii");
        foreach (HttpResponseMessage denied in new[] { changedCapability, changedQuery, changedContentType })
        {
            await AssertProblemAsync(denied, HttpStatusCode.BadRequest, "anonymous_registration_challenge_invalid");
            await AssertNoDisclosureAsync(denied);
        }
        using JsonDocument body = JsonDocument.Parse(host.Body);
        string reformatted = "{\n\"lines\":" + body.RootElement.GetProperty("lines").GetRawText()
            + ", \"platformContributionBasisPoints\":null,\"bookingPartyType\":1,\"ticketCatalogVersionId\":"
            + body.RootElement.GetProperty("ticketCatalogVersionId").GetRawText() + "}";
        using HttpResponseMessage equivalent = await host.StartAsync(host.Key, proof, body: reformatted);
        await Assert.That(equivalent.StatusCode).IsEqualTo(HttpStatusCode.Created);
        await Assert.That(await ReadIdAsync(equivalent)).IsEqualTo(await ReadIdAsync(started));
        await Assert.That(await host.OrdersAsync()).Count().IsEqualTo(1);
    }

    [Test]
    public async Task EffectiveIpBudgetCoversIssuanceAndStartWithoutLoopbackBypass()
    {
        await using var host = await NativeHost.CreateAsync(ipLimit: 1);
        SolvedProof proof = await host.IssueAsync(host.Key);
        using HttpResponseMessage limited = await host.StartAsync(host.Key, proof);
        await AssertProblemAsync(limited, HttpStatusCode.TooManyRequests, "rate_limited");
        await AssertNoDisclosureAsync(limited);
        await Assert.That(limited.Headers.RetryAfter).IsNotNull();
        await Assert.That(limited.Headers.GetValues("X-RateLimit-Limit").Single()).IsEqualTo("1");
        await Assert.That(await host.OrdersAsync()).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SubnetBudgetCombinesDistinctEffectiveAddresses()
    {
        await using var host = await NativeHost.CreateAsync(subnetLimit: 1);
        using HttpResponseMessage first = await host.StartAsync(host.Key, null, remoteAddress: "198.51.100.11");
        using HttpResponseMessage second = await host.StartAsync(host.Key, null, remoteAddress: "198.51.100.12");
        await AssertProblemAsync(first, HttpStatusCode.BadRequest, "anonymous_registration_challenge_invalid");
        await AssertProblemAsync(second, HttpStatusCode.TooManyRequests, "rate_limited");
        await AssertNoDisclosureAsync(second);
        await Assert.That(await host.OrdersAsync()).Count().IsEqualTo(0);
    }

    [Test]
    public async Task ConcurrencyLimitRejectsWhileExactAllocationIsPausedWithoutQueueing()
    {
        var barrier = new DatabaseBarrier(responseStore: false);
        await using var host = await NativeHost.CreateAsync(barrier, concurrency: 1);
        SolvedProof proof = await host.IssueAsync(host.Key);
        Task<HttpResponseMessage> owner = host.StartAsync(host.Key, proof);
        await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        try
        {
            using HttpResponseMessage rejected = await host.StartAsync(Guid.CreateVersion7().ToString("N"), null);
            await AssertProblemAsync(rejected, HttpStatusCode.TooManyRequests, "rate_limited");
            await AssertNoDisclosureAsync(rejected);
        }
        finally { barrier.Release.TrySetResult(); }
        using HttpResponseMessage completed = await owner.WaitAsync(TimeSpan.FromSeconds(30));
        await Assert.That(completed.StatusCode).IsEqualTo(HttpStatusCode.Created);
        await Assert.That(await host.OrdersAsync()).Count().IsEqualTo(1);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        await Assert.That(response.StatusCode).IsEqualTo(status);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(document.RootElement.GetProperty("code").GetString()).IsEqualTo(code);
    }

    private static async Task AssertNoDisclosureAsync(HttpResponseMessage response)
    {
        await Assert.That(response.Headers.Contains("X-Registration-Order-Capability")).IsFalse();
        await Assert.That(response.Headers.Contains("X-Idempotency-Replay")).IsFalse();
        await Assert.That(response.Headers.Location).IsNull();
    }

    private static async Task<Guid> ReadIdAsync(HttpResponseMessage response)
    {
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("id").GetGuid();
    }

    internal sealed record SolvedProof(string Envelope, string Nonce)
    {
        public override string ToString() => "SolvedProof { Redacted = true }";
    }

    internal sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan amount) => _now += amount;
    }

    internal sealed class DatabaseBarrier(bool responseStore) : DbCommandInterceptor
    {
        private int _entered;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await PauseAsync(command, eventData, cancellationToken);
            return result;
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            await PauseAsync(command, eventData, cancellationToken);
            return result;
        }

        private async Task PauseAsync(DbCommand command, CommandEventData eventData, CancellationToken cancellationToken)
        {
            Type entity = responseStore ? typeof(IdempotencyRecord) : typeof(RegistrationOrder);
            string table = eventData.Context!.Model.FindEntityType(entity)!.GetTableName()!;
            string prefix = responseStore ? "UPDATE " : "INSERT INTO ";
            if (!command.CommandText.StartsWith(prefix, StringComparison.Ordinal)
                || !command.CommandText.Contains('"' + table + '"', StringComparison.Ordinal)
                || Interlocked.CompareExchange(ref _entered, 1, 0) != 0)
                return;
            Entered.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            if (responseStore)
                throw new IOException("Controlled response-store failure after observed allocation commit.");
        }
    }

    internal sealed class NativeHost : IAsyncDisposable
    {
        private readonly LocalAdmissionWebApplicationFactory _base;
        private int _ipLimit = 100;
        private int _subnetLimit = 200;
        private int _concurrency = 8;
        private string _pathBase = string.Empty;
        private readonly DirectoryInfo _keys = Directory.CreateTempSubdirectory("anonymous-http-keys-");
        public WebApplicationFactory<Program> Factory { get; private set; } = null!;
        public HttpClient Client { get; private set; } = null!;
        public TestClock Clock { get; } = new();
        public Guid EventId { get; private set; }
        public string Body { get; private set; } = null!;
        public string Key { get; } = Guid.CreateVersion7().ToString("N");

        private NativeHost(LocalAdmissionWebApplicationFactory factory) => _base = factory;

        public static async Task<NativeHost> CreateAsync(DatabaseBarrier? barrier = null,
            int ipLimit = 100, int subnetLimit = 200, int concurrency = 8, string pathBase = "")
        {
            var host = new NativeHost(await LocalAdmissionWebApplicationFactory.CreateAsync(
                persistenceInterceptor: barrier, enableRateLimiting: true))
            {
                _ipLimit = ipLimit, _subnetLimit = subnetLimit, _concurrency = concurrency, _pathBase = pathBase
            };
            host.Factory = host.CreateReplica();
            host.Client = host.Factory.CreateClient(new() { AllowAutoRedirect = false });
            await host.SeedAsync();
            return host;
        }

        public WebApplicationFactory<Program> CreateReplica(Guid? tenantId = null) => _base.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:AnonymousRegistration:IpPermitLimit"] = _ipLimit.ToString(CultureInfo.InvariantCulture),
                ["RateLimiting:AnonymousRegistration:SubnetPermitLimit"] = _subnetLimit.ToString(CultureInfo.InvariantCulture),
                ["RateLimiting:AnonymousRegistration:ConcurrencyLimit"] = _concurrency.ToString(CultureInfo.InvariantCulture),
                ["Deployment:DefaultTenantId"] = (tenantId ?? PlatformDefaults.DefaultTenantId).ToString("D")
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);
                services.AddSingleton<IStartupFilter>(new NativeTransport(_pathBase));
                services.AddDataProtection().PersistKeysToFileSystem(_keys).SetApplicationName("islamu-event");
            });
        });

        public async Task<HttpResponseMessage> StartAsync(string key, SolvedProof? proof, string? body = null,
            Guid? eventId = null, HttpClient? client = null, string? capability = null,
            string query = "", string? mediaType = null, string remoteAddress = "127.0.0.1")
        {
            using var request = Request($"{_pathBase}/api/events/{eventId ?? EventId:D}/registration-orders/guest{query}", key, body ?? Body);
            request.Headers.Add("Test-Remote-Address", remoteAddress);
            if (capability is not null) request.Headers.Add("X-Registration-Order-Capability", capability);
            if (mediaType is not null) request.Content!.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(mediaType);
            if (proof is not null)
            {
                request.Headers.Add("X-Registration-Challenge", proof.Envelope);
                request.Headers.Add("X-Registration-Proof", proof.Nonce);
            }
            return await (client ?? Client).SendAsync(request);
        }

        public async Task<string> LoginAsync()
        {
            var credentials = await _base.SeedLocalUserAsync(emailConfirmed: true);
            using var request = Request("/api/auth/local/login", Guid.CreateVersion7().ToString("N"), JsonSerializer.Serialize(credentials));
            using HttpResponseMessage response = await Client.SendAsync(request);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return body.RootElement.GetProperty("token").GetString()!;
        }

        public async Task<SolvedProof> IssueAsync(string key, string? body = null)
        {
            using var request = Request($"{_pathBase}/api/events/{EventId:D}/guest-registration-challenges", key, body ?? Body);
            request.Headers.Add("Test-Remote-Address", "127.0.0.1");
            using HttpResponseMessage response = await Client.SendAsync(request);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
            using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            string envelope = document.RootElement.GetProperty("protectedChallenge").GetString()!;
            int difficulty = document.RootElement.GetProperty("difficulty").GetInt32();
            await Assert.That(document.RootElement.GetProperty("version").GetInt32()).IsEqualTo(1);
            return new SolvedProof(envelope, Solve(envelope, difficulty));
        }

        public async Task<IReadOnlyList<RegistrationOrder>> OrdersAsync()
        {
            await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IRegistrationInventoryRepository>()
                .GetOrdersByEventAsync(EventId, PlatformDefaults.DefaultTenantId, CancellationToken.None);
        }

        public async Task<IReadOnlyList<RegistrationInventoryHold>> HoldsAsync(Guid orderId)
        {
            await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IRegistrationInventoryRepository>()
                .GetHoldsByOrderAsync(orderId, PlatformDefaults.DefaultTenantId, CancellationToken.None);
        }

        public async Task<Guid> SeedTenantAsync()
        {
            await using ExploreDbContext database = _base.CreateDatabase();
            Guid id = Guid.CreateVersion7();
            database.Tenants.Add(new Tenant
            {
                Id = id, Slug = $"other-{id:N}", FullName = "Other scope", TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!, CreatedAt = Clock.GetUtcNow().UtcDateTime
            });
            await database.SaveChangesAsync();
            return id;
        }

        private sealed class NativeTransport(string pathBase) : IStartupFilter
        {
            public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
            {
                if (pathBase.Length > 0) app.UsePathBase(pathBase);
                app.Use(async (context, continuation) =>
                {
                    if (context.Request.Headers.TryGetValue("Test-Remote-Address", out var address))
                        context.Connection.RemoteIpAddress = IPAddress.Parse(address.ToString());
                    await continuation(context);
                });
                next(app);
            };
        }

        private async Task SeedAsync()
        {
            await using ExploreDbContext database = _base.CreateDatabase();
            Guid tenantId = PlatformDefaults.DefaultTenantId;
            Guid userId = (await database.InstanceBootstrapStates.SingleAsync()).CompletedByUserId!.Value;
            var actor = new Actor
            {
                Id = Guid.CreateVersion7(), ActorTypeId = (int)ActorTypeEnum.User, ActorType = null!,
                UserId = userId, Pii = new ActorPii { DisplayName = "Anonymous event operator" }, CreatedAt = Clock.GetUtcNow().UtcDateTime
            };
            EventId = Guid.CreateVersion7();
            var target = new Explore.Domain.Event(EventStatusEnum.Published)
            {
                Id = EventId, Title = "Native anonymous event", TenantId = tenantId, Tenant = null!,
                ActorId = actor.Id, Actor = actor, OrganizerActorId = actor.Id,
                EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
                VisibilityTypeId = (int)VisibilityTypeEnum.Public, VisibilityType = null!,
                EventFormatId = (int)EventFormatEnum.Local, EventFormat = null!, EventStatus = null!,
                SessionCount = 1,
                FirstSessionStartUtc = Clock.GetUtcNow().AddDays(30), LastSessionEndUtc = Clock.GetUtcNow().AddDays(31),
                CreatedAt = Clock.GetUtcNow().UtcDateTime
            };
            target.ParticipationConfiguration = EventParticipationConfiguration.Create(EventId, tenantId,
                (int)ParticipationHandlingModeEnum.PlatformManaged, (int)AdvanceRegistrationObligationEnum.Required,
                (int)IdentityAccessModeEnum.GuestAllowed, GuestRecoveryPolicyEnum.EmailOptional, Clock.GetUtcNow().UtcDateTime);
            var catalog = EventTicketCatalogVersion.Create(tenantId, EventId, "USD", 1);
            var pool = EventCapacityPool.Create(tenantId, EventId, "Native capacity", 10, 900,
                CapacityHoldPolicyEnum.TimedHoldOnSelection, CapacityOversellPolicyEnum.Disallow, true);
            var ticket = EventTicketType.Create(Guid.CreateVersion7(), tenantId, catalog.Id, "Free admission", "USD",
                TicketPricingModeEnum.Free, null, null, null, ParticipantDataCollectionModeEnum.None, pool.Id,
                null, null, false, false, null, null, null, null);
            catalog.AddTicketType(ticket, pool);
            catalog.AddEntitlement(ticket, TicketTypeEntitlement.CreateForEvent(ticket.Id, tenantId, EventId, 1));
            catalog.Publish();
            database.AddRange(actor, target, catalog, pool, new TenantUser
            {
                Id = Guid.CreateVersion7(), TenantId = tenantId, Tenant = null!, UserId = userId, User = null!,
                ActorId = actor.Id, StatusId = (int)TenantUserStatusEnum.Active,
                JoinedAt = Clock.GetUtcNow().UtcDateTime, CreatedAt = Clock.GetUtcNow().UtcDateTime
            });
            await database.SaveChangesAsync();
            Body = JsonSerializer.Serialize(new
            {
                ticketCatalogVersionId = catalog.Id,
                bookingPartyType = (int)BookingPartyTypeEnum.Individual,
                lines = new[] { new { ticketTypeId = ticket.Id, quantity = 1, chosenUnitPriceMinor = (long?)null } },
                platformContributionBasisPoints = (int?)null
            });
        }

        private static HttpRequestMessage Request(string path, string key, string body)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Idempotency-Key", key);
            return request;
        }

        private static string Solve(string envelope, int difficulty)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            byte[] prefix = Encoding.UTF8.GetBytes("islamu-event:anonymous-registration:v1\n" + envelope + "\n");
            byte[] work = new byte[prefix.Length + 16];
            prefix.CopyTo(work, 0);
            Span<byte> digest = stackalloc byte[32];
            for (ulong candidate = 0; ; candidate++)
            {
                if (candidate % 1024 == 0) timeout.Token.ThrowIfCancellationRequested();
                string nonce = candidate.ToString("x16", CultureInfo.InvariantCulture);
                Encoding.ASCII.GetBytes(nonce, work.AsSpan(prefix.Length));
                SHA256.HashData(work, digest);
                bool valid = true;
                for (int bit = 0; bit < difficulty; bit++)
                    if ((digest[bit / 8] & (1 << (7 - bit % 8))) != 0) { valid = false; break; }
                if (valid) return nonce;
            }
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await Factory.DisposeAsync();
            await _base.DisposeAsync();
            _keys.Delete(recursive: true);
        }
    }
}
