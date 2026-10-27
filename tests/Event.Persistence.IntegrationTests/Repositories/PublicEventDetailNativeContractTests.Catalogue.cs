using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed partial class PublicEventDetailNativeContractTests
{
    [Test]
    public async Task CatalogueColumnsCoexistWithAllEventReadOwnersAfterReinstallation()
    {
        var seeded = await SeedAsync();
        await using (var migration = fixture.CreateDbContext())
        {
            foreach (var owner in CatalogueProjectionOwners(migration)) await owner.Apply();
            await PostgresLookupCatalogueReadPermissions.ApplyAsync(migration, fixture.NativeQueueEnrollment);
            foreach (var owner in CatalogueProjectionOwners(migration)) await owner.Apply();
        }
        await using (var catalogue = fixture.CreateEnrolledTenantlessDbContext())
            await Assert.That((await GlobalLookupCatalogueProviderOperations.ReadAsync<EventFormat>(
                catalogue, fixture.NativeQueueEnrollment, default)).Any()).IsTrue();
        await using var runtime = Open(PrimaryDatabaseRole.Runtime, seeded.Tenant);
        await Assert.That((await ReadAsync(runtime, seeded.Event))!.Id).IsEqualTo(seeded.Event);
    }

    [Test]
    public async Task EventReadOwnersDoNotRequireCatalogueGrantCompleteness()
    {
        await using var migration = fixture.CreateDbContext();
        await using var transaction = await migration.Database.BeginTransactionAsync();
        var names = new HumanPrincipalSourceSql(migration);
        await migration.Database.ExecuteSqlRawAsync(
            $"REVOKE SELECT ({names.Column(typeof(EventFormat), nameof(EventFormat.Description))}) ON {names.Table(typeof(EventFormat))} FROM {names.Quote(fixture.NativeQueueEnrollment.RuntimeLogin)}");
        foreach (var owner in CatalogueProjectionOwners(migration)) await owner.Apply();
        await transaction.RollbackAsync();
    }

    [Test]
    [Arguments("update")]
    [Arguments("table-select")]
    [Arguments("xmin")]
    [Arguments("grant-option")]
    [Arguments("inherited")]
    [Arguments("set-only")]
    [Arguments("admin")]
    [Arguments("public")]
    [Arguments("private-event")]
    [Arguments("parent-grantor")]
    [Arguments("parent-grant-option")]
    [Arguments("parent-inherited")]
    [Arguments("parent-admin")]
    [Arguments("parent-public")]
    [Arguments("parent-update")]
    public async Task CatalogueExceptionCannotExpandAnyEventReadOwnersSourceAuthority(string change)
    {
        await using var migration = fixture.CreateDbContext();
        await using var transaction = await migration.Database.BeginTransactionAsync();
        var names = new HumanPrincipalSourceSql(migration);
        string table = names.Table(typeof(EventFormat));
        string id = names.Column(typeof(EventFormat), nameof(EventFormat.Id));
        string runtime = names.Quote(fixture.NativeQueueEnrollment.RuntimeLogin);
        string otherRole = names.Quote("catalogue_projection_drift_" + Guid.CreateVersion7().ToString("N"));
        string eventTable = names.Table(typeof(Explore.Domain.Event));
        string eventId = names.Column(typeof(Explore.Domain.Event), nameof(Explore.Domain.Event.Id));
        await migration.Database.ExecuteSqlRawAsync(change switch
        {
            "update" => $"GRANT UPDATE ({id}) ON {table} TO {runtime}",
            "table-select" => $"GRANT SELECT ON {table} TO {runtime}",
            "xmin" => $"GRANT SELECT (xmin) ON {table} TO {runtime}",
            "grant-option" => $"GRANT SELECT ({id}) ON {table} TO {runtime} WITH GRANT OPTION",
            "inherited" => $"CREATE ROLE {otherRole} NOLOGIN; GRANT SELECT ({id}) ON {table} TO {otherRole}; GRANT {otherRole} TO {runtime} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE",
            "set-only" => $"CREATE ROLE {otherRole} NOLOGIN; GRANT SELECT ({id}) ON {table} TO {otherRole}; GRANT {otherRole} TO {runtime} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE",
            "admin" => $"CREATE ROLE {otherRole} NOLOGIN; GRANT SELECT ({id}) ON {table} TO {otherRole}; GRANT {otherRole} TO {runtime} WITH INHERIT FALSE, SET FALSE, ADMIN TRUE",
            "public" => $"GRANT SELECT ({id}) ON {table} TO PUBLIC",
            "private-event" => $"GRANT SELECT ({names.Column(typeof(Explore.Domain.Event), nameof(Explore.Domain.Event.Title))}) ON {names.Table(typeof(Explore.Domain.Event))} TO {runtime}",
            "parent-grant-option" => $"GRANT SELECT ({eventId}) ON {eventTable} TO {runtime} WITH GRANT OPTION",
            "parent-inherited" => $"CREATE ROLE {otherRole} NOLOGIN; GRANT SELECT ({eventId}) ON {eventTable} TO {otherRole}; GRANT {otherRole} TO {runtime} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE",
            "parent-admin" => $"CREATE ROLE {otherRole} NOLOGIN; GRANT SELECT ({eventId}) ON {eventTable} TO {otherRole}; GRANT {otherRole} TO {runtime} WITH INHERIT FALSE, SET FALSE, ADMIN TRUE",
            "parent-public" => $"GRANT SELECT ({eventId}) ON {eventTable} TO PUBLIC",
            "parent-update" => $"GRANT UPDATE ({eventId}) ON {eventTable} TO {runtime}",
            "parent-grantor" => $"""
                CREATE ROLE {otherRole} NOLOGIN;
                GRANT {otherRole} TO {names.Quote(fixture.NativeQueueEnrollment.MigratorLogin)} WITH INHERIT FALSE, SET TRUE, ADMIN TRUE;
                GRANT USAGE ON SCHEMA {names.Quote(names.Schema)} TO {otherRole};
                REVOKE SELECT ({names.Column(typeof(Explore.Domain.Event), nameof(Explore.Domain.Event.Id))}) ON {names.Table(typeof(Explore.Domain.Event))} FROM {runtime};
                GRANT SELECT ({names.Column(typeof(Explore.Domain.Event), nameof(Explore.Domain.Event.Id))}) ON {names.Table(typeof(Explore.Domain.Event))} TO {otherRole} WITH GRANT OPTION;
                SET LOCAL ROLE {otherRole};
                GRANT SELECT ({names.Column(typeof(Explore.Domain.Event), nameof(Explore.Domain.Event.Id))}) ON {names.Table(typeof(Explore.Domain.Event))} TO {runtime};
                RESET ROLE;
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        });
        var owners = CatalogueProjectionOwners(migration);
        if (change.StartsWith("parent-", StringComparison.Ordinal) || change == "private-event")
            owners = [.. owners,
                ("public_clock", () => PostgresPublicEventResponseClockContract.ApplyAsync(migration, fixture.NativeQueueEnrollment)),
                ("registration_private_read", () => PostgresRegistrationPrivateReadContract.ApplyAsync(migration, fixture.NativeQueueEnrollment))];
        foreach (var owner in owners)
        {
            await transaction.CreateSavepointAsync("before_projection_admission");
            var denied = await Assert.ThrowsAsync<PostgresException>(owner.Apply);
            bool publicGrant = change is "public" or "parent-public";
            await Assert.That(denied!.SqlState).IsEqualTo(publicGrant
                ? PostgresErrorCodes.ObjectNotInPrerequisiteState : PostgresErrorCodes.InsufficientPrivilege);
            await Assert.That(denied.MessageText).IsEqualTo(publicGrant
                ? "native_privilege_guard_not_ready" : owner.Prefix + "_runtime_source_privilege");
            await transaction.RollbackToSavepointAsync("before_projection_admission");
        }
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task CatalogueRoleExpansionRemainsLocalToThePrivateManagementSourceInventory()
    {
        await using var migration = fixture.CreateDbContext();
        await using var transaction = await migration.Database.BeginTransactionAsync();
        var names = new HumanPrincipalSourceSql(migration);
        await migration.Database.ExecuteSqlRawAsync(
            $"GRANT UPDATE ({names.Column(typeof(Role), nameof(Role.Id))}) ON {names.Table(typeof(Role))} TO {names.Quote(fixture.NativeQueueEnrollment.RuntimeLogin)}");
        await PostgresPublicEventDetailContract.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        await PostgresPublicEventDiscoveryContract.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        var denied = await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresPrivateEventManagementContract.ApplyAsync(migration, fixture.NativeQueueEnrollment));
        await Assert.That(denied!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        await Assert.That(denied.MessageText).IsEqualTo("private_management_runtime_source_privilege");
        await transaction.RollbackAsync();
    }

    private (string Prefix, Func<Task> Apply)[] CatalogueProjectionOwners(ExploreDbContext context) =>
    [
        ("public_detail", () => PostgresPublicEventDetailContract.ApplyAsync(context, fixture.NativeQueueEnrollment)),
        ("public_discovery", () => PostgresPublicEventDiscoveryContract.ApplyAsync(context, fixture.NativeQueueEnrollment)),
        ("private_management", () => PostgresPrivateEventManagementContract.ApplyAsync(context, fixture.NativeQueueEnrollment))
    ];
}
