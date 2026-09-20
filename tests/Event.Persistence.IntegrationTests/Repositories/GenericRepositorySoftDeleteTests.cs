using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed class GenericRepositorySoftDeleteTests
{
    [Test]
    [Arguments(CascadeTiming.Immediate)]
    [Arguments(CascadeTiming.OnSaveChanges)]
    [Arguments(CascadeTiming.Never)]
    public async Task TrackedGroupDeletion_PreservesRequiredDependentsAndAuditOwnership(CascadeTiming timing)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseTestInMemoryDatabase(Guid.CreateVersion7().ToString()).Options;
        var tenantId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        Guid groupId;
        Guid participationId;
        await using (var context = new ExploreDbContext(options))
        {
            context.TenantContext = new TenantContext(tenantId);
            context.CurrentUserService = new CurrentUser(userId);
            var group = new Group { FullName = "Soft deletion" };
            var participation = new GroupTenant
            {
                Group = group,
                GroupId = group.Id,
                TenantId = tenantId,
                Tenant = null!,
                ApprovalStatusId = (int)ApprovalStatusEnum.Pending,
                ApprovalStatus = null!
            };
            context.GroupTenants.Add(participation);
            await context.SaveChangesAsync();
            groupId = group.Id;
            participationId = participation.Id;
            context.ChangeTracker.CascadeDeleteTiming = timing;

            await new GenericRepository<Group, Guid>(context).Delete(group);

            await Assert.That(context.ChangeTracker.CascadeDeleteTiming).IsEqualTo(timing);
            await Assert.That(group.IsDeleted).IsTrue();
            await Assert.That(group.DeletedBy).IsEqualTo((Guid?)userId);
            await Assert.That(group.DeletedAt).IsNotNull();
            await Assert.That(group.UpdatedBy).IsEqualTo((Guid?)userId);
            await Assert.That(group.UpdatedAt).IsEqualTo(group.DeletedAt);
            await Assert.That(participation.IsDeleted).IsFalse();
            await Assert.That(participation.GroupId).IsEqualTo(groupId);
        }
        await using var verification = new ExploreDbContext(options);
        verification.TenantContext = new TenantContext(tenantId);
        await Assert.That(await verification.Groups.AnyAsync(group => group.Id == groupId)).IsFalse();
        var retained = await verification.Groups.IgnoreQueryFilters(["SoftDelete"])
            .SingleAsync(group => group.Id == groupId);
        await Assert.That(retained.IsDeleted).IsTrue();
        await Assert.That(retained.DeletedBy).IsEqualTo((Guid?)userId);
        var dependent = await verification.GroupTenants.IgnoreQueryFilters(["SoftDelete"])
            .SingleAsync(item => item.Id == participationId);
        await Assert.That(dependent.IsDeleted).IsFalse();
        await Assert.That(dependent.GroupId).IsEqualTo(groupId);
        await Assert.That(dependent.TenantId).IsEqualTo(tenantId);
    }

    [Test]
    public async Task FailedSoftDeletion_RestoresIncomingTimingAndDoesNotPersistTheChange()
    {
        var failure = new SaveFailure();
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseTestInMemoryDatabase(Guid.CreateVersion7().ToString())
            .AddInterceptors(failure).Options;
        Guid id;
        await using (var context = new ExploreDbContext(options))
        {
            var group = new Group { FullName = "Failed soft deletion" };
            context.Groups.Add(group);
            await context.SaveChangesAsync();
            id = group.Id;
            context.ChangeTracker.CascadeDeleteTiming = CascadeTiming.Never;
            failure.Enabled = true;

            await Assert.That(async () => await new GenericRepository<Group, Guid>(context).Delete(group))
                .Throws<InjectedSaveException>();
            await Assert.That(context.ChangeTracker.CascadeDeleteTiming).IsEqualTo(CascadeTiming.Never);
        }
        failure.Enabled = false;
        await using var verification = new ExploreDbContext(options);
        await Assert.That((await verification.Groups.SingleAsync(group => group.Id == id)).IsDeleted).IsFalse();
    }

    [Test]
    public async Task NonSoftRelationshipDeletion_RemovesTheRowWithoutChangingCascadeTiming()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseTestInMemoryDatabase(Guid.CreateVersion7().ToString()).Options;
        await using var context = new ExploreDbContext(options);
        var tenantId = Guid.CreateVersion7();
        context.TenantContext = new TenantContext(tenantId);
        var link = new TagTypeTags
        {
            TagId = Guid.CreateVersion7(),
            Tag = null!,
            TagTypeId = 1,
            TagType = null!,
            TenantId = tenantId,
            Tenant = null!
        };
        context.TagTypeTags.Add(link);
        await context.SaveChangesAsync();
        context.ChangeTracker.CascadeDeleteTiming = CascadeTiming.Never;

        await new GenericRepository<TagTypeTags, Guid>(context).Delete(link);

        await Assert.That(await context.TagTypeTags.AnyAsync(item => item.Id == link.Id)).IsFalse();
        await Assert.That(context.ChangeTracker.CascadeDeleteTiming).IsEqualTo(CascadeTiming.Never);
    }

    private sealed record TenantContext(Guid TenantId) : ITenantContext;

    private sealed record CurrentUser(Guid? UserId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
    }

    private sealed class InjectedSaveException : Exception;

    private sealed class SaveFailure : SaveChangesInterceptor
    {
        public bool Enabled { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Enabled)
            {
                throw new InjectedSaveException();
            }
            return ValueTask.FromResult(result);
        }
    }
}
