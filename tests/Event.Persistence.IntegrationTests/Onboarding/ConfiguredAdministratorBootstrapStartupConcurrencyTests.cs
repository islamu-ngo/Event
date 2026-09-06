// ABOUTME: Proves configured-administrator startup converges when concurrent transactions observe an empty database.
// ABOUTME: Uses a command interceptor barrier and shared-file SQLite so the race is deterministic without timing waits.

using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Services;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Secrets.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using Microsoft.Extensions.Configuration;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Onboarding;

[NotInParallel("ConfiguredAdministratorBootstrapSqlite")]
public sealed class ConfiguredAdministratorBootstrapStartupConcurrencyTests
{
    private static readonly DateTime PreparedAt =
        new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task ConcurrentPrepareAgainstEmptySharedFileSqliteBothSucceedWithOnePendingGeneration()
    {
        string databasePath = Path.Combine(
            Path.GetTempPath(),
            $"configured-bootstrap-{Guid.NewGuid():N}.db");
        var coordination = new EmptyBootstrapRaceCoordination();

        try
        {
            await using (ExploreDbContext setup = CreateContext(databasePath))
            {
                await setup.Database.EnsureCreatedAsync();
                await setup.Database.ExecuteSqlRawAsync(
                    "PRAGMA journal_mode=WAL;");
            }

            IConfiguration configuration = CreateConfiguration();

            await using ExploreDbContext firstContext = CreateContext(
                databasePath,
                new FirstEmptyBootstrapReadBarrier(coordination));
            await using ExploreDbContext secondContext = CreateContext(
                databasePath,
                new SecondConnectionBarrier(coordination));
            ConfiguredAdministratorBootstrapStartupRunner first = CreateRunner(firstContext, configuration);
            ConfiguredAdministratorBootstrapStartupRunner second = CreateRunner(secondContext, configuration);

            // Initialize models without opening connections or triggering the race interceptors.
            _ = firstContext.Model;
            _ = secondContext.Model;
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            {
                await Task.WhenAll(
                    first.PrepareAsync(timeout.Token),
                    second.PrepareAsync(timeout.Token));
            }

            await using ExploreDbContext verification = CreateContext(databasePath);
            var states = await verification.InstanceBootstrapStates
                .AsNoTracking()
                .ToListAsync();
            await Assert.That(states).Count().IsEqualTo(1);
            await Assert.That(states[0].Status).IsEqualTo(InstanceBootstrapStatus.Pending);
            await Assert.That(states[0].Generation).IsEqualTo(1L);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    internal static IConfiguration CreateConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["INSTANCE_BOOTSTRAP_MODE"] = "ConfiguredAdministrator",
                ["INSTANCE_BOOTSTRAP_ADMIN_PROVIDER"] = "keycloak",
                ["INSTANCE_BOOTSTRAP_ADMIN_SUBJECT"] = "concurrent-subject",
                ["INSTANCE_BOOTSTRAP_BINDING_GENERATION"] = "1",
                ["INSTANCE_BOOTSTRAP_ADMIN_EMAIL"] = "administrator@example.test",
                ["INSTANCE_BOOTSTRAP_ADMIN_FIRST_NAME"] = "Configured",
                ["INSTANCE_BOOTSTRAP_ADMIN_LAST_NAME"] = "Administrator",
                ["Keycloak:Authority"] = "https://identity.example.test/realms/event",
                ["Deployment:Mode"] = "SingleTenant"
            })
            .Build();

    internal static ConfiguredAdministratorBootstrapStartupRunner CreateRunner(
        ExploreDbContext context,
        IConfiguration configuration)
    {
        var repository = new InstanceBootstrapStateRepository(context);
        var provider = new ConfiguredAdministratorBootstrapProvider(
            configuration,
            InstanceOperatorIdentity.Create(new InstanceOperatorIdentityOptions
            {
                OperatorId = Guid.Parse("01991f00-0000-7000-8000-000000000001"),
                PublicName = "Concurrent Test Operator",
                LegalName = "Concurrent Test Operator ASBL",
                OfficialOrigin = "https://example.test",
                OperatorKindCode = "registered_organization",
                JurisdictionCountryCode = "BE",
                RegistrationIdentifier = "BE 0123.456.789",
                PublicContactEmail = "contact@example.test",
                WebsiteUrl = "https://example.test",
                LegalNoticeUrl = "https://example.test/legal",
                TermsUrl = "https://example.test/terms",
                PrivacyUrl = "https://example.test/privacy"
            }),
            repository);
        return new ConfiguredAdministratorBootstrapStartupRunner(
            provider,
            repository,
            new EfCoreUnitOfWork(context),
            new FixedTimeProvider(PreparedAt));
    }

    private static ExploreDbContext CreateContext(
        string databasePath,
        params IInterceptor[] interceptors)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false,
            DefaultTimeout = 1
        }.ToString();
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(interceptors)
            .Options;
        return new ExploreDbContext(options);
    }

    private sealed class EmptyBootstrapRaceCoordination
    {
        public TaskCompletionSource FirstRead { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondAttemptCompleted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class FirstEmptyBootstrapReadBarrier(
        EmptyBootstrapRaceCoordination coordination) : DbCommandInterceptor
    {
        private int _entered;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _entered, 1) != 0
                || !command.CommandText.Contains(
                    "instance_bootstrap_states",
                    StringComparison.OrdinalIgnoreCase))
            {
                return result;
            }

            coordination.FirstRead.TrySetResult();
            await coordination.SecondAttemptCompleted.Task.WaitAsync(cancellationToken);
            return result;
        }
    }

    private sealed class SecondConnectionBarrier(
        EmptyBootstrapRaceCoordination coordination) : DbConnectionInterceptor
    {
        private int _entered;

        public override async Task ConnectionOpenedAsync(
            DbConnection connection,
            ConnectionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _entered, 1) != 0)
            {
                return;
            }

            await coordination.FirstRead.Task.WaitAsync(cancellationToken);
            await using DbCommand command = connection.CreateCommand();
            command.CommandText = "BEGIN IMMEDIATE;";
            try
            {
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            finally
            {
                coordination.SecondAttemptCompleted.TrySetResult();
            }
        }
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}

[ClassDataSource<AdmissionAuthorityProviderFixture>(Shared = SharedType.PerClass)]
[NotInParallel("ConfiguredAdministratorBootstrapMySql")]
public sealed class ConfiguredAdministratorBootstrapMySqlConcurrencyTests(
    AdmissionAuthorityProviderFixture fixture)
{
    [Test]
    [Arguments(PrimaryDatabaseProvider.MariaDb)]
    [Arguments(PrimaryDatabaseProvider.MySql)]
    public async Task ConcurrentPrepareAgainstEmptyMySqlFamilyDatabaseConverges(
        PrimaryDatabaseProvider provider)
    {
        await using (ExploreDbContext setup = CreateContext(fixture.CreateOptions(provider)))
        {
            await setup.Database.EnsureCreatedAsync();
        }

        // Bound the bootstrap race, not provisioning the full application schema.
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var barrier = new EmptyReadBarrier(participantCount: 2);
        IConfiguration configuration =
            ConfiguredAdministratorBootstrapStartupConcurrencyTests.CreateConfiguration();
        await using ExploreDbContext firstContext = CreateContext(fixture.CreateOptions(provider), barrier);
        await using ExploreDbContext secondContext = CreateContext(fixture.CreateOptions(provider), barrier);
        ConfiguredAdministratorBootstrapStartupRunner first =
            ConfiguredAdministratorBootstrapStartupConcurrencyTests.CreateRunner(
                firstContext,
                configuration);
        ConfiguredAdministratorBootstrapStartupRunner second =
            ConfiguredAdministratorBootstrapStartupConcurrencyTests.CreateRunner(
                secondContext,
                configuration);

        await Task.WhenAll(
                first.PrepareAsync(timeout.Token),
                second.PrepareAsync(timeout.Token))
            .WaitAsync(timeout.Token);

        await using ExploreDbContext verification = CreateContext(fixture.CreateOptions(provider));
        var states = await verification.InstanceBootstrapStates
            .AsNoTracking()
            .ToListAsync(timeout.Token);
        await Assert.That(states).Count().IsEqualTo(1);
        await Assert.That(states[0].Status).IsEqualTo(InstanceBootstrapStatus.Pending);
        await Assert.That(states[0].Generation).IsEqualTo(1L);
    }

    private static ExploreDbContext CreateContext(
        PrimaryDatabaseConnectionOptions options,
        params IInterceptor[] interceptors)
    {
        var builder = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder, options);
        builder.AddInterceptors(interceptors);
        return new ExploreDbContext(builder.Options);
    }

    private sealed class EmptyReadBarrier(int participantCount) : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _allRead =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _readers;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (!command.CommandText.Contains(
                    "instance_bootstrap_states",
                    StringComparison.OrdinalIgnoreCase))
            {
                return result;
            }

            if (Interlocked.Increment(ref _readers) == participantCount)
            {
                _allRead.TrySetResult();
            }

            await _allRead.Task.WaitAsync(cancellationToken);
            return result;
        }
    }
}
