using System.Runtime.CompilerServices;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Services;
using Explore.Application.Services.Moderation;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Privacy.ErasureAuthority.Repositories;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Data.Sqlite;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Fixtures;

internal static class NativeIdentityRecognitionBoundary
{
    private static readonly ConditionalWeakTable<ExploreDbContext, PrivacyIdentityFenceOperation> Boundaries = new();

    internal static async Task InitializeExternalMemoryAsync(ExploreDbContext context, SqliteConnection connection,
        TimeProvider clock, CancellationToken token)
    {
        await connection.OpenAsync(token);
        var options = TestDbContextOptions.Create<EmbeddedPrivacyErasureAuthorityDbContext>()
            .UseSqlite(connection).UseSnakeCaseNamingConvention().Options;
        await using (var authorityContext = new EmbeddedPrivacyErasureAuthorityDbContext(options))
            await authorityContext.Database.EnsureCreatedAsync(token);
        var authority = new EmbeddedPrivacyErasureAuthorityRepository(new AuthorityFactory(options), clock,
            Options.Create(new PrivacyErasureOptions()), applicationContext: context);
        Boundaries.Add(context, Create(context, authority, clock, RejectHumanSettlement));
    }

    internal static async Task InitializeColocatedSchemaAsync(ExploreDbContext context, CancellationToken token)
    {
        await using var authority = new EmbeddedPrivacyErasureAuthorityDbContext(
            TestDbContextOptions.Create<EmbeddedPrivacyErasureAuthorityDbContext>()
                .UseSqlite(context.Database.GetConnectionString()!).UseSnakeCaseNamingConvention().Options);
        await authority.Database.ExecuteSqlRawAsync(authority.Database.GenerateCreateScript(), token);
    }

    // Ordinary Local lifecycle/bootstrap paths never enter settled-human enrollment; cache no scoped execution lease.
    internal static PrivacyIdentityFenceOperation For(ExploreDbContext context, TimeProvider clock) =>
        Boundaries.GetValue(context, primary =>
        {
            var factory = new AuthorityFactory(TestDbContextOptions.Create<EmbeddedPrivacyErasureAuthorityDbContext>()
                .UseSqlite(primary.Database.GetConnectionString()!).UseSnakeCaseNamingConvention().Options);
            var authority = new EmbeddedPrivacyErasureAuthorityRepository(factory, clock,
                Options.Create(new PrivacyErasureOptions()), applicationContext: primary);
            return Create(primary, authority, clock, RejectHumanSettlement);
        });

    internal static PrivacyIdentityFenceOperation Create(ExploreDbContext context,
        IPrivacyIdentityFenceAuthority authority, TimeProvider clock,
        Func<IHumanActionCommitFinalizer> commitFinalizer)
    {
        var repository = new ModerationRecognitionErasureRepository(context, new UnconfiguredKeys(), clock,
            authority, (IPrivacyErasureAuthority)authority,
            new ModerationRecognitionKeyLifecycleRepository(context, authority, (IPrivacyErasureAuthority)authority,
                new ModerationRecognitionKeyLifecycleReplicaOwner(), clock));
        return new(authority, new ModerationRecognitionContinuity(repository,
            new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context))), clock,
            new UserExternalLoginRepository(context), commitFinalizer);
    }

    // These fixtures enter ExecuteEnrollmentAsync/ExecuteIdentityMutationAsync, never ExecuteSettledEnrollmentAsync.
    internal static IHumanActionCommitFinalizer RejectHumanSettlement() =>
        throw new InvalidOperationException("This ordinary identity fixture cannot settle human enrollment.");

    internal static async Task<CoLocatedPrivacyErasureAuthorityDbContext> CreatePostgresAuthorityAsync(
        PostgreSqlContainerFixture fixture, CancellationToken token)
    {
        var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString);
        string schema = $"identity_fence_{Guid.CreateVersion7():N}";
        await using (var migrations = new CoLocatedPrivacyErasureAuthorityDbContext(OptionsFor(PrimaryDatabaseRole.Migrator)))
            await migrations.Database.MigrateAsync(token);
        return new CoLocatedPrivacyErasureAuthorityDbContext(OptionsFor(PrimaryDatabaseRole.Runtime));

        DbContextOptions<CoLocatedPrivacyErasureAuthorityDbContext> OptionsFor(PrimaryDatabaseRole role)
        {
            var builder = TestDbContextOptions.Create<CoLocatedPrivacyErasureAuthorityDbContext>();
            PrimaryDatabaseProviderComposition.ConfigureCoLocatedPrivacyErasureAuthority(builder, new PrimaryDatabaseConnectionOptions
            {
                Role = role,
                Provider = PrimaryDatabaseProvider.PostgreSql,
                Host = connection.Host,
                Port = connection.Port,
                Database = connection.Database,
                Username = connection.Username,
                Password = connection.Password,
                Schema = schema,
                TlsMode = PrimaryDatabaseTlsMode.Disabled
            });
            return builder.Options;
        }
    }

    private sealed class AuthorityFactory(DbContextOptions<EmbeddedPrivacyErasureAuthorityDbContext> options)
        : IDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext>
    {
        public EmbeddedPrivacyErasureAuthorityDbContext CreateDbContext() => new(options);
    }

    private sealed class UnconfiguredKeys : IModerationRecognitionKeyRingProvider
    {
        public Task<ModerationRecognitionKeyRing> ResolveAsync(Guid trustDomainId, CancellationToken token) =>
            throw new InvalidOperationException("This ordinary identity fixture has no adopted recognition key authority.");
    }
}
