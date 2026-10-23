using Explore.Persistence.Database.ProviderPrimitives;
using System.Text.Json;
using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.API.BackgroundServices;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel]
public sealed class GenericOutboxWorkerAdmissionTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SubjectConvergence_UsesExactClaim_WithoutTenantFallback(bool replace)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using var hosted = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(GenericOutboxWorkerTestServices.Configure));
        using var client = hosted.CreateClient();
        var message = PrivacyErasureCacheInvalidationOutboxMessageFactory.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), DateTime.UtcNow);
        await using var seed = hosted.Services.CreateAsyncScope();
        var repository = seed.ServiceProvider.GetRequiredService<IOutboxRepository>();
        await repository.Create(message);
        var claim = (await seed.ServiceProvider.GetRequiredService<IGenericOutboxAdmission>()
            .ClaimDueAsync(1000, DateTime.UtcNow, CancellationToken.None)).Single(row => row.MessageId == message.Id);
        await Assert.That(claim.TenantId).IsNull();
        await using (var prepared = await seed.ServiceProvider.GetRequiredService<IGenericOutboxExecutionScopeFactory>()
            .CreateAsync(claim, CancellationToken.None))
        {
            var runtime = prepared.Services.GetRequiredService<ExploreDbContext>();
            await Assert.That(runtime.TenantFilterTenantId).IsNull();
            await Assert.That(runtime.IsTenantFilterBypassed).IsFalse();
        }
        var processor = CreateProcessor(hosted.Services);
        DateTime? successor = null;
        if (replace)
        {
            successor = await repository.TryClaimForProcessing(message.Id, claim.LeaseExpiresAt);
            await Assert.That(successor).IsNotNull();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                processor.ProcessGenericClaimAsync(claim, CancellationToken.None));
        }
        else
            await processor.ProcessGenericClaimAsync(claim, CancellationToken.None);
        var database = seed.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var stored = await database.OutboxMessages.AsNoTracking().SingleAsync(row => row.Id == message.Id);
        await Assert.That(stored.Status).IsEqualTo(replace ? OutboxMessageStatus.Processing : OutboxMessageStatus.Completed);
        await Assert.That(stored.NextRetryAt).IsEqualTo(successor);
    }

    [Test]
    [Arguments("original")]
    [Arguments("foreign")]
    [Arguments("wrong-source")]
    public async Task NativeRecoveryIntent_DerivesNondefaultTenant_AndCannotRetarget(string variant)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using var hosted = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(GenericOutboxWorkerTestServices.Configure));
        using var client = hosted.CreateClient();
        await using var seed = hosted.Services.CreateAsyncScope();
        var database = seed.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var tenant = new TenantBuilder().Build();
        database.Tenants.Add(tenant);
        var id = Guid.CreateVersion7();
        DateTime now = DateTime.UtcNow;
        var intent = new AdmissionRecoveryRequestIntent(id, tenant.Id, "protected-test-envelope", 1, now);
        // Already consumed intent proves recovery is not executed or rematerialized on replay.
        intent.Complete(now);
        database.AdmissionRecoveryRequestIntents.Add(intent);
        var message = new OutboxMessage
        {
            Id = id,
            AggregateId = variant == "wrong-source" ? Guid.CreateVersion7() : id,
            AggregateType = nameof(AdmissionRecoveryRequestIntent),
            EventType = "AdmissionRecoveryRequestProcessingRequested",
            Payload = JsonSerializer.Serialize(new
            {
                TenantId = variant == "foreign" ? Guid.CreateVersion7() : tenant.Id,
                RequestIntentId = id
            }),
            CreatedAt = now,
            Status = OutboxMessageStatus.Pending,
            MaxRetries = 10
        };
        database.OutboxMessages.Add(message);
        await database.SaveChangesAsync();
        var claims = await seed.ServiceProvider.GetRequiredService<IGenericOutboxAdmission>()
            .ClaimDueAsync(1000, DateTime.UtcNow, CancellationToken.None);
        if (variant == "original")
        {
            var claim = claims.Single(row => row.MessageId == id);
            await Assert.That(claim.TenantId).IsEqualTo(tenant.Id);
            await using var execution = await seed.ServiceProvider.GetRequiredService<IGenericOutboxExecutionScopeFactory>()
                .CreateAsync(claim, CancellationToken.None);
            await Assert.That(execution.Services.GetRequiredService<ITenantContextAccessor>().TenantId).IsEqualTo(tenant.Id);
            await Assert.That(execution.Services.GetRequiredService<ExploreDbContext>().TenantFilterTenantId).IsEqualTo(tenant.Id);
            await CreateProcessor(hosted.Services).ProcessGenericClaimAsync(claim, CancellationToken.None);
        }
        else
            await Assert.That(claims.Any(row => row.MessageId == id)).IsFalse();
        var stored = await database.OutboxMessages.AsNoTracking().SingleAsync(row => row.Id == id);
        await Assert.That(stored.Status).IsEqualTo(
            variant == "original" ? OutboxMessageStatus.Completed : OutboxMessageStatus.Failed);
        await using var verify = await seed.ServiceProvider.GetRequiredService<ITenantExploreDbContextFactory>()
            .CreateDbContextAsync(tenant.Id);
        var retained = await verify.AdmissionRecoveryRequestIntents.AsNoTracking().SingleAsync(row => row.Id == id);
        await Assert.That(retained.ProcessedAt).IsEqualTo(now);
        await Assert.That(retained.ProtectedIdentity).IsEqualTo(string.Empty);
        await Assert.That(await verify.AdmissionRecoveryDeliveryIntents.AnyAsync()).IsFalse();
    }

    [Test]
    [Arguments("original")]
    [Arguments("current-case-version")]
    [Arguments("outbox-id")]
    [Arguments("envelope")]
    [Arguments("native-report")]
    [Arguments("tenant")]
    [Arguments("event")]
    [Arguments("case")]
    [Arguments("retained-version")]
    [Arguments("retained-time")]
    [Arguments("message-time")]
    [Arguments("digest")]
    [Arguments("lowercase-digest")]
    [Arguments("payload")]
    public async Task PortableReportAdmission_BindsRetainedSubmissionRatherThanCurrentCase(string variant)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using var hosted = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(GenericOutboxWorkerTestServices.Configure));
        using var client = hosted.CreateClient();
        await using var scope = hosted.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var publisher = await TenantScenarioSeed.SeedActiveTenantWithOrganizationPublisherAsync(database);
        var tenant = new TenantBuilder().Build();
        database.Tenants.Add(tenant);
        var source = new EventBuilder().WithTenantId(tenant.Id).WithActorId(publisher.ActorId)
            .WithStatus(EventStatusEnum.Published).Build();
        database.Events.Add(source);
        var report = EventReport.Create(tenant.Id, source.Id, publisher.UserId, null,
            EventReporterKind.AuthenticatedUser, EventReportSourceKind.UserReport, "spam", null,
            EventReportPriority.Normal, null, false, false, "en", null, null, DateTime.UtcNow);
        var reportCase = EventReportCase.Create(tenant.Id, report.Id, "safety", EventReportPriority.Normal, null, report.CreatedAt);
        var message = EventReportOutboxMessageFactory.CreateProviderSyncRequestedMessage(report, reportCase, null);
        Guid originalVersion = reportCase.ConcurrencyStamp;
        string originalDigest = report.ProviderSyncPayloadDigest!;
        database.AddRange(report, reportCase);
        var fields = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(message.Payload!)!;
        switch (variant)
        {
            case "current-case-version": reportCase.ConcurrencyStamp = Guid.CreateVersion7(); break;
            case "outbox-id": message.Id = Guid.CreateVersion7(); break;
            case "envelope": message.AggregateType = "Event"; break;
            case "native-report": message.AggregateId = Guid.CreateVersion7(); break;
            case "tenant": fields["TenantId"] = JsonSerializer.SerializeToElement(Guid.CreateVersion7()); break;
            case "event": fields["EventId"] = JsonSerializer.SerializeToElement(Guid.CreateVersion7()); break;
            case "case": fields["CaseId"] = JsonSerializer.SerializeToElement(Guid.CreateVersion7()); break;
            case "retained-version":
                database.Entry(report).Property(row => row.ProviderSyncCaseConcurrencyStamp).CurrentValue = Guid.CreateVersion7();
                break;
            case "retained-time":
                database.Entry(report).Property(row => row.ProviderSyncSubmittedAtUtc).CurrentValue = report.CreatedAt.AddTicks(10);
                break;
            case "message-time": message.CreatedAt = message.CreatedAt.AddTicks(10); break;
            case "digest":
                database.Entry(report).Property(row => row.ProviderSyncPayloadDigest).CurrentValue = new string('0', 64);
                break;
            case "lowercase-digest":
                database.Entry(report).Property(row => row.ProviderSyncPayloadDigest).CurrentValue = new string('a', 64);
                break;
            case "payload": fields["CorrelationId"] = JsonSerializer.SerializeToElement("changed"); break;
        }
        if (variant is "tenant" or "event" or "case" or "payload")
            message.Payload = JsonSerializer.Serialize(fields);
        database.OutboxMessages.Add(message);
        await database.SaveChangesAsync();
        var admission = scope.ServiceProvider.GetRequiredService<IGenericOutboxAdmission>();
        var claims = await admission.ClaimDueAsync(1000, DateTime.UtcNow, CancellationToken.None);
        if (variant is "original" or "current-case-version")
        {
            var claim = claims.Single(row => row.MessageId == message.Id);
            await Assert.That(claim.TenantId).IsEqualTo(tenant.Id);
            await Assert.That(claim.SourceIdentity).IsEqualTo(
                $"{report.Id:D}:{source.Id:D}:{reportCase.Id:D}:{originalVersion:D}:{originalDigest}");
            await using var execution = await scope.ServiceProvider.GetRequiredService<IGenericOutboxExecutionScopeFactory>()
                .CreateAsync(claim, CancellationToken.None);
            var persisted = await execution.Services.GetRequiredService<IOutboxRepository>()
                .GetClaimedGenericAsync(claim.MessageId, claim.LeaseExpiresAt, claim.Status,
                    execution.Services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime);
            await execution.Services.GetRequiredService<IGenericOutboxExecutionAuthority>()
                .RequireAsync(persisted!, CancellationToken.None);
            await using var sourceContext = await scope.ServiceProvider.GetRequiredService<ITenantExploreDbContextFactory>()
                .CreateDbContextAsync(tenant.Id);
            await sourceContext.Set<EventReport>().Where(row => row.Id == report.Id).ExecuteUpdateAsync(setters =>
                setters.SetProperty(row => row.ProviderSyncCaseConcurrencyStamp, Guid.CreateVersion7()));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                execution.Services.GetRequiredService<IGenericOutboxExecutionAuthority>()
                    .RequireAsync(persisted!, CancellationToken.None));
        }
        else
        {
            await Assert.That(claims.Any(row => row.MessageId == message.Id)).IsFalse();
            var stored = await database.OutboxMessages.AsNoTracking().SingleAsync(row => row.Id == message.Id);
            await Assert.That(stored.Status).IsEqualTo(OutboxMessageStatus.Failed);
            await Assert.That(stored.LastError).IsEqualTo("generic_source_not_admitted");
        }
    }

    internal static OutboxProcessor CreateProcessor(IServiceProvider services) => new(
        services, Options.Create(new OutboxProcessorSettings
        {
            InitialRetryDelaySeconds = 1,
            MaxRetryDelaySeconds = 60
        }), NullLogger<OutboxProcessor>.Instance);
}

/// <summary>Uses production admission, worker, repositories and Runtime scope selection on the fixture database.</summary>
internal static class GenericOutboxWorkerTestServices
{
    internal static void Configure(IServiceCollection services)
    {
        services.RemoveAll<IGenericOutboxAdmissionDbContextFactory>();
        services.AddSingleton<IGenericOutboxAdmissionDbContextFactory, AdmissionFactory>();
        services.RemoveAll<ITenantExploreDbContextFactory>();
        services.AddSingleton<ITenantExploreDbContextFactory, TenantFactory>();
        services.RemoveAll<ExploreDbContext>();
        services.AddScoped(provider =>
        {
            if (GenericOutboxExecutionScopeFactory.GetSelectedRuntimeContext(provider) is { } selected)
                return selected;
            var context = provider.GetRequiredService<IDbContextFactory<ExploreDbContext>>().CreateDbContext();
            context.TenantContext = provider.GetService<ITenantContext>();
            context.CurrentUserService = provider.GetService<ICurrentUserService>();
            return context;
        });
    }

    private sealed class AdmissionFactory(IDbContextFactory<ExploreDbContext> factory)
        : IGenericOutboxAdmissionDbContextFactory
    {
        public Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            factory.CreateDbContextAsync(cancellationToken);
    }

    private sealed class TenantFactory(IDbContextFactory<ExploreDbContext> factory) : ITenantExploreDbContextFactory
    {
        public async Task<ExploreDbContext> CreateDbContextAsync(Guid tenantId, CancellationToken cancellationToken = default)
        {
            var context = await factory.CreateDbContextAsync(cancellationToken);
            context.TenantContext = new SelectedTenant(tenantId);
            return context;
        }
    }

    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;
}
