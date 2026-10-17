using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Identity;
using Explore.Application.Exceptions;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Footer.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Definitions;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventPublicActionsHttpTests
{
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task FooterGroupDeletionRequiresCurrentHumanAdministration(int authority)
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OutsiderId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await GrantFooterAdministrator(db, data.OutsiderId, authority);
        var group = await SeedFooterGroup(db, db.TenantContext!.TenantId);
        var unrelatedGroup = await SeedFooterGroup(db, db.TenantContext.TenantId);
        var unrelatedLink = await db.TenantFooterLinks.SingleAsync(row => row.FooterLinkGroupId == unrelatedGroup.Id);
        await scope.ServiceProvider.GetRequiredService<Explore.Application.Contracts.Operations.IHumanCommitScopePreparation>()
            .PrepareAsync(default);
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<DeleteFooterLinkGroupCommand, bool>>();
        var command = new DeleteFooterLinkGroupCommand { GroupId = group.Id, TenantId = db.TenantContext!.TenantId };

        if (authority == 0)
            await Assert.ThrowsAsync<AuthorizationException>(() => handler.ExecuteAsync(command, default));
        else
            await Assert.That(await handler.ExecuteAsync(command, default)).IsTrue();

        await Assert.That(await db.TenantFooterLinkGroups.AsNoTracking().AnyAsync(row => row.Id == group.Id))
            .IsEqualTo(authority == 0);
        await Assert.That(await db.TenantFooterLinks.AsNoTracking().AnyAsync(row => row.FooterLinkGroupId == group.Id))
            .IsEqualTo(authority == 0);
        await Assert.That(db.Entry(unrelatedLink).State).IsEqualTo(EntityState.Unchanged);
        await Assert.That(await db.TenantFooterLinks.AsNoTracking().AnyAsync(row => row.Id == unrelatedLink.Id
            && row.FooterLinkGroupId == unrelatedGroup.Id)).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FooterGroupDeletionCannotTargetForeignOrInstanceGroups(bool instance)
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OutsiderId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await GrantFooterAdministrator(db, data.OutsiderId, 1);
        var group = await SeedFooterGroup(db, db.TenantContext!.TenantId);
        Guid? targetTenant = instance ? null : data.ForeignTenantId;
        // The seed context changes stored ownership without impersonating an administrator.
        await db.TenantFooterLinkGroups.Where(row => row.Id == group.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.TenantId, targetTenant));
        db.ChangeTracker.Clear();

        await scope.ServiceProvider.GetRequiredService<Explore.Application.Contracts.Operations.IHumanCommitScopePreparation>()
            .PrepareAsync(default);
        await Assert.ThrowsAsync<NotFoundException>(() =>
            scope.ServiceProvider.GetRequiredService<ICommandHandler<DeleteFooterLinkGroupCommand, bool>>()
                .ExecuteAsync(new() { GroupId = group.Id, TenantId = db.TenantContext!.TenantId }, default));

        await Assert.That(await db.TenantFooterLinks.IgnoreQueryFilters()
            .AnyAsync(row => row.FooterLinkGroupId == group.Id)).IsTrue();
    }

    [Test]
    public async Task FooterGroupDeletionRejectsTrackedOwnershipSubstitution()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OutsiderId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await GrantFooterAdministrator(db, data.OutsiderId, 1);
        var group = await SeedFooterGroup(db, db.TenantContext!.TenantId);
        group.TenantId = Guid.CreateVersion7();
        await scope.ServiceProvider.GetRequiredService<Explore.Application.Contracts.Operations.IHumanCommitScopePreparation>()
            .PrepareAsync(default);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<ICommandHandler<DeleteFooterLinkGroupCommand, bool>>()
                .ExecuteAsync(new() { GroupId = group.Id, TenantId = db.TenantContext!.TenantId }, default));

        await Assert.That(await db.TenantFooterLinkGroups.AsNoTracking().AnyAsync(row => row.Id == group.Id
            && row.TenantId == db.TenantContext!.TenantId)).IsTrue();
        await Assert.That(await db.TenantFooterLinks.AsNoTracking().AnyAsync(row => row.FooterLinkGroupId == group.Id)).IsTrue();
    }

    [Test]
    public async Task FooterGroupDeletionRejectsTrackedChildParentSubstitution()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OutsiderId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await GrantFooterAdministrator(db, data.OutsiderId, 1);
        var group = await SeedFooterGroup(db, db.TenantContext!.TenantId);
        var other = await SeedFooterGroup(db, db.TenantContext.TenantId);
        var link = await db.TenantFooterLinks.SingleAsync(row => row.FooterLinkGroupId == group.Id);
        link.FooterLinkGroupId = other.Id;
        await scope.ServiceProvider.GetRequiredService<Explore.Application.Contracts.Operations.IHumanCommitScopePreparation>()
            .PrepareAsync(default);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<ICommandHandler<DeleteFooterLinkGroupCommand, bool>>()
                .ExecuteAsync(new() { GroupId = group.Id, TenantId = db.TenantContext!.TenantId }, default));

        await Assert.That(await db.TenantFooterLinkGroups.AsNoTracking().AnyAsync(row => row.Id == group.Id)).IsTrue();
        await Assert.That(await db.TenantFooterLinks.AsNoTracking()
            .AnyAsync(row => row.Id == link.Id && row.FooterLinkGroupId == group.Id)).IsTrue();
        await Assert.That(await db.TenantFooterLinkGroups.AsNoTracking().AnyAsync(row => row.Id == other.Id)).IsTrue();
    }

    [Test]
    public async Task FooterGroupDeletionRechecksAdministrationAfterPriorResolution()
    {
        var boundary = new BeforeAuthorityTransaction();
        await using var factory = new NativeEventSeriesFactory { DatabaseInterceptor = boundary };
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OutsiderId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await GrantFooterAdministrator(db, data.OutsiderId, 1);
        var group = await SeedFooterGroup(db, db.TenantContext!.TenantId);
        await Assert.That(await scope.ServiceProvider.GetRequiredService<IAdminContext>().IsInstanceAdminAsync()).IsTrue();
        boundary.Arm(db, async token =>
        {
            await using var changed = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<ExploreDbContext>>()
                .CreateDbContextAsync(token);
            changed.TenantContext = new AuthorityTenant(db.TenantContext!.TenantId);
            await changed.PlatformUserRoles.Where(row => row.UserId == data.OutsiderId).ExecuteDeleteAsync(token);
        });
        await scope.ServiceProvider.GetRequiredService<Explore.Application.Contracts.Operations.IHumanCommitScopePreparation>()
            .PrepareAsync(default);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            scope.ServiceProvider.GetRequiredService<ICommandHandler<DeleteFooterLinkGroupCommand, bool>>()
                .ExecuteAsync(new() { GroupId = group.Id, TenantId = db.TenantContext!.TenantId }, default));

        await Assert.That(await db.TenantFooterLinks.AsNoTracking().AnyAsync(row => row.FooterLinkGroupId == group.Id)).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FooterGroupDeletionUsesCurrentGovernanceAndDeploymentMode(bool singleTenant)
    {
        await using var factory = new NativeEventSeriesFactory();
        factory.AdditionalConfiguration["Testing:DisableDeploymentModeCache"] = "true";
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OutsiderId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await GrantFooterAdministrator(db, data.OutsiderId, 1);
        var group = await SeedFooterGroup(db, db.TenantContext!.TenantId);
        string key = FooterSettingDefinitions.LockTenantLinkGroups.Key;
        var setting = await db.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == key);
        if (setting is null)
            db.SystemSettings.Add(new SystemSetting
            {
                Id = Guid.CreateVersion7(),
                SettingKey = key,
                Value = "true",
                ValueType = SettingValueType.Boolean
            });
        else
            setting.Value = "true";
        if (!singleTenant)
        {
            var bootstrap = InstanceBootstrapState.CreateInteractivePending(
                Guid.CreateVersion7(), DeploymentMode.MultiTenant, DateTime.UtcNow);
            bootstrap.CompleteInteractive(data.OutsiderId, DateTime.UtcNow);
            db.Set<InstanceBootstrapState>().Add(bootstrap);
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await scope.ServiceProvider.GetRequiredService<Explore.Application.Contracts.Operations.IHumanCommitScopePreparation>()
            .PrepareAsync(default);
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<DeleteFooterLinkGroupCommand, bool>>();

        if (singleTenant)
            await Assert.That(await handler.ExecuteAsync(new() { GroupId = group.Id, TenantId = db.TenantContext!.TenantId }, default)).IsTrue();
        else
            await Assert.ThrowsAsync<AuthorizationException>(() => handler.ExecuteAsync(new() { GroupId = group.Id, TenantId = db.TenantContext!.TenantId }, default));

        await Assert.That(await db.TenantFooterLinks.AsNoTracking().AnyAsync(row => row.FooterLinkGroupId == group.Id))
            .IsEqualTo(!singleTenant);
    }

    [Test]
    public async Task FooterGroupDeletionRollsBackLinksWhenParentDeletionFails()
    {
        var boundary = new RejectFooterParentDeletion();
        await using var factory = new NativeEventSeriesFactory { DatabaseInterceptor = boundary };
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OutsiderId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await GrantFooterAdministrator(db, data.OutsiderId, 1);
        var group = await SeedFooterGroup(db, db.TenantContext!.TenantId);
        boundary.GroupId = group.Id;
        await scope.ServiceProvider.GetRequiredService<Explore.Application.Contracts.Operations.IHumanCommitScopePreparation>()
            .PrepareAsync(default);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            scope.ServiceProvider.GetRequiredService<ICommandHandler<DeleteFooterLinkGroupCommand, bool>>()
                .ExecuteAsync(new() { GroupId = group.Id, TenantId = db.TenantContext!.TenantId }, default));

        await Assert.That(boundary.ParentDeletionAttempted).IsTrue();
        await Assert.That(await db.TenantFooterLinkGroups.AsNoTracking().AnyAsync(row => row.Id == group.Id)).IsTrue();
        await Assert.That(await db.TenantFooterLinks.AsNoTracking().AnyAsync(row => row.FooterLinkGroupId == group.Id)).IsTrue();
    }

    [Test]
    public async Task RawFooterLinkDeletionRequiresTheOwningSerializableTransaction()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OwnerId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var group = await SeedFooterGroup(db, db.TenantContext!.TenantId);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new FooterLinkRepository(db).DeleteByGroupIdAsync(group.Id));

        await Assert.That(await db.TenantFooterLinks.AsNoTracking().AnyAsync(row => row.FooterLinkGroupId == group.Id)).IsTrue();
    }

    private static async Task GrantFooterAdministrator(ExploreDbContext db, Guid userId, int authority)
    {
        if (authority == 1)
            await GrantTemplateAdministrator(db, userId);
        else if (authority == 2)
        {
            var member = await db.TenantUsers.SingleAsync(row => row.UserId == userId);
            db.TenantUserRoleGrants.Add(new TenantUserRoleGrant
            {
                Id = Guid.CreateVersion7(),
                TenantId = member.TenantId,
                Tenant = null!,
                TenantUserId = member.Id,
                TenantUser = member,
                RoleId = (int)RoleEnum.TenantAdmin,
                Role = null!,
                RoleScopeId = (int)RoleScopeEnum.Tenant,
                GrantedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
    }

    private static async Task<TenantFooterLinkGroup> SeedFooterGroup(ExploreDbContext db, Guid tenantId)
    {
        var group = new TenantFooterLinkGroup
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Title = "Owned footer",
            Order = 1,
            IsActive = true
        };
        db.TenantFooterLinkGroups.Add(group);
        db.TenantFooterLinks.Add(new TenantFooterLink
        {
            Id = Guid.CreateVersion7(),
            FooterLinkGroupId = group.Id,
            Label = "Owned link",
            Url = "/owned",
            Order = 1,
            IsActive = true
        });
        await db.SaveChangesAsync();
        return group;
    }

    private sealed class RejectFooterParentDeletion : SaveChangesInterceptor
    {
        public Guid GroupId { get; set; }
        public bool ParentDeletionAttempted { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (GroupId != Guid.Empty && eventData.Context?.ChangeTracker.Entries<TenantFooterLinkGroup>()
                    .Any(entry => entry.Entity.Id == GroupId && entry.State == EntityState.Deleted) == true)
            {
                ParentDeletionAttempted = true;
                throw new OperationCanceledException("Injected parent deletion failure.", cancellationToken);
            }
            return ValueTask.FromResult(result);
        }
    }
}
