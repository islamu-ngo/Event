using Explore.Persistence.Database.ProviderPrimitives;
using System.Diagnostics.Metrics;
using System.Net;
using System.Security.Cryptography;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Configuration;
using Explore.Infrastructure.Webhooks;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure.Webhooks;

internal sealed class WebhookPostAwaitCustodyFixture : IAsyncDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"webhook-custody-{Guid.CreateVersion7():N}.db");
    private readonly TaskCompletionSource _preflightEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _resumePreflight = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Meter _meter = new(BusinessMetrics.MeterName);
    private readonly Transport _transport = new();
    private readonly HttpClient _client;
    private readonly ServiceProvider _services;
    private readonly GlobalExploreDbContextFactory _global;
    private readonly IConfiguration _secrets = Substitute.For<IConfiguration>();
    private readonly string _secret = "whsec_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private WebhookPostAwaitCustodyFixture()
    {
        var options = new PrimaryDatabaseConnectionOptions
        {
            Provider = PrimaryDatabaseProvider.Sqlite,
            Role = PrimaryDatabaseRole.Runtime,
            Database = _path
        };
        Runtime = new TenantExploreDbContextFactory(options);
        _global = new GlobalExploreDbContextFactory(options with { Role = PrimaryDatabaseRole.Global });
        var admission = new WebhookLocalDeliveryAdmission(new AdmissionFactory(_global));
        var policy = new WebhookDeliveryGovernancePolicy(16, 4, 1, 10, 8, 15, 5, "custody-test-v1");
        var governance = Substitute.For<IWebhookDeliveryGovernanceResolver>();
        governance.ResolveAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                if (call.Arg<Guid?>() == TenantId && TargetId != Guid.Empty)
                {
                    bool claimed;
                    await using (var observation = await Runtime.CreateDbContextAsync(
                        TenantId, call.Arg<CancellationToken>()))
                    {
                        claimed = await observation.WebhookLocalTargetSnapshots.AsNoTracking().AnyAsync(
                            row => row.Id == TargetId && row.TenantId == TenantId && row.ProcessingLeaseToken != null,
                            call.Arg<CancellationToken>());
                    }
                    if (claimed)
                    {
                        _preflightEntered.TrySetResult();
                        await _resumePreflight.Task.WaitAsync(call.Arg<CancellationToken>());
                    }
                }
                return policy;
            });
        _services = new ServiceCollection()
            .AddSingleton(Substitute.For<ICurrentUserService>())
            .AddSingleton(Substitute.For<IMachinePrincipalAccessor>())
            .AddSingleton<TimeProvider>(TimeProvider.System)
            .AddSingleton<IWebhookRetentionPolicyResolver>(new WebhookRetentionPolicyResolver(
                new OptionsMonitor<WebhookRetentionSettings>(new WebhookRetentionSettings())))
            .AddSingleton(governance)
            .BuildServiceProvider();
        _secrets[$"{WebhookOptions.SectionName}:EndpointSecrets:custody-test"].Returns(_secret);
        var webhookOptions = new OptionsMonitor<WebhookOptions>(new WebhookOptions
        {
            Enabled = true,
            Provider = WebhookOptions.ProviderLocal
        });
        var meters = Substitute.For<IMeterFactory>();
        meters.Create(Arg.Any<MeterOptions>()).Returns(_meter);
        _client = new HttpClient(_transport) { Timeout = Timeout.InfiniteTimeSpan };
        Service = new WebhookDeliveryDrainService(
            _services.GetRequiredService<IServiceScopeFactory>(),
            admission,
            new WebhookLocalDeliveryExecutionScopeFactory(Runtime, _services.GetRequiredService<IServiceScopeFactory>()),
            new ClientFactory(_client),
            new WebhookSignatureService(),
            new WebhookEndpointSafetyPolicy(webhookOptions),
            new WebhookRetryScheduler(),
            new WebhookEndpointSecretResolver(_secrets),
            Options.Create(new WebhookDeliveryProcessorSettings()),
            webhookOptions,
            new BusinessMetrics(meters),
            NullLogger<WebhookDeliveryDrainService>.Instance);
        _secrets.ClearReceivedCalls();
    }

    internal TenantExploreDbContextFactory Runtime { get; }
    internal WebhookDeliveryDrainService Service { get; }
    internal Guid TenantId { get; private set; }
    internal Guid TargetId { get; private set; }
    internal int TransportCalls => _transport.Calls;
    internal int SecretReads => _secrets.ReceivedCalls().Count();
    internal byte[] Payload => _transport.Payload;
    internal bool SignatureValid => new WebhookSignatureService().Verify(
        _transport.Payload, _transport.Headers, new WebhookSecretMaterial(_secret, 1)).IsValid;

    internal static async Task<WebhookPostAwaitCustodyFixture> CreateAsync()
    {
        var fixture = new WebhookPostAwaitCustodyFixture();
        await using var setup = await fixture._global.CreateDbContextAsync();
        await setup.Database.EnsureCreatedAsync();
        await LookupTableSeeder.SeedAsync(setup);
        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        fixture.TenantId = Guid.CreateVersion7();
        var tenant = new Tenant
        {
            Id = fixture.TenantId,
            FullName = "Webhook custody tenant",
            Slug = $"webhook-custody-{fixture.TenantId:N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
            CreatedAt = now.UtcDateTime
        };
        var consumer = new WebhookConsumer
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            ConsumerKind = WebhookConsumerKind.Tenant,
            Name = "Custody consumer",
            Status = WebhookConsumerStatus.Active,
            ProviderMode = WebhookProviderMode.Local,
            ConfigurationVersion = 1,
            CreatedAt = now.UtcDateTime
        };
        var endpoint = new WebhookEndpoint
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            ConsumerId = consumer.Id,
            Url = "https://8.8.8.8/webhook",
            Status = WebhookEndpointStatus.Active,
            SecretRef = "custody-test",
            SecretVersion = 1,
            SecretActivatedAt = now.AddMinutes(-1).UtcDateTime,
            ConfigurationVersion = 1,
            MaxAttempts = 8,
            TimeoutSeconds = 15,
            CreatedAt = now.UtcDateTime
        };
        var message = WebhookMessage.Create(tenant.Id, "event.published", $"custody-{Guid.CreateVersion7():N}",
            "Event", Guid.CreateVersion7(), consumer.Id, "{\"type\":\"event.published\"}"u8,
            "application/json", "utf-8", now.UtcDateTime, now.AddDays(14).UtcDateTime, now.UtcDateTime);
        var plan = WebhookDeliveryPlanSnapshot.Create(tenant.Id, message.Id, consumer.Id, WebhookProviderMode.Local,
            "consumer-v1", "contract-v1", "standard", "retention-v1", now.AddDays(14), now.AddDays(30),
            now.AddDays(90), now.AddDays(90), now.AddDays(30), now);
        var target = WebhookLocalTargetSnapshot.Create(plan, endpoint, 1, now.AddMinutes(-1), null, now);
        fixture.TargetId = target.Id;
        setup.AddRange(tenant, consumer, endpoint, message, plan, target);
        await setup.SaveChangesAsync();
        return fixture;
    }

    internal async Task AwaitPreflightAsync(Task processing)
    {
        var completed = await Task.WhenAny(_preflightEntered.Task, processing)
            .WaitAsync(TimeSpan.FromSeconds(10));
        if (completed == processing)
        {
            await processing;
            throw new InvalidOperationException("Webhook processing ended before claimed preflight.");
        }
        await _preflightEntered.Task;
    }
    internal void ResumePreflight() => _resumePreflight.TrySetResult();

    internal async Task<CustodyState> InvalidateAsync(string invalidation)
    {
        await using var competitor = await Runtime.CreateDbContextAsync(TenantId);
        var target = await competitor.WebhookLocalTargetSnapshots
            .Include(row => row.WebhookMessage).Include(row => row.WebhookEndpoint)
            .SingleAsync(row => row.Id == TargetId);
        var now = DateTimeOffset.UtcNow;
        switch (invalidation)
        {
            case "paused":
                target.WebhookEndpoint.Status = WebhookEndpointStatus.AutoPaused;
                break;
            case "archived":
                target.WebhookEndpoint.Status = WebhookEndpointStatus.Archived;
                break;
            case "expired":
                competitor.Entry(target).Property(row => row.ProcessingLeaseExpiresAtUtc).CurrentValue = now.AddSeconds(-1);
                break;
            case "replaced":
                target.ScheduleRetry(target.ProcessingLeaseToken!.Value, target.DeliveryFence, now.AddSeconds(1), now);
                target.ClaimForDelivery("competing-worker", Guid.CreateVersion7(), now.AddMinutes(5), now.AddSeconds(1));
                break;
            case "suppressed":
                target.Abandon(target.ProcessingLeaseToken!.Value, target.DeliveryFence, now);
                break;
            case "retention":
            case "cleared":
                competitor.Entry(target.WebhookMessage).Property(row => row.PayloadRetentionUntil)
                    .CurrentValue = now.AddSeconds(-1).UtcDateTime;
                if (invalidation == "cleared")
                    target.WebhookMessage.ClearPayload(now.UtcDateTime);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(invalidation));
        }
        await competitor.SaveChangesAsync();
        return await ReadStateAsync();
    }

    internal async Task<CustodyState> ReadStateAsync()
    {
        await using var verify = await Runtime.CreateDbContextAsync(TenantId);
        var target = await verify.WebhookLocalTargetSnapshots.AsNoTracking()
            .Include(row => row.WebhookEndpoint).Include(row => row.WebhookMessage)
            .SingleAsync(row => row.Id == TargetId);
        return new CustodyState(target.DeliveryStatus, target.ProcessingLeaseToken, target.DeliveryFence,
            target.ProcessingLeaseExpiresAtUtc, target.ConcurrencyVersion, target.WebhookEndpoint.Status,
            target.WebhookEndpoint.ConsecutiveFailureCount, target.WebhookEndpoint.LastSuccessAt,
            target.WebhookMessage.PayloadClearedAt, target.WebhookMessage.PayloadRetentionUntil,
            await verify.WebhookDeliveryAttempts.CountAsync(), await verify.WebhookAuditEvents.CountAsync());
    }

    public async ValueTask DisposeAsync()
    {
        ResumePreflight();
        _client.Dispose();
        _meter.Dispose();
        await _services.DisposeAsync();
        File.Delete(_path);
        File.Delete(_path + "-wal");
        File.Delete(_path + "-shm");
    }

    internal sealed record CustodyState(
        WebhookLocalDeliveryStatus Status, Guid? LeaseToken, long Fence,
        DateTimeOffset? LeaseExpiresAt, long ConcurrencyVersion, WebhookEndpointStatus EndpointStatus,
        int ConsecutiveFailures, DateTime? LastSuccessAt, DateTime? PayloadClearedAt,
        DateTime PayloadRetentionUntil, int Attempts, int Audits);

    private sealed class AdmissionFactory(GlobalExploreDbContextFactory factory) : IWebhookLocalDeliveryAdmissionDbContextFactory
    {
        public Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            factory.CreateDbContextAsync(cancellationToken);
    }

    private sealed class OptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class Transport : HttpMessageHandler
    {
        internal int Calls { get; private set; }
        internal byte[] Payload { get; private set; } = [];
        internal Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Payload = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            foreach (var header in request.Headers)
                Headers[header.Key] = header.Value.Single();
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }
}
