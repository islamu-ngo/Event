using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Infrastructure.Recovery;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Secrets.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests;

internal static class TicketingRecoveryHealthTestData
{
    internal static readonly DateTime ObservedAt = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    internal static ServiceProvider Services(
        IGlobalExploreDbContextFactory factory, ExploreDbContext? request = null, bool enabled = true)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(factory);
        services.AddSingleton<TimeProvider>(new FixedClock());
        services.Configure<TicketingRecoveryOperatorOptions>(options => options.Enabled = enabled);
        services.AddTicketingRecoveryHealthPersistence();
        if (request is not null)
            services.AddScoped(_ => request);
        services.AddHealthChecks().AddCheck<TicketingRecoveryHealthCheck>(
            TicketingRecoveryHealthCheck.Name, tags: ["ready", "recovery"]);
        return services.BuildServiceProvider();
    }

    internal static async Task SeedAsync(ExploreDbContext context, Guid tenantA, Guid tenantB, bool unknown)
    {
        foreach (Guid tenant in new[] { tenantA, tenantB })
        {
            context.Tenants.Add(new Tenant
            {
                Id = tenant,
                FullName = "Recovery observation",
                Slug = $"recovery-health-{tenant:N}",
                TenantStatusId = 1,
                TenantStatus = null!,
                CreatedAt = ObservedAt
            });
            var manifest = TicketingRecoveryManifest.Create(
                Guid.CreateVersion7(), tenant, "release", "schema", 1, ObservedAt,
                1, 1, 1, 1, 1, 1, 1, new string('a', 64));
            var checkpoint = TicketingRecoveryCheckpoint.Begin(manifest, ObservedAt);
            if (tenant == tenantB)
                checkpoint.Fail("RECOVERY_INCOMPLETE", ObservedAt);
            context.TicketingRecoveryCheckpoints.Add(checkpoint);
            context.TicketingRecoveryReissueIntents.Add(TicketingRecoveryReissueIntent.Create(
                tenant, manifest.OperationId, Guid.CreateVersion7(), 2, ObservedAt));
        }
        await context.SaveChangesAsync();

        // Persist observation fixtures, not substitute repository results. Parent FKs
        // are disabled only by setup; CHECK constraints remain real and enforced.
        await InsertEffectAsync(context, tenantA, FairReturnOrchestrationEffectStatus.Pending, ObservedAt.AddSeconds(-30), null);
        await InsertEffectAsync(context, tenantB, FairReturnOrchestrationEffectStatus.Pending, ObservedAt.AddSeconds(30), null);
        await InsertEffectAsync(context, tenantB, FairReturnOrchestrationEffectStatus.DeadLettered, ObservedAt.AddDays(-2), "POISON_PROVIDER");
        await InsertEffectAsync(context, tenantB, FairReturnOrchestrationEffectStatus.DeadLettered, ObservedAt.AddDays(-3), "POISONX_PROVIDER");
        await InsertEffectAsync(context, tenantB, FairReturnOrchestrationEffectStatus.Completed, ObservedAt.AddDays(-4), null);
        if (unknown)
            await InsertEffectAsync(context, tenantB, FairReturnOrchestrationEffectStatus.Unknown, ObservedAt.AddSeconds(-90), null);
    }

    private static Task<int> InsertEffectAsync(
        ExploreDbContext context, Guid tenant, FairReturnOrchestrationEffectStatus status, DateTime due, string? failure)
    {
        string table = Table<FairReturnOrchestrationEffect>(context);
        DateTime? completed = status == FairReturnOrchestrationEffectStatus.Completed ? ObservedAt : null;
        DateTime? deadLettered = status == FairReturnOrchestrationEffectStatus.DeadLettered ? ObservedAt : null;
        return context.Database.ExecuteSqlRawAsync($$"""
            INSERT INTO {{table}}
                (id, tenant_id, waitlist_payment_intent_id, stable_operation_id, stable_cursor,
                 status_id, next_attempt_at, processing_fence, attempt_count, maximum_attempts,
                 last_failure_code, completed_at, dead_lettered_at, concurrency_stamp, created_at)
            VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, 1, 1, 5,
                    {7}, {8}, {9}, {10}, {11})
            """,
            [Guid.CreateVersion7(), tenant, Guid.CreateVersion7(), Guid.CreateVersion7(), due.Ticks,
                (int)status, due, (object?)failure ?? DBNull.Value, (object?)completed ?? DBNull.Value,
                (object?)deadLettered ?? DBNull.Value, Guid.CreateVersion7(), due]);
    }

    internal static string Table<T>(ExploreDbContext context) where T : class
    {
        var entity = context.Model.FindEntityType(typeof(T))!;
        string? schema = context.Database.IsSqlite() ? null : context.GetService<IDbContextOptions>()
            .FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema ?? entity.GetSchema();
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, schema);
    }

    internal sealed record TenantIdentity(Guid TenantId) : ITenantContext;

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(ObservedAt);
    }
}
