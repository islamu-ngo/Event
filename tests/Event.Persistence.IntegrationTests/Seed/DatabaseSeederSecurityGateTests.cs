using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Settings.Documents;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Explore.Persistence.Seed;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Event.Persistence.IntegrationTests.Seed;

[NotInParallel("PersistenceDb")]
public sealed class DatabaseSeederSecurityGateTests
{
    private const string Observation = "Independent observer checks the actual development seed's exact tenant graph across two startups.";

    [Test]
    public async Task DevelopmentSeederHoldsHumanBeforeDiscoveryAndPreservesItsGraphAcrossStartups()
    {
        string path = Path.Combine(Path.GetTempPath(), $"seeder-human-source-{Guid.CreateVersion7():N}.db");
        CancellationToken token = TestContext.Current!.Execution.CancellationToken;
        try
        {
            // Prerequisite lookups have their own real seed owner. It is disposed
            // before invoking the product seeder, so it cannot hide missing custody.
            await using (var preparation = Open(path))
            {
                await preparation.Database.EnsureCreatedAsync(token);
                await SqliteDatabaseInitializer.InitializeAsync(preparation, token);
                await using var human = new HumanSecuritySourceGate(preparation);
                await using var humanLease = await human.AcquireAsync(token);
                await using var discovery = new EventDiscoveryEnrollmentGate(preparation);
                await using var discoveryLease = await discovery.AcquireAsync(token);
                await LookupTableSeeder.SeedAsync(preparation, token);
            }

            var firstProbe = new FirstTenantObservation();
            await using (var original = Open(path, firstProbe))
            {
                firstProbe.ExpectedContext = original;
                await DatabaseSeeder.SeedAsync(original, new DevelopmentEnvironment(), cancellationToken: token);
                await Assert.That(firstProbe.Observed).IsTrue();
                await Assert.That(HumanSecuritySourceGate.TryRequireCurrent(original)).IsFalse();
                await Assert.That(EventDiscoveryEnrollmentGate.TryRequireCurrent(original)).IsFalse();
                await Assert.That(original.IsTenantFilterBypassed).IsFalse();
            }
            var first = await ReadGraphAsync(path, token);
            await Assert.That(first.TenantId).IsEqualTo(SeedIds.DefaultTenantId);
            await Assert.That(first.RevisionId.Version).IsEqualTo(7);
            await Assert.That(first.Users).IsEqualTo(3);
            await Assert.That(first.UserActors).IsEqualTo(3);
            await Assert.That(first.Memberships).IsEqualTo(3);
            await Assert.That(first.Grants).IsEqualTo(3);
            await Assert.That(first.Events).IsEqualTo(SeedIds.IslamicEventCatalogIds.Length);
            await Assert.That(first.BrandingDocuments).IsEqualTo(1);

            var secondProbe = new FirstTenantObservation();
            await using (var original = Open(path, secondProbe))
            {
                secondProbe.ExpectedContext = original;
                await DatabaseSeeder.SeedAsync(original, new DevelopmentEnvironment(), cancellationToken: token);
                await Assert.That(secondProbe.Observed).IsTrue();
                await Assert.That(HumanSecuritySourceGate.TryRequireCurrent(original)).IsFalse();
                await Assert.That(EventDiscoveryEnrollmentGate.TryRequireCurrent(original)).IsFalse();
                await Assert.That(original.IsTenantFilterBypassed).IsFalse();
            }
            var second = await ReadGraphAsync(path, token);
            await Assert.That(second).IsEqualTo(first);
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + "-wal");
            File.Delete(path + "-shm");
        }
    }

    private static ExploreDbContext Open(string path, params IInterceptor[] interceptors)
    {
        var context = EmailDispatchSqliteFixture.CreateContext(path, interceptors);
        context.BindTenantLease();
        return context;
    }

    private static async Task<SeededGraph> ReadGraphAsync(string path, CancellationToken token)
    {
        await using var observer = Open(path);
        var tenant = await observer.Tenants.AsNoTracking().SingleAsync(row => row.Id == SeedIds.DefaultTenantId, token);
        var revision = await observer.Set<EventDiscoveryRevision>().IgnoreTenantFilter(Observation).AsNoTracking()
            .SingleAsync(row => row.TenantId == tenant.Id, token);
        Guid[] userIds = [SeedIds.AdminUserId, SeedIds.RegularUserId, SeedIds.ModeratorUserId];
        Guid[] actorIds = [SeedIds.AdminUserActorId, SeedIds.RegularUserActorId, SeedIds.ModeratorUserActorId];
        return new(tenant.Id, tenant.Slug, revision.Id, revision.IdentityEpoch, revision.DisclosureEpoch,
            await observer.Users.CountAsync(row => userIds.Contains(row.Id), token),
            await observer.Actors.CountAsync(row => actorIds.Contains(row.Id), token),
            await observer.TenantUsers.IgnoreTenantFilter(Observation)
                .CountAsync(row => row.TenantId == tenant.Id && userIds.Contains(row.UserId), token),
            await observer.TenantUserRoleGrants.IgnoreTenantFilter(Observation).CountAsync(row => row.TenantId == tenant.Id, token),
            await observer.Events.IgnoreTenantFilter(Observation)
                .CountAsync(row => row.TenantId == tenant.Id && SeedIds.IslamicEventCatalogIds.Contains(row.Id), token),
            await observer.Set<TenantSettingsDocument>().IgnoreTenantFilter(Observation)
                .CountAsync(row => row.TenantId == tenant.Id && row.DocumentKey == SettingsDocumentKeys.Tenant.Branding, token));
    }

    private sealed record SeededGraph(Guid TenantId, string Slug, Guid RevisionId, long IdentityEpoch,
        long DisclosureEpoch, int Users, int UserActors, int Memberships, int Grants, int Events, int BrandingDocuments);

    private sealed class FirstTenantObservation : DbCommandInterceptor
    {
        internal ExploreDbContext? ExpectedContext { get; set; }
        internal bool Observed { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Observed || eventData.Context is not ExploreDbContext context) return ValueTask.FromResult(result);
            var metadata = context.Model.FindEntityType(typeof(Tenant))!;
            string table = context.GetService<ISqlGenerationHelper>().DelimitIdentifier(metadata.GetTableName()!, metadata.GetSchema());
            if (!command.CommandText.Contains("FROM " + table, StringComparison.OrdinalIgnoreCase)) return ValueTask.FromResult(result);
            if (!ReferenceEquals(context, ExpectedContext)
                || !ReferenceEquals(command.Connection, context.Database.GetDbConnection())
                || context.Database.CurrentTransaction is not null)
                throw new InvalidOperationException("seeder_test_original_idle_context_missing");
            HumanSecuritySourceGate.RequireCurrent(context);
            EventDiscoveryEnrollmentGate.RequireCurrent(context);
            Observed = true;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class DevelopmentEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Event.Persistence.IntegrationTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
