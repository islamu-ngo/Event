using System.Data.Common;
using System.Text.Json;
using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Event.Testing.DataProtection;
using Explore.API.BackgroundServices;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Events.Discovery.Commands;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.ServiceDefaults.DataProtection;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Event.Api.IntegrationTests.Features.Notifications;

public sealed partial class NotificationOutboxWorkerAdmissionTests
{
    [Test]
    [Arguments("discovery", "read")]
    [Arguments("discovery", "bind")]
    [Arguments("discovery", "require")]
    [Arguments("discovery", "worker")]
    [Arguments("discovery", "live")]
    [Arguments("publication", "read")]
    [Arguments("publication", "bind")]
    [Arguments("publication", "require")]
    [Arguments("publication", "worker")]
    [Arguments("publication", "live")]
    public async Task SourceReaderCompletionMustNotAdmitExpiredNotification(string route, string boundary)
    {
        var clock = new AdmissionClock(DateTime.UtcNow);
        var signal = new SourceReadClockAdvance(clock);
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using var hosted = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
            services.RemoveAll<IExternalDataProtectionSource>();
            services.AddSingleton<IExternalDataProtectionSource>(new ExternalDataProtectionTestAuthority("api", clock));
            services.RemoveAll<INotificationOutboxAdmissionDbContextFactory>();
            services.AddSingleton<INotificationOutboxAdmissionDbContextFactory>(provider =>
                new ObservedAdmissionFactory(provider.GetRequiredService<IDbContextFactory<ExploreDbContext>>(), signal));
        }));
        using var client = hosted.CreateClient();
        Guid tenantId;
        OutboxMessage message;
        await using (var db = factory.CreateDatabase())
        {
            var publisher = await TenantScenarioSeed.SeedActiveTenantWithOrganizationPublisherAsync(db);
            tenantId = publisher.TenantId;
            var source = new EventBuilder().WithTenantId(tenantId).WithActorId(publisher.OrganizationActorId)
                .WithStatus(EventStatusEnum.Published).Build();
            db.Events.Add(source);
            if (route == "publication")
                message = EventPublishedOutboxMessageFactory.CreateNotificationFanoutOutboxMessage(
                    Guid.CreateVersion7(), source, clock.GetUtcNow());
            else
            {
                string payload = JsonSerializer.Serialize(new EventDiscoveryIdentityCorrectionRequested(
                    tenantId, source.Id, source.Id, 1, "confirmed_alias", "same_offering"));
                db.Set<AuditLog>().Add(new AuditLog
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenantId,
                    Tenant = null!,
                    ActorId = publisher.UserId,
                    EntityType = "EventDiscoveryIdentity",
                    EntityId = source.Id.ToString("D"),
                    Action = "EventDiscoveryAliasConfirmed",
                    NewValues = payload,
                    Timestamp = clock.UtcNow
                });
                message = new OutboxMessage
                {
                    Id = Guid.CreateVersion7(),
                    AggregateType = "EventDiscoveryIdentity",
                    AggregateId = source.Id,
                    EventType = EventDiscoveryIdentityCorrectionRequested.EventType,
                    Payload = payload,
                    CreatedAt = clock.UtcNow,
                    Status = OutboxMessageStatus.Pending,
                    MaxRetries = 5
                };
            }
            db.OutboxMessages.Add(message);
            await db.SaveChangesAsync();
            signal.SourceTable = db.Model.FindEntityType(route == "publication" ? typeof(Explore.Domain.Event) : typeof(AuditLog))!
                .GetTableName()!;
        }
        await using var admissionScope = hosted.Services.CreateAsyncScope();
        var admission = admissionScope.ServiceProvider.GetRequiredService<INotificationOutboxAdmission>();
        var claim = (await admission.ClaimDueAsync(1000, clock.UtcNow, default)).Single(row => row.MessageId == message.Id);
        var original = await admission.ReadClaimAsync(message.Id, claim.ProcessingLeaseExpiresAt, default);
        await Assert.That(original).IsEqualTo(claim);
        await using var runtime = hosted.Services.CreateAsyncScope();
        runtime.ServiceProvider.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(tenantId);
        var authority = runtime.ServiceProvider.GetRequiredService<INotificationOutboxExecutionAuthority>();
        await authority.BindAsync(claim, default);
        var retained = await runtime.ServiceProvider.GetRequiredService<IOutboxRepository>()
            .GetClaimedNotificationAsync(message.Id, claim.ProcessingLeaseExpiresAt);
        await Assert.That(retained).IsNotNull();
        clock.UtcNow = claim.ProcessingLeaseExpiresAt.AddTicks(-1);
        signal.AdvanceTo = boundary == "live" ? clock.UtcNow : claim.ProcessingLeaseExpiresAt;
        signal.Armed = true;
        if (boundary is "read" or "live")
        {
            var observed = await admission.ReadClaimAsync(message.Id, claim.ProcessingLeaseExpiresAt, default);
            if (boundary == "live")
            {
                await Assert.That(observed).IsEqualTo(claim);
                await authority.RequireAsync(retained!, default);
            }
            else
                await Assert.That(observed).IsNull();
        }
        else if (boundary == "bind")
        {
            await using var fresh = hosted.Services.CreateAsyncScope();
            fresh.ServiceProvider.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(tenantId);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fresh.ServiceProvider.GetRequiredService<INotificationOutboxExecutionAuthority>().BindAsync(claim, default));
        }
        else if (boundary == "require")
            await Assert.ThrowsAsync<InvalidOperationException>(() => authority.RequireAsync(retained!, default));
        else
        {
            var processor = new OutboxProcessor(hosted.Services, Options.Create(new OutboxProcessorSettings()),
                NullLogger<OutboxProcessor>.Instance);
            await Assert.ThrowsAsync<InvalidOperationException>(() => processor.ProcessNotificationClaimAsync(claim, default));
        }
        await Assert.That(signal.Fired).IsTrue();
        await Assert.That(clock.UtcNow).IsEqualTo(signal.AdvanceTo);
        var database = runtime.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await Assert.That(await database.NotificationFanoutRuns.AnyAsync(row => row.EntityId == message.AggregateId)).IsFalse();
        await Assert.That(await database.Notifications.AnyAsync()).IsFalse();
        var unchanged = await database.OutboxMessages.AsNoTracking().SingleAsync(row => row.Id == message.Id);
        await Assert.That(unchanged.Status).IsEqualTo(OutboxMessageStatus.Processing);
        await Assert.That(unchanged.NextRetryAt).IsEqualTo(claim.ProcessingLeaseExpiresAt);
        await Assert.That(unchanged.Payload).IsEqualTo(message.Payload);
    }

    private sealed class AdmissionClock(DateTime initial) : TimeProvider
    {
        public DateTime UtcNow { get; set; } = initial;
        public override DateTimeOffset GetUtcNow() => new(UtcNow);
    }

    private sealed class SourceReadClockAdvance(AdmissionClock clock) : DbCommandInterceptor
    {
        public string SourceTable { get; set; } = string.Empty;
        public DateTime AdvanceTo { get; set; }
        public bool Armed { get; set; }
        public bool Fired { get; private set; }
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (Armed && command.CommandText.Contains($"FROM \"{SourceTable}\"", StringComparison.Ordinal))
            {
                clock.UtcNow = AdvanceTo;
                Armed = false;
                Fired = true;
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ObservedAdmissionFactory(IDbContextFactory<ExploreDbContext> factory, SourceReadClockAdvance signal)
        : INotificationOutboxAdmissionDbContextFactory
    {
        public async Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            await using var template = await factory.CreateDbContextAsync(cancellationToken);
            return new ExploreDbContext(new DbContextOptionsBuilder<ExploreDbContext>(
                (DbContextOptions<ExploreDbContext>)template.GetService<IDbContextOptions>())
                .AddInterceptors(signal).Options);
        }
    }
}
