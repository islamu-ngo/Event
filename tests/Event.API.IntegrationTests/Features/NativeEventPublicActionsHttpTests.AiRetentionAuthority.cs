using System.Data.Common;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.AiAssistant.Handlers.Commands;
using Explore.Application.Features.AiAssistant.Requests.Commands;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using Explore.Domain;
using Explore.Domain.Ai;
using Explore.Domain.Constants;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventPublicActionsHttpTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AiRetentionOwnsOneExactTenantTransactionAndPreservesDryRun(bool dryRun)
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OwnerId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        DateTime now = DateTime.UtcNow;
        Guid owned = await SeedRetentionConversation(db, db.TenantContext!.TenantId, data.OwnerId, now.AddDays(-45));
        Guid foreign;
        await using (var other = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<ExploreDbContext>>().CreateDbContextAsync())
        {
            other.TenantContext = new AuthorityTenant(data.ForeignTenantId);
            Guid foreignOwner = await other.Events.Where(row => row.Id == data.ForeignId)
                .Select(row => row.Actor.UserId!.Value).SingleAsync();
            foreign = await SeedRetentionConversation(other, data.ForeignTenantId, foreignOwner, now.AddDays(-45));
        }

        var result = await ActivatorUtilities.CreateInstance<RunAiRetentionCleanupCommandHandler>(scope.ServiceProvider)
            .ExecuteAsync(new() { UtcNow = now, DryRun = dryRun }, default);

        await Assert.That(result.EligibleConversations).IsEqualTo(1);
        await Assert.That(result.RedactedConversations).IsEqualTo(dryRun ? 0 : 1);
        var stored = await db.AiConversations.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.Id == owned);
        await Assert.That(stored.IsDeleted).IsEqualTo(!dryRun);
        await using var verify = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<ExploreDbContext>>().CreateDbContextAsync();
        await Assert.That((await verify.AiConversations.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.Id == foreign)).IsDeleted).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AiRetentionReadsPersistedPolicyAndHonorsInstanceLock(bool locked)
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OwnerId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        DateTime now = DateTime.UtcNow;
        Guid tenantId = db.TenantContext!.TenantId;
        Guid id = await SeedRetentionConversation(db, tenantId, data.OwnerId, now.AddDays(-10));
        _ = await scope.ServiceProvider.GetRequiredService<IHierarchicalSettingsResolver>()
            .ResolveGroupAsync<AiAssistantSettingGroup>(new SettingContext(TenantId: tenantId), default);
        string key = GovernanceSettingKeys.AiAssistant.RetentionDays;
        var system = await db.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == key);
        if (system is null)
            db.SystemSettings.Add(new SystemSetting
            {
                Id = Guid.CreateVersion7(),
                SettingKey = key,
                Value = "30",
                ValueType = SettingValueType.Integer,
                IsLocked = locked
            });
        else
        {
            system.Value = "30";
            system.IsLocked = locked;
        }
        db.TenantSettingOverrides.Add(new TenantSetting
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = null!,
            SettingKey = key,
            Value = "1"
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await ActivatorUtilities.CreateInstance<RunAiRetentionCleanupCommandHandler>(scope.ServiceProvider)
            .ExecuteAsync(new() { UtcNow = now }, default);

        await Assert.That(result.RetentionDays).IsEqualTo(locked ? 30 : 1);
        await Assert.That((await db.AiConversations.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.Id == id)).IsDeleted)
            .IsEqualTo(!locked);
    }

    [Test]
    public async Task AiRetentionRollsBackAlreadyRedactedMessagesWhenALaterStepFails()
    {
        var boundary = new FailAfterAiMessageRedaction();
        await using var factory = new NativeEventSeriesFactory { DatabaseInterceptor = boundary };
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OwnerId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        DateTime now = DateTime.UtcNow;
        Guid id = await SeedRetentionConversation(db, db.TenantContext!.TenantId, data.OwnerId, now.AddDays(-45));
        boundary.Arm(db);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            ActivatorUtilities.CreateInstance<RunAiRetentionCleanupCommandHandler>(scope.ServiceProvider)
                .ExecuteAsync(new() { UtcNow = now }, default));

        await Assert.That(boundary.Applied).IsTrue();
        await Assert.That((await db.AiMessages.AsNoTracking().SingleAsync(row => row.ConversationId == id)).Content).IsEqualTo("Retained transcript");
        await Assert.That((await db.AiConversations.AsNoTracking().SingleAsync(row => row.Id == id)).IsDeleted).IsFalse();
    }

    [Test]
    public async Task AiRetentionRawMutationRequiresItsCallerOwnedTransaction()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OwnerId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        DateTime now = DateTime.UtcNow;
        Guid id = await SeedRetentionConversation(db, db.TenantContext!.TenantId, data.OwnerId, now.AddDays(-45));

        await Assert.ThrowsAsync<InvalidOperationException>(() => new AiConversationRepository(db)
            .RedactExpiredConversationsAsync(now.AddDays(-30), 30, now, false, default));

        await Assert.That((await db.AiMessages.AsNoTracking().SingleAsync(row => row.ConversationId == id)).Content).IsEqualTo("Retained transcript");
    }

    [Test]
    public async Task ScheduledAiRetentionServiceUsesTheNativeTenantTransactionOwner()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        DateTime now = DateTime.UtcNow;
        using (var scope = Scope(factory, data.OwnerId))
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            await SeedRetentionConversation(db, db.TenantContext!.TenantId, data.OwnerId, now.AddDays(-45));
        }

        var result = await factory.Services.GetRequiredService<IAiRetentionCleanupService>().CleanupAllTenantsAsync(now);

        await Assert.That(result.FailedTenantCount).IsEqualTo(0);
        await Assert.That(result.EligibleConversations).IsEqualTo(1);
    }

    private static async Task<Guid> SeedRetentionConversation(ExploreDbContext db, Guid tenantId, Guid userId, DateTime touched)
    {
        var conversation = new AiConversation
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            UserId = userId,
            Title = "Retention candidate",
            CreatedAt = touched,
            UpdatedAt = touched,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        conversation.AddMessage(AiMessageRole.User, "Retained transcript", userId, touched);
        db.AiConversations.Add(conversation);
        await db.SaveChangesAsync();
        await db.AiConversations.Where(row => row.Id == conversation.Id).ExecuteUpdateAsync(setters =>
            setters.SetProperty(row => row.CreatedAt, touched).SetProperty(row => row.UpdatedAt, (DateTime?)touched));
        db.ChangeTracker.Clear();
        return conversation.Id;
    }

    private sealed class FailAfterAiMessageRedaction : DbCommandInterceptor
    {
        private ExploreDbContext? _target;
        private string? _table;
        public bool Applied { get; private set; }
        public void Arm(ExploreDbContext context)
        {
            _target = context;
            _table = context.Model.FindEntityType(typeof(AiMessage))!.GetTableName();
        }
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (ReferenceEquals(eventData.Context, _target) && _table is not null
                && command.CommandText.StartsWith("UPDATE", StringComparison.Ordinal)
                && command.CommandText.Contains(_table, StringComparison.Ordinal) && result > 0)
            {
                Applied = true;
                throw new OperationCanceledException("Injected retention graph failure.", cancellationToken);
            }
            return ValueTask.FromResult(result);
        }
    }
}
