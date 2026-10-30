using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Security;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed partial class PublicEventResponseClockNativeContractTests
{
    [Test]
    public async Task ChangedSessionTenantCannotReadTheClock()
    {
        var seeded = await SeedAsync();
        await using var reader = Open(PrimaryDatabaseRole.Runtime, seeded.Tenant);
        await using var transaction = await reader.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await reader.Database.ExecuteSqlRawAsync(
            "SELECT set_config('app.current_tenant_id',{0},true)", Guid.CreateVersion7().ToString());
        var denied = await Assert.ThrowsAsync<PostgresException>(() => ReadAsync(reader));
        await Assert.That(denied!.SqlState).IsEqualTo("42501");
        await transaction.RollbackAsync();
    }

    [Test]
    [Arguments("acl")]
    [Arguments("body")]
    [Arguments("policy")]
    [Arguments("column")]
    [Arguments("role")]
    public async Task AdmissionAndReinstallationRejectDriftWithoutRepair(string change)
    {
        await SeedAsync();
        await using var writer = fixture.CreateDbContext();
        await using var transaction = await writer.Database.BeginTransactionAsync();
        var names = new HumanPrincipalSourceSql(writer);
        string function = PostgresPublicEventResponseClockContract.FunctionSql(writer);
        const string signature = "uuid,text,timestamp with time zone,timestamp with time zone,boolean,boolean";
        await writer.Database.ExecuteSqlRawAsync(change switch
        {
            "acl" => $"GRANT EXECUTE ON FUNCTION {function}({signature}) TO PUBLIC",
            "body" => $"""
                GRANT CREATE ON SCHEMA {names.Quote(names.Schema)} TO {PostgresPublicEventResponseClockContract.OwnerRole};
                SET LOCAL ROLE {PostgresPublicEventResponseClockContract.OwnerRole};
                CREATE OR REPLACE FUNCTION {function}(
                    p_tenant uuid,p_operation text,p_now timestamptz,p_threshold timestamptz,p_address boolean,p_coordinates boolean)
                    RETURNS timestamptz LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                    AS $changed$ BEGIN RETURN NULL; END; $changed$;
                RESET ROLE;
                REVOKE CREATE ON SCHEMA {names.Quote(names.Schema)} FROM {PostgresPublicEventResponseClockContract.OwnerRole};
                """,
            "policy" => $"ALTER POLICY public_event_response_clock_read ON {names.Table(typeof(Explore.Domain.Event))} USING (true)",
            "column" => $"GRANT SELECT ({names.Column(typeof(EventSession), nameof(EventSession.Title))}) ON {names.Table(typeof(EventSession))} TO {PostgresPublicEventResponseClockContract.OwnerRole}",
            "role" => $"ALTER ROLE {PostgresPublicEventResponseClockContract.RuntimeRole} INHERIT",
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        });
        // Savepoints let both read-only admission and reinstall see the same tampering.
        // The parent transaction rolls it all back, including role and ACL changes.
        await transaction.CreateSavepointAsync("before_admission");
        var denied = await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresPublicEventResponseClockContract.RequireReadyAsync(writer, fixture.NativeQueueEnrollment));
        await Assert.That(denied!.SqlState).IsEqualTo("55000");
        await transaction.RollbackToSavepointAsync("before_admission");
        denied = await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresPublicEventResponseClockContract.ApplyAsync(writer, fixture.NativeQueueEnrollment));
        await Assert.That(denied!.SqlState).IsEqualTo("55000");
        await transaction.RollbackAsync();
    }

    [Test]
    [Arguments("session-cancelled")]
    [Arguments("session-deleted")]
    [Arguments("event-deleted")]
    [Arguments("membership-deleted")]
    [Arguments("actor-suspended")]
    public async Task IneligibleSourcesDoNotContributeClockBoundaries(string change)
    {
        var seeded = await SeedAsync();
        await using (var writer = fixture.CreateDbContext())
        {
            switch (change)
            {
                case "session-cancelled":
                    (await writer.EventSessions.SingleAsync(value => value.Id == seeded.Session))
                        .Cancel(EventStatusEnum.Published, Now.UtcDateTime);
                    break;
                case "session-deleted":
                    (await writer.EventSessions.SingleAsync(value => value.Id == seeded.Session)).IsDeleted = true;
                    break;
                case "event-deleted":
                    (await writer.Events.SingleAsync(value => value.Id == seeded.Event)).IsDeleted = true;
                    break;
                case "membership-deleted":
                    (await writer.TenantUsers.SingleAsync(value => value.TenantId == seeded.Tenant)).IsDeleted = true;
                    break;
                case "actor-suspended":
                    var actorId = await writer.Events.Where(value => value.Id == seeded.Event).Select(value => value.ActorId).SingleAsync();
                    (await writer.Actors.SingleAsync(value => value.Id == actorId)).IsSuspended = true;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(change));
            }
            await writer.SaveChangesAsync();
        }
        await using var reader = Open(PrimaryDatabaseRole.Runtime, seeded.Tenant);
        await Assert.That(await ReadAsync(reader)).IsNull();
    }
}
