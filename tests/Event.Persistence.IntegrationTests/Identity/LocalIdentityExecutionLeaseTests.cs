using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Identity;

public sealed class LocalIdentityExecutionLeaseTests
{
    [Test]
    public async Task MissingSelectionCannotAcquireIdentityWriterCustody()
    {
        await using var context = Create();
        await Assert.That(() => new LocalIdentityExecutionLease().RequireCurrent(context, context))
            .Throws<UnauthorizedAccessException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SelectedPairRemainsOriginalAcrossItsTransactionSettlement(bool external)
    {
        await using var application = Create();
        await using var other = Create();
        DbContext identity = external ? other : application;
        var owner = new LocalIdentityExecutionLease();
        owner.Bind(application, identity);
        await Assert.That(owner.Application).IsSameReferenceAs(application);
        await Assert.That(owner.Identity).IsSameReferenceAs(identity);
        owner.RequireCurrent(application, identity);
        await using (var transaction = await identity.Database.BeginTransactionAsync())
        {
            owner.RequireCurrent(application, identity);
            await transaction.CommitAsync();
        }
        owner.RequireCurrent(application, identity);
        await Assert.That(() => owner.Bind(application, identity)).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ACallerCannotReplaceEitherSelectedContext(bool replaceIdentity)
    {
        await using var application = Create();
        await using var identity = Create();
        await using var replacement = Create();
        var owner = new LocalIdentityExecutionLease();
        owner.Bind(application, identity);
        await Assert.That(() => owner.RequireCurrent(replaceIdentity ? application : replacement,
            replaceIdentity ? replacement : identity)).Throws<UnauthorizedAccessException>();
        owner.RequireCurrent(application, identity);
    }

    [Test]
    public async Task SelectionAfterBeginFailsWithoutAdoptingTheCallersTransaction()
    {
        await using var context = Create();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var owner = new LocalIdentityExecutionLease();
        await Assert.That(() => owner.Bind(context, context)).Throws<InvalidOperationException>();
        await Assert.That(owner.Application).IsNull();
        await Assert.That(owner.Identity).IsNull();
        await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
    }

    [Test]
    public async Task MutatingSelectedTenantCannotReuseTheOriginalWriterPair()
    {
        var selected = new Tenant(Guid.CreateVersion7());
        await using var context = Create(selected);
        var owner = new LocalIdentityExecutionLease();
        owner.Bind(context, context);
        selected.TenantId = Guid.CreateVersion7();
        await Assert.That(() => owner.RequireCurrent(context, context)).Throws<InvalidOperationException>();
    }

    private static ExploreDbContext Create(ITenantContext? tenant = null)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(SqliteTestDatabaseFactory.CreateIsolatedMemoryConnectionString())
            .AddInterceptors(SqliteNamedLockTransactionInterceptor.Instance).Options;
        var context = new ExploreDbContext(options) { TenantContext = tenant };
        context.BindTenantLease();
        return context;
    }

    private sealed class Tenant(Guid id) : ITenantContext
    {
        public Guid TenantId { get; set; } = id;
    }
}
