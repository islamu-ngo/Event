using Event.Persistence.IntegrationTests;
using Explore.API.Hosting;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Seed;
using Explore.Persistence.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Event.Api.IntegrationTests.Features;

/// <summary>
/// Exercises startup construction and disposal, not native grants or a completed bootstrap.
/// Real preparation uses SQLite in memory; poison constructors stop later work.
/// </summary>
public sealed class ApiStartupOwnerSelectionTests
{
    [Test]
    public async Task BootstrapWaitsForInstanceSelectionBeforeConstructingDependencies()
    {
        await using var composition = new Composition();
        composition.State.PauseGlobalSelection = true;
        using var cancellation = new CancellationTokenSource();
        Task execution = composition.Application.PrepareConfiguredAdministratorBootstrapAsync(cancellation.Token);
        try
        {
            await ObserveSelection(composition.State.GlobalRequested.Task, execution);
            await Assert.That(composition.State.RepositoryConstructed).IsFalse();
            await Assert.That(composition.State.RunnerConstructed).IsFalse();
            await Assert.That(composition.State.Owners.Count).IsEqualTo(0);
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => execution);
            await Assert.That(composition.State.WrongOwnerConstructed).IsFalse();
            await Assert.That(composition.State.Owners.Count).IsEqualTo(0);
        }
        finally
        {
            cancellation.Cancel();
            composition.State.ReleaseGlobal.TrySetResult();
            await ObserveSettlement(execution);
        }
    }

    [Test]
    public async Task BootstrapPreparationAndCompletionConstructionShareOneInstanceOwner()
    {
        await using var composition = new Composition();

        await Assert.ThrowsAsync<CompletionConstructionReached>(() =>
            composition.Application.PrepareConfiguredAdministratorBootstrapAsync(CancellationToken.None));

        var owner = composition.State.Owners.Single();
        await Assert.That(owner.Kind).IsEqualTo(OwnerKind.Global);
        await Assert.That(owner.Disposed).IsTrue();
        await Assert.That(composition.State.RepositoryOwner).IsSameReferenceAs(owner);
        await Assert.That(composition.State.RunnerOwner).IsSameReferenceAs(owner);
        await Assert.That(composition.State.WrongOwnerConstructed).IsFalse();
        await Assert.That(await composition.BootstrapRowCountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task FailedPreparationConstructionReleasesInstanceOwnerWithoutConstructingCompletion()
    {
        await using var composition = new Composition();
        composition.State.FailPreparationConstruction = true;

        await Assert.ThrowsAsync<PreparationConstructionFailure>(() =>
            composition.Application.PrepareConfiguredAdministratorBootstrapAsync(CancellationToken.None));

        var owner = composition.State.Owners.Single();
        await Assert.That(owner.Kind).IsEqualTo(OwnerKind.Global);
        await Assert.That(owner.Disposed).IsTrue();
        await Assert.That(composition.State.RepositoryOwner).IsSameReferenceAs(owner);
        await Assert.That(composition.State.RunnerConstructed).IsFalse();
        await Assert.That(composition.State.WrongOwnerConstructed).IsFalse();
        await Assert.That(await composition.BootstrapRowCountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task PersonaReceiptClassificationSelectsLocalBeforeCredentialConstruction()
    {
        await using var composition = new Composition();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["INSTANCE_BOOTSTRAP_MODE"] = "ConfiguredAdministrator",
            ["INSTANCE_BOOTSTRAP_ADMIN_PROVIDER"] = "local",
            ["INSTANCE_BOOTSTRAP_ADMIN_SUBJECT"] = AgentBrowserPersonaCatalog.Administrator.SubjectId.ToString("D")
        }).Build();

        await Assert.ThrowsAsync<CredentialConstructionReached>(() =>
            AgentBrowserPersonaStartup.ProvisionAsync(composition.Application.Services, configuration, CancellationToken.None));

        var owner = composition.State.Owners.Single();
        await Assert.That(owner.Kind).IsEqualTo(OwnerKind.Local);
        await Assert.That(owner.Disposed).IsTrue();
        await Assert.That(composition.State.CredentialOwner).IsSameReferenceAs(owner);
        await Assert.That(composition.State.WrongOwnerConstructed).IsFalse();
        await Assert.That(composition.State.GlobalRequested.Task.IsCompleted).IsFalse();
        await Assert.That(await composition.BootstrapRowCountAsync()).IsEqualTo(0);
    }

    private static async Task ObserveSelection(Task requested, Task execution)
    {
        Task first = await Task.WhenAny(requested, execution).WaitAsync(TimeSpan.FromSeconds(5));
        if (ReferenceEquals(first, execution)) await execution;
        await requested.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static async Task ObserveSettlement(Task execution)
    {
        try { await execution.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (OperationCanceledException) { }
        catch (WrongOwnerConstruction) { }
    }

    private sealed class Composition : IAsyncDisposable
    {
        private readonly ServiceProvider global;
        private readonly ServiceProvider local;
        private readonly SqliteConnection connection;
        private readonly DbContextOptions<ExploreDbContext> databaseOptions;
        internal State State { get; } = new();
        internal WebApplication Application { get; }

        internal Composition()
        {
            connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = ":memory:" }.ConnectionString);
            connection.Open();
            databaseOptions = TestDbContextOptions.Create<ExploreDbContext>().UseSqlite(connection)
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(SqliteNamedLockTransactionInterceptor.Instance, SqliteProjectionLockTransactionInterceptor.Instance)
                .Options;
            using (var database = new ExploreDbContext(databaseOptions))
                database.Database.EnsureCreated();
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["INSTANCE_BOOTSTRAP_MODE"] = "Interactive",
                ["Deployment:Mode"] = "MultiTenant"
            }).Build();
            global = Services(OwnerKind.Global, configuration).BuildIsolatedServiceProvider(validateScopes: true);
            local = Services(OwnerKind.Local, configuration).BuildIsolatedServiceProvider(validateScopes: true);
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            foreach (var descriptor in Services(OwnerKind.Ambient, configuration))
                builder.Services.Add(descriptor);
            builder.Services.AddSingleton<IPolicyMutationExecutionScopeFactory>(new GlobalFactory(global, State));
            builder.Services.AddSingleton<ILocalIdentityExecutionScopeFactory>(new LocalFactory(local, State));
            Application = builder.Build();
        }

        private ServiceCollection Services(OwnerKind kind, IConfiguration configuration)
        {
            var services = new ServiceCollection();
            services.AddSingleton(configuration);
            services.AddSingleton<TimeProvider>(TimeProvider.System);
            services.AddSingleton(Options.Create(new InstanceOperatorIdentityOptions()));
            services.AddScoped(_ => new Owner(State, kind));
            services.AddScoped(_ =>
            {
                var database = new ExploreDbContext(databaseOptions);
                database.BindTenantLease();
                return database;
            });
            services.AddScoped<IUnitOfWork, EfCoreUnitOfWork>();
            services.AddScoped<IEventDiscoveryEnrollmentGate, EventDiscoveryEnrollmentGate>();
            services.AddScoped<IInstanceBootstrapStateRepository>(provider =>
            {
                var owner = provider.GetRequiredService<Owner>();
                owner.Require(OwnerKind.Global);
                State.RepositoryOwner = owner;
                if (State.FailPreparationConstruction) throw new PreparationConstructionFailure();
                return new InstanceBootstrapStateRepository(provider.GetRequiredService<ExploreDbContext>());
            });
            services.AddScoped<LocalAdministratorBootstrapRunner>(provider =>
            {
                var owner = provider.GetRequiredService<Owner>();
                owner.Require(OwnerKind.Global);
                State.RunnerOwner = owner;
                // Actual preparation must finish before startup resolves this completion graph.
                throw new CompletionConstructionReached();
            });
            services.AddScoped<ILocalCredentialAdministration>(provider =>
            {
                var owner = provider.GetRequiredService<Owner>();
                owner.Require(OwnerKind.Local);
                State.CredentialOwner = owner;
                throw new CredentialConstructionReached();
            });
            return services;
        }

        internal async Task<int> BootstrapRowCountAsync()
        {
            await using var database = new ExploreDbContext(databaseOptions);
            return await database.InstanceBootstrapStates.CountAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Application.DisposeAsync();
            await local.DisposeAsync();
            await global.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private enum OwnerKind { Ambient, Global, Local }

    private sealed class State
    {
        internal bool PauseGlobalSelection { get; set; }
        internal bool FailPreparationConstruction { get; set; }
        internal bool WrongOwnerConstructed { get; set; }
        internal Owner? RepositoryOwner { get; set; }
        internal Owner? RunnerOwner { get; set; }
        internal Owner? CredentialOwner { get; set; }
        internal bool RepositoryConstructed => RepositoryOwner is not null;
        internal bool RunnerConstructed => RunnerOwner is not null;
        internal List<Owner> Owners { get; } = [];
        internal TaskCompletionSource GlobalRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseGlobal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class Owner(State state, OwnerKind kind) : IAsyncDisposable
    {
        internal OwnerKind Kind => kind;
        internal bool Disposed { get; private set; }

        internal void Require(OwnerKind expected)
        {
            if (Disposed) throw new ObjectDisposedException(nameof(Owner));
            if (kind == expected) return;
            state.WrongOwnerConstructed = true;
            throw new WrongOwnerConstruction();
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class GlobalFactory(ServiceProvider services, State state) : IPolicyMutationExecutionScopeFactory
    {
        public async ValueTask<IPolicyMutationExecutionScope> CreateAsync(PolicyMutationScope selection,
            CancellationToken cancellationToken)
        {
            if (selection != PolicyMutationScope.Instance) throw new WrongOwnerConstruction();
            state.GlobalRequested.TrySetResult();
            if (state.PauseGlobalSelection) await state.ReleaseGlobal.Task.WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var scope = services.CreateAsyncScope();
            state.Owners.Add(scope.ServiceProvider.GetRequiredService<Owner>());
            return new GlobalScope(scope);
        }
    }

    private sealed class LocalFactory(ServiceProvider services, State state) : ILocalIdentityExecutionScopeFactory
    {
        public ValueTask<ILocalIdentityExecutionScope> CreateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scope = services.CreateAsyncScope();
            state.Owners.Add(scope.ServiceProvider.GetRequiredService<Owner>());
            return ValueTask.FromResult<ILocalIdentityExecutionScope>(new LocalScope(scope));
        }
    }

    private sealed class GlobalScope(AsyncServiceScope scope) : IPolicyMutationExecutionScope
    {
        public IServiceProvider Services => scope.ServiceProvider;
        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }

    private sealed class LocalScope(AsyncServiceScope scope) : ILocalIdentityExecutionScope
    {
        public IServiceProvider Services => scope.ServiceProvider;
        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }

    private sealed class WrongOwnerConstruction : Exception;
    private sealed class PreparationConstructionFailure : Exception;
    private sealed class CompletionConstructionReached : Exception;
    private sealed class CredentialConstructionReached : Exception;
}
