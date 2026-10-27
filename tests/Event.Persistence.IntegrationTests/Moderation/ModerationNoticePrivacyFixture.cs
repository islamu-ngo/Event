using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Configuration;
using Explore.Persistence;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Privacy.ErasureAuthority.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace Event.Persistence.IntegrationTests.Moderation;

internal sealed class ModerationNoticePrivacyFixture(
    SqliteConnection connection, DbContextOptions<EmbeddedPrivacyErasureAuthorityDbContext> options,
    bool ownsConnection = true) : IAsyncDisposable
{
    internal static async Task<ModerationNoticePrivacyFixture> CreateAsync()
    {
        var connection = await SqliteTestDatabaseFactory.CreateOpenIsolatedConnectionAsync();
        var options = TestDbContextOptions.Create<EmbeddedPrivacyErasureAuthorityDbContext>()
            .UseSqlite(connection).UseSnakeCaseNamingConvention().Options;
        await using var context = new EmbeddedPrivacyErasureAuthorityDbContext(options);
        await context.Database.EnsureCreatedAsync();
        return new(connection, options);
    }

    internal static async Task<ModerationNoticePrivacyFixture> CreateColocatedAsync(
        ExploreDbContext primary, params IInterceptor[] interceptors)
    {
        var connection = (SqliteConnection)primary.Database.GetDbConnection();
        var options = TestDbContextOptions.Create<EmbeddedPrivacyErasureAuthorityDbContext>()
            .UseSqlite(connection).UseSnakeCaseNamingConvention().AddInterceptors(interceptors).Options;
        await using var schema = new EmbeddedPrivacyErasureAuthorityDbContext(options);
        await schema.Database.ExecuteSqlRawAsync(schema.Database.GenerateCreateScript());
        return new(connection, options, ownsConnection: false);
    }

    internal static ModerationNoticePrivacyFixture ReuseColocated(
        ExploreDbContext primary, params IInterceptor[] interceptors)
    {
        var connection = (SqliteConnection)primary.Database.GetDbConnection();
        var options = TestDbContextOptions.Create<EmbeddedPrivacyErasureAuthorityDbContext>()
            .UseSqlite(connection).UseSnakeCaseNamingConvention().AddInterceptors(interceptors).Options;
        // LocalIdentitySqliteTemplate already includes the co-located privacy authority schema.
        return new(connection, options, ownsConnection: false);
    }

    internal EmbeddedPrivacyErasureAuthorityRepository Authority(ExploreDbContext primary, TimeProvider clock) =>
        new(new AuthorityFactory(options), clock, Options.Create(new PrivacyErasureOptions()), applicationContext: primary);

    public ValueTask DisposeAsync() => ownsConnection ? connection.DisposeAsync() : ValueTask.CompletedTask;

    private sealed class AuthorityFactory(DbContextOptions<EmbeddedPrivacyErasureAuthorityDbContext> options)
        : IDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext>
    {
        public EmbeddedPrivacyErasureAuthorityDbContext CreateDbContext() => new(options);
    }
}
