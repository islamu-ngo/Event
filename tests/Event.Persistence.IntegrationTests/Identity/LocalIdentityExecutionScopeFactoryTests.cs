using System.Data;
using Explore.Application.Contracts.Identity;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Identity;

[NotInParallel]
public sealed class LocalIdentityExecutionScopeFactoryTests
{
    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task HumanFreeEntrySelectsTheOriginalPairBeforeConstructingTheRealIdentityStore(
        IdentityDatabaseTopology topology)
    {
        await using var fixture = await LocalCredentialFirstUseTests.Fixture.CreateAsync(topology);
        await using var provider = await ComposeAsync(fixture, topology);
        await using var request = provider.CreateAsyncScope();
        var ordinary = request.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var ordinaryId = ordinary.ContextId;
        var factory = request.ServiceProvider.GetRequiredService<ILocalIdentityExecutionScopeFactory>();

        await using var selected = await factory.CreateAsync(fixture.CancellationToken);
        var pair = RequireSelected(selected.Services);
        await Assert.That(ReferenceEquals(selected.Services, request.ServiceProvider)).IsFalse();
        await Assert.That(ReferenceEquals(pair.Application, ordinary)).IsFalse();
        await Assert.That(ReferenceEquals(pair.Application, pair.Identity))
            .IsEqualTo(topology == IdentityDatabaseTopology.Colocated);
        await Assert.That(selected.Services.GetRequiredService<ExploreDbContextExecutionLease>().HumanScope).IsNull();
        await Assert.That(selected.Services.GetRequiredService<ExploreDbContextExecutionLease>().HumanLifetime).IsNull();
        await Assert.That(pair.Application.Database.CurrentTransaction).IsNull();
        await Assert.That(pair.Identity.Database.CurrentTransaction).IsNull();

        // This is the real EF Identity store selected by hosting, not a test replacement store.
        var manager = selected.Services.GetRequiredService<UserManager<LocalIdentityUser>>();
        var user = await manager.FindByIdAsync(fixture.Receipt.LocalSubjectId.ToString("D"));
        await Assert.That(user).IsNotNull();
        await Assert.That(user!.Id).IsEqualTo(fixture.Receipt.LocalSubjectId);
        var eligibility = await pair.Identity.Set<LocalIdentityEligibility>().AsNoTracking()
            .SingleAsync(row => row.LocalSubjectId == user.Id, fixture.CancellationToken);
        await Assert.That(eligibility.CurrentOperationId).IsEqualTo(fixture.Receipt.OperationId);
        RequireSelected(selected.Services);

        await Assert.That(ordinary.ContextId).IsEqualTo(ordinaryId);
        await Assert.That(ordinary.Database.GetDbConnection().State).IsEqualTo(ConnectionState.Closed);
        await Assert.That(ordinary.Database.CurrentTransaction).IsNull();
        await Assert.That(request.ServiceProvider.GetRequiredService<LocalIdentityExecutionLease>().Application).IsNull();
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task NestedSelectionBorrowsTheSameServicesAndCannotDisposeTheOuterOriginalTransaction(
        IdentityDatabaseTopology topology)
    {
        await using var fixture = await LocalCredentialFirstUseTests.Fixture.CreateAsync(topology);
        await using var provider = await ComposeAsync(fixture, topology);
        await using var request = provider.CreateAsyncScope();
        await using var outer = await request.ServiceProvider.GetRequiredService<ILocalIdentityExecutionScopeFactory>()
            .CreateAsync(fixture.CancellationToken);
        var pair = RequireSelected(outer.Services);
        await using var transaction = await pair.Identity.Database.BeginTransactionAsync(fixture.CancellationToken);
        var nested = await outer.Services.GetRequiredService<ILocalIdentityExecutionScopeFactory>()
            .CreateAsync(fixture.CancellationToken);
        await Assert.That(nested.Services).IsSameReferenceAs(outer.Services);
        await nested.DisposeAsync();
        await nested.DisposeAsync();

        RequireSelected(outer.Services);
        await Assert.That(pair.Identity.Database.CurrentTransaction).IsSameReferenceAs(transaction);
        await Assert.That(pair.Identity.Database.GetDbConnection().State).IsEqualTo(ConnectionState.Open);
        var user = await outer.Services.GetRequiredService<UserManager<LocalIdentityUser>>()
            .FindByIdAsync(fixture.Receipt.LocalSubjectId.ToString("D"));
        await Assert.That(user!.Id).IsEqualTo(fixture.Receipt.LocalSubjectId);
        await transaction.RollbackAsync(fixture.CancellationToken);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task DisposedChildCannotReturnAContextAgainAfterTheNextScopeHasRentedIt(
        IdentityDatabaseTopology topology)
    {
        await using var fixture = await LocalCredentialFirstUseTests.Fixture.CreateAsync(topology);
        await using var provider = await ComposeAsync(fixture, topology);
        await using var request = provider.CreateAsyncScope();
        var factory = request.ServiceProvider.GetRequiredService<ILocalIdentityExecutionScopeFactory>();
        var previous = await factory.CreateAsync(fixture.CancellationToken);
        var oldPair = RequireSelected(previous.Services);
        var oldId = oldPair.Application.ContextId;
        var oldIdentityId = oldPair.Identity.ContextId;
        // Resolving both services transfers their lifetime to the real child DI scope.
        _ = previous.Services.GetRequiredService<UserManager<LocalIdentityUser>>();
        await previous.DisposeAsync();

        await using var current = await factory.CreateAsync(fixture.CancellationToken);
        var pair = RequireSelected(current.Services);
        await Assert.That(pair.Application).IsSameReferenceAs(oldPair.Application);
        await Assert.That(pair.Identity).IsSameReferenceAs(oldPair.Identity);
        await Assert.That(pair.Application.ContextId == oldId).IsFalse();
        await Assert.That(pair.Identity.ContextId == oldIdentityId).IsFalse();
        await using var transaction = await pair.Identity.Database.BeginTransactionAsync(fixture.CancellationToken);
        await previous.DisposeAsync();

        RequireSelected(current.Services);
        await Assert.That(pair.Identity.Database.CurrentTransaction).IsSameReferenceAs(transaction);
        await Assert.That(pair.Identity.Database.GetDbConnection().State).IsEqualTo(ConnectionState.Open);
        var row = await pair.Identity.Set<LocalIdentityEligibility>().AsNoTracking()
            .SingleAsync(value => value.LocalSubjectId == fixture.Receipt.LocalSubjectId, fixture.CancellationToken);
        await Assert.That(row.ExternalLoginId).IsEqualTo(fixture.Receipt.ExternalLoginId);
        await transaction.RollbackAsync(fixture.CancellationToken);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task CancellationWhileTheRealLifecycleFenceIsHeldReleasesThePartialChildForTheNextAttempt(
        IdentityDatabaseTopology topology)
    {
        await using var fixture = await LocalCredentialFirstUseTests.Fixture.CreateAsync(topology);
        await using var provider = await ComposeAsync(fixture, topology);
        await using var holderScope = fixture.Provider.CreateAsyncScope();
        var holder = await LocalIdentityLifecycleGateCustody.EnterAsync(fixture.Identity(holderScope), fixture.CancellationToken);
        await using var request = provider.CreateAsyncScope();
        var factory = request.ServiceProvider.GetRequiredService<ILocalIdentityExecutionScopeFactory>();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(fixture.CancellationToken);
        Task<ILocalIdentityExecutionScope>? pending = null;
        try
        {
            pending = factory.CreateAsync(cancellation.Token).AsTask();
            await Assert.That(pending.IsCompleted).IsFalse();
            cancellation.Cancel();
            await Assert.That(async () => { await pending; }).Throws<OperationCanceledException>();
            await Assert.That(request.ServiceProvider.GetRequiredService<LocalIdentityExecutionLease>().Application).IsNull();
        }
        finally
        {
            cancellation.Cancel();
            if (pending is not null)
            {
                try { await using var unexpected = await pending; }
                catch (Exception) { /* Preserve the assertion while observing failed acquisition and cleanup. */ }
            }
            await holder.DisposeAsync();
        }

        await using var retry = await factory.CreateAsync(fixture.CancellationToken);
        var pair = RequireSelected(retry.Services);
        var row = await pair.Identity.Set<LocalIdentityEligibility>().AsNoTracking()
            .SingleAsync(value => value.LocalSubjectId == fixture.Receipt.LocalSubjectId, fixture.CancellationToken);
        await Assert.That(row.CurrentOperationId).IsEqualTo(fixture.Receipt.OperationId);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task FreshSelectionRefusesAHumanGateHeldByAnOtherwiseIdleOriginalInstanceOwner(
        IdentityDatabaseTopology topology)
    {
        await using var fixture = await LocalCredentialFirstUseTests.Fixture.CreateAsync(topology);
        await using var provider = await ComposeAsync(fixture, topology);
        await using var request = provider.CreateAsyncScope();
        var application = request.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var parent = request.ServiceProvider.GetRequiredService<ExploreDbContextExecutionLease>();
        parent.Bind(application, null);
        await using var human = new HumanSecuritySourceGate(application);
        await using var gate = await human.AcquireAsync(fixture.CancellationToken);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(fixture.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(10));
        await Assert.That(async () =>
        {
            await using var unexpected = await request.ServiceProvider
                .GetRequiredService<ILocalIdentityExecutionScopeFactory>().CreateAsync(cancellation.Token);
        }).Throws<InvalidOperationException>();
        parent.RequireOriginalOwner(application);
        HumanSecuritySourceGate.RequireCurrent(application);
        await Assert.That(application.Database.CurrentTransaction).IsNull();
        await Assert.That(request.ServiceProvider.GetRequiredService<LocalIdentityExecutionLease>().Application).IsNull();
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task FreshSelectionRefusesRetainedParentDiscoveryWithoutWaitingForCancellation(
        IdentityDatabaseTopology topology)
    {
        await using var fixture = await LocalCredentialFirstUseTests.Fixture.CreateAsync(topology);
        await using var provider = await ComposeAsync(fixture, topology);
        await using var request = provider.CreateAsyncScope();
        var application = request.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var parent = request.ServiceProvider.GetRequiredService<ExploreDbContextExecutionLease>();
        parent.Bind(application, null);
        var originalContextId = application.ContextId;
        var connector = application.Database.GetDbConnection();
        await using var discovery = new EventDiscoveryEnrollmentGate(application);
        await using var parentLease = await discovery.AcquireAsync(fixture.CancellationToken);
        await Assert.That(HumanSecuritySourceGate.TryRequireCurrent(application)).IsFalse();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(fixture.CancellationToken);
        Task<ILocalIdentityExecutionScope> pending = request.ServiceProvider
            .GetRequiredService<ILocalIdentityExecutionScopeFactory>().CreateAsync(cancellation.Token).AsTask();
        bool refusedBeforeCancellation;
        Exception? refusal = null;
        try
        {
            await Task.WhenAny(pending, Task.Delay(TimeSpan.FromMilliseconds(250), fixture.CancellationToken));
            refusedBeforeCancellation = pending.IsCompleted;
            if (refusedBeforeCancellation)
            {
                try { await using var unexpected = await pending; }
                catch (Exception failure) { refusal = failure; }
            }
            // The parent still owns the real resource when refusal must occur.
            // Cancellation below cleans only a defective child; it never supplies the expected refusal.
            discovery.RequireCurrent();
            await Assert.That(cancellation.IsCancellationRequested).IsFalse();
        }
        finally
        {
            cancellation.Cancel();
            try { await using var remaining = await pending; }
            catch (Exception) { /* The primary observation above classifies prompt refusal, not cleanup cancellation. */ }
        }

        parent.RequireOriginalOwner(application);
        discovery.RequireCurrent();
        await Assert.That(application.ContextId).IsEqualTo(originalContextId);
        await Assert.That(application.Database.GetDbConnection()).IsSameReferenceAs(connector);
        await Assert.That(connector.State).IsEqualTo(ConnectionState.Open);
        await Assert.That(application.Database.CurrentTransaction).IsNull();
        await Assert.That(HumanSecuritySourceGate.TryRequireCurrent(application)).IsFalse();
        await Assert.That(request.ServiceProvider.GetRequiredService<LocalIdentityExecutionLease>().Application).IsNull();
        await Assert.That(refusedBeforeCancellation).IsTrue();
        await Assert.That(refusal is InvalidOperationException).IsTrue();
        await Assert.That(refusal?.Message)
            .IsEqualTo("Fresh Local selection cannot borrow another connector's retained Discovery custody.");
    }

    private static (ExploreDbContext Application, DbContext Identity) RequireSelected(IServiceProvider services)
    {
        var lease = services.GetRequiredService<LocalIdentityExecutionLease>();
        var application = services.GetRequiredService<ExploreDbContext>();
        var identity = lease.Identity ?? throw new UnauthorizedAccessException("The selected identity context is missing.");
        lease.RequireCurrent(application, identity);
        services.GetRequiredService<ExploreDbContextExecutionLease>().RequireOriginalOwner(application);
        if (identity is ExternalIdentityDbContext)
        {
            if (!ReferenceEquals(services.GetRequiredService<ExternalIdentityDbContext>(), identity))
                throw new UnauthorizedAccessException("The child Identity store replaced its selected context.");
        }
        HumanSecuritySourceGate.RequireCurrent(application);
        LocalIdentityLifecycleGateCustody.RequireHeld(identity);
        return (application, identity);
    }

    internal static async Task<ServiceProvider> ComposeAsync(LocalCredentialFirstUseTests.Fixture fixture,
        IdentityDatabaseTopology topology, IInterceptor[]? interceptors = null)
    {
        await using var original = fixture.Provider.CreateAsyncScope();
        var application = original.ServiceProvider.GetRequiredService<ExploreDbContext>();
        DbContext identity = fixture.Identity(original);
        string applicationConnection = application.Database.GetConnectionString()!;
        string identityConnection = identity.Database.GetConnectionString()!;
        string applicationPath = new SqliteConnectionStringBuilder(applicationConnection).DataSource;
        var writer = new LocalIdentityWriterDbContextFactory(new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.LocalIdentityWriter,
            Provider = PrimaryDatabaseProvider.Sqlite,
            Database = applicationPath,
        }, topology == IdentityDatabaseTopology.External ? new ExternalIdentityDatabaseDescriptor(
            PrimaryDatabaseProvider.Sqlite, identityConnection, IdentityDatabaseConfiguration.DefaultSchema,
            null, null) : null, poolSize: 1, interceptors: interceptors);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(writer);
        services.AddPooledDbContextFactory<ExploreDbContext>(options => options
            .UseSqlite(applicationConnection).UseSnakeCaseNamingConvention());
        services.AddScoped<ExploreDbContextExecutionLease>();
        var builder = services.AddIdentityCore<LocalIdentityUser>().AddRoles<LocalIdentityRole>();
        if (topology == IdentityDatabaseTopology.External)
        {
            services.AddDbContext<ExternalIdentityDbContext>(options => options
                .UseSqlite(identityConnection).UseSnakeCaseNamingConvention());
            builder.AddEntityFrameworkStores<ExternalIdentityDbContext>();
        }
        else builder.AddEntityFrameworkStores<ExploreDbContext>();
        services.AddLocalIdentityExecutionContextSelection(topology);
        return services.BuildIsolatedServiceProvider(validateScopes: true);
    }
}
