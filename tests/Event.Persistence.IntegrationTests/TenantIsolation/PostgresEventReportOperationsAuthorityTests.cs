using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class PostgresEventReportOperationsAuthorityTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments(false, true, false)]
    [Arguments(true, true, false)]
    [Arguments(false, true, true)]
    [Arguments(false, false, false)]
    public async Task OperationalCountsIncludeBothTenantsWithoutChangingRequestLease(bool absentTenant, bool capability, bool softDeleted)
    {
        await using var rows = await PostgresAuthenticationLookupAuthorityTests.AuthenticationRows.CreateAsync(fixture);
        DateTime cutoff = DateTime.UtcNow.AddMinutes(-10);
        string table;
        string reportsTable;
        var eventIds = new List<Guid>();
        var reportIds = new List<Guid>();
        await using (var seed = fixture.CreateDbContext())
        {
            Guid actor = await seed.Actors.Where(row => row.UserId == rows.UserId).Select(row => row.Id).SingleAsync();
            foreach (Guid tenant in new[] { rows.A, rows.B })
            {
                var sourceEvent = new Explore.Domain.Event(EventStatusEnum.Draft)
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenant,
                    Tenant = null!,
                    ActorId = actor,
                    Actor = null!,
                    Title = "Operational aggregate probe",
                    EventStatus = null!,
                    CreatedAt = cutoff.AddMinutes(-20),
                    EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
                    VisibilityTypeId = (int)VisibilityTypeEnum.Public,
                    VisibilityType = null!,
                    EventFormatId = (int)EventFormatEnum.Digital,
                    EventFormat = null!
                };
                seed.Events.Add(sourceEvent);
                eventIds.Add(sourceEvent.Id);
                var report = EventReport.Create(tenant, sourceEvent.Id, null, null, EventReporterKind.Anonymous,
                    EventReportSourceKind.UserReport, "spam", null, EventReportPriority.Normal, null, false, false,
                    "en", null, null, cutoff.AddMinutes(-10));
                seed.EventReports.Add(report);
                report.IsDeleted = softDeleted && tenant == rows.B;
                reportIds.Add(report.Id);
                seed.EventReportExternalLinks.Add(EventReportExternalLink.CreatePending(
                    tenant, report.Id, null, EventReportExternalProvider.Osprey,
                    Guid.CreateVersion7().ToString("N"), cutoff.AddMinutes(-1)));
                var failed = EventReportExternalLink.CreatePending(tenant, report.Id, null,
                    EventReportExternalProvider.Osprey, Guid.CreateVersion7().ToString("N"), cutoff);
                failed.MarkFailed("health_probe", cutoff);
                seed.EventReportExternalLinks.Add(failed);
            }
            await seed.SaveChangesAsync();
            var entity = seed.Model.FindEntityType(typeof(EventReportExternalLink))!;
            table = $"\"{entity.GetSchema()}\".\"{entity.GetTableName()}\"";
            var reports = seed.Model.FindEntityType(typeof(EventReport))!;
            reportsTable = $"\"{reports.GetSchema()}\".\"{reports.GetTableName()}\"";
        }
        string role = rows.RuntimeOptions.Username ?? throw new InvalidOperationException("The native fixture requires a Runtime login.");
        string globalRole = $"report_operations_{Guid.CreateVersion7():N}";
        string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var options = rows.RuntimeOptions with { Role = PrimaryDatabaseRole.Global, Username = globalRole, Password = password };
        await using var administration = new NpgsqlConnection(fixture.ConnectionString);
        await administration.OpenAsync();
        await using (var grant = administration.CreateCommand())
        {
            grant.CommandText = $"""
                GRANT SELECT ON {table}, {reportsTable} TO "{role}";
                CREATE ROLE "{globalRole}" LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD '{password}';
                GRANT USAGE ON SCHEMA "{rows.Schema}" TO "{globalRole}";
                """;
            if (capability)
                grant.CommandText += $"\nGRANT \"{PostgresEventReportOperationsContract.GlobalRole}\" TO \"{globalRole}\" WITH INHERIT TRUE, SET FALSE;";
            await grant.ExecuteNonQueryAsync();
        }
        try
        {
            await using var context = rows.Context(absentTenant ? null : rows.A);
            await rows.AssertOrdinaryRoleAsync(context);
            await Assert.That(await context.EventReportExternalLinks.IgnoreQueryFilters()
                .AnyAsync(link => link.TenantId == rows.B)).IsFalse();
            var factory = new GlobalExploreDbContextFactory(options);
            await using (var global = await factory.CreateDbContextAsync())
            {
                var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
                    await global.EventReportExternalLinks.IgnoreQueryFilters().AsNoTracking().ToListAsync());
                await Assert.That(denied!.SqlState).IsEqualTo("42501");
                await Assert.That(global.TenantContext).IsNull();
            }
            var repository = new EventReportOperationsRepository(factory);
            if (!capability)
            {
                var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
                    await repository.CountExternalLinksBySyncStateAsync(EventReportSyncState.Pending, CancellationToken.None));
                await Assert.That(denied!.SqlState).IsEqualTo("42501");
                return;
            }
            int expected = softDeleted ? 1 : 2;
            await Assert.That(await repository.CountExternalLinksBySyncStateAsync(
                EventReportSyncState.Pending, CancellationToken.None)).IsEqualTo(expected);
            await Assert.That(await repository.CountExternalLinksBySyncStateAsync(
                EventReportSyncState.Failed, CancellationToken.None)).IsEqualTo(expected);
            await Assert.That(await repository.CountExternalLinksBySyncStateBeforeAsync(
                EventReportSyncState.Pending, cutoff, CancellationToken.None)).IsEqualTo(expected);
            await Assert.That(await repository.CountExternalLinksBySyncStateBeforeAsync(
                EventReportSyncState.Failed, cutoff, CancellationToken.None)).IsEqualTo(0);
            await Assert.That(context.TenantFilterTenantId).IsEqualTo(absentTenant ? (Guid?)null : rows.A);
        }
        finally
        {
            using var pool = new NpgsqlConnection(PrimaryDatabaseConfiguration.BuildConnectionString(options).ConnectionString);
            NpgsqlConnection.ClearPool(pool);
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.EventReportExternalLinks.IgnoreQueryFilters()
                .Where(link => reportIds.Contains(link.ReportId)).ExecuteDeleteAsync();
            await cleanup.EventReports.IgnoreQueryFilters().Where(report => reportIds.Contains(report.Id)).ExecuteDeleteAsync();
            var root = cleanup.Model.FindEntityType(typeof(Explore.Domain.Event))!.GetRootType();
            string rootTable = cleanup.GetService<ISqlGenerationHelper>()
                .DelimitIdentifier(root.GetTableName()!, root.GetSchema());
            string deleteEvents = $"DELETE FROM {rootTable} WHERE id = ANY({{0}})";
            await cleanup.Database.ExecuteSqlRawAsync(deleteEvents, [eventIds.ToArray()]);
            await using var revoke = administration.CreateCommand();
            revoke.CommandText = $"""
                REVOKE SELECT ON {table}, {reportsTable} FROM "{role}";
                REVOKE USAGE ON SCHEMA "{rows.Schema}" FROM "{globalRole}";
                """;
            if (capability)
                revoke.CommandText += $"\nREVOKE \"{PostgresEventReportOperationsContract.GlobalRole}\" FROM \"{globalRole}\";";
            revoke.CommandText += $"\nDROP ROLE \"{globalRole}\";";
            await revoke.ExecuteNonQueryAsync();
        }
    }
}
