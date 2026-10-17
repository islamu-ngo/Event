using System.Data;
using System.Security.Claims;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Users.Handlers.Queries;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class HumanEventAssignmentNativeSourceFenceTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task AssignmentPhantomCommittedAfterSnapshotCannotBeAcceptedByEpochAcquisition()
    {
        var rows = await SeedAsync();
        await using var context = Open(rows.Tenant);
        var source = new HumanEventAssignmentSourceFence(context, await SubjectAsync(rows.User),
            PolicySourceSetPurpose.RuntimeTenant, rows.Tenant, [rows.Event], fixture.NativeQueueEnrollment);
        var prepared = await source.ObserveAsync();
        await Assert.That(prepared.Assignments.Length).IsEqualTo(0);
        var sets = new PolicySourceSetFence(context, PolicySourceSetPurpose.RuntimeTenant, fixture.NativeQueueEnrollment);
        await using (var owner = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var setLease = await sets.AcquireAsync();
            await using (var writer = fixture.CreateDbContext())
            {
                writer.EventRoleAssignments.Add(EventRoleAssignment.Create(rows.Tenant, rows.Event, rows.User,
                    (int)RoleEnum.EventManager, EventRoleAssignmentStatus.Active,
                    new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc), null, rows.User));
                await writer.SaveChangesAsync();
            }
            var error = await Assert.ThrowsAsync<PostgresException>(() => source.AcquireAsync(setLease));
            await Assert.That(error!.SqlState).IsEqualTo("40001");
            await owner.RollbackAsync();
        }
        // A new observation stands for whole-attempt reprepare, never old decision replay.
        await Assert.That((await source.ObserveAsync()).Assignments.Length).IsEqualTo(1);
        await using var fresh = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var held = await source.AcquireAsync(await sets.AcquireAsync());
        await Assert.That(held.Facts.Assignments.Length).IsEqualTo(1);
        await fresh.RollbackAsync();
    }

    [Test]
    public async Task ActualContributorRowsAndEmptyAssignmentEpochAreHeldAcrossPrincipalReentry()
    {
        var rows = await SeedAsync();
        await using var enrollment = fixture.CreateDbContext();
        string events = Table(enrollment, typeof(Explore.Domain.Event));
        string runtime = enrollment.GetService<ISqlGenerationHelper>()
            .DelimitIdentifier(fixture.NativeQueueEnrollment.RuntimeLogin);
        // The source fence grants no business DML. Declare only this test's
        // organizer writer columns; all private source tables stay execute-only.
        await enrollment.Database.ExecuteSqlRawAsync(
            $"GRANT SELECT (id), UPDATE (organizer_actor_id) ON {events} TO {runtime}");
        try
        {
            await using var context = Open(rows.Tenant);
            var subject = await SubjectAsync(rows.User);
            var principal = new HumanPrincipalSourceFence(context, subject, PolicySourceSetPurpose.RuntimeTenant,
                enrollment: fixture.NativeQueueEnrollment);
            var source = new HumanEventAssignmentSourceFence(context, subject,
                PolicySourceSetPurpose.RuntimeTenant, rows.Tenant, [rows.Event], fixture.NativeQueueEnrollment);
            var sets = new PolicySourceSetFence(context, PolicySourceSetPurpose.RuntimeTenant, fixture.NativeQueueEnrollment);
            await using var governanceLock = await RelationalNamedLock.AcquireSessionAsync(
                context, $"assignment-source:{rows.Event:N}", CancellationToken.None);
            await using var owner = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var setLease = await sets.AcquireAsync();
            var principalLease = await principal.AcquireAsync(setLease);
            var held = await source.AcquireAsync(setLease);
            await principalLease.ValidateAsync();
            await using var contender = fixture.CreateDbContext();
            foreach (var (type, id) in new[] { (typeof(Explore.Domain.Event), rows.Event), (typeof(Actor), rows.Actor) })
            {
                await using var attempt = await contender.Database.BeginTransactionAsync();
                var error = await Assert.ThrowsAsync<PostgresException>(() => contender.Database.ExecuteSqlRawAsync(
                    $"SELECT id FROM {Table(contender, type)} WHERE id = {{0}} FOR UPDATE NOWAIT", id));
                await Assert.That(error!.SqlState).IsEqualTo("55P03");
                await attempt.RollbackAsync();
            }
            await using (var attempt = await contender.Database.BeginTransactionAsync())
            {
                // NOWAIT tests the exact lock used by every real writer trigger, without timing guesses.
                var error = await Assert.ThrowsAsync<PostgresException>(() => contender.Database.ExecuteSqlRawAsync(
                    $"SELECT id FROM {Epoch(contender)} WHERE id = 1 FOR UPDATE NOWAIT"));
                await Assert.That(error!.SqlState).IsEqualTo("55P03");
                await attempt.RollbackAsync();
            }
            await Assert.That(await context.Database.ExecuteSqlRawAsync(
                $"UPDATE {events} SET organizer_actor_id = NULL WHERE id = {{0}}", rows.Event)).IsEqualTo(1);
            var final = await held.ValidateAsync();
            await Assert.That(held.Facts.Events.Single().OrganizerActorId).IsEqualTo(rows.Actor);
            await Assert.That(final.Events.Single().OrganizerActorId).IsNull();
            await owner.RollbackAsync();
        }
        finally
        {
            await enrollment.Database.ExecuteSqlRawAsync(
                $"REVOKE SELECT (id), UPDATE (organizer_actor_id) ON {events} FROM {runtime}");
        }
    }

    [Test]
    [Arguments("event")]
    [Arguments("assignment")]
    [Arguments("actor")]
    [Arguments("permission")]
    [Arguments("placement")]
    public async Task RealSourceWritesParticipateInAssignmentEpoch(string contributor)
    {
        var rows = await SeedAsync();
        await using var writer = fixture.CreateDbContext();
        await using var transaction = await writer.Database.BeginTransactionAsync();
        Type type = contributor switch
        {
            "event" => typeof(Explore.Domain.Event),
            "assignment" => typeof(EventRoleAssignment),
            "actor" => typeof(Actor),
            "permission" => typeof(Permission),
            "placement" => typeof(OrganizationTenant),
            _ => throw new ArgumentOutOfRangeException(nameof(contributor))
        };
        // Even a real zero-row DML statement must participate, covering empty-range writers.
        await writer.Database.ExecuteSqlRawAsync($"DELETE FROM {Table(writer, type)} WHERE false");
        await using var contender = fixture.CreateDbContext();
        await using var attempt = await contender.Database.BeginTransactionAsync();
        var error = await Assert.ThrowsAsync<PostgresException>(() => contender.Database.ExecuteSqlRawAsync(
            $"SELECT id FROM {Epoch(contender)} WHERE id = 1 FOR UPDATE NOWAIT"));
        await Assert.That(error!.SqlState).IsEqualTo("55P03");
        await attempt.RollbackAsync();
        await transaction.RollbackAsync();
        await Assert.That(rows.Event).IsNotEqualTo(Guid.Empty);
    }

    [Test]
    [Arguments("trigger")]
    [Arguments("public")]
    public async Task RestrictedRuntimeRejectsDisabledWriterOrWidenedCapability(string drift)
    {
        var rows = await SeedAsync();
        await using var migration = fixture.CreateDbContext();
        string function = PostgresHumanEventAssignmentSourceContract.ObserveFunctionSql(migration) + "(uuid,integer,uuid,uuid[])";
        string table = Table(migration, typeof(EventRoleAssignment));
        string change = drift == "trigger"
            ? $"ALTER TABLE {table} DISABLE TRIGGER zz_human_event_assignment_source_write"
            : $"GRANT EXECUTE ON FUNCTION {function} TO PUBLIC";
        string restore = drift == "trigger"
            ? $"ALTER TABLE {table} ENABLE ALWAYS TRIGGER zz_human_event_assignment_source_write"
            : $"REVOKE EXECUTE ON FUNCTION {function} FROM PUBLIC";
        await migration.Database.ExecuteSqlRawAsync(change);
        try
        {
            await using var context = Open(rows.Tenant);
            var source = new HumanEventAssignmentSourceFence(context, await SubjectAsync(rows.User),
                PolicySourceSetPurpose.RuntimeTenant, rows.Tenant, [rows.Event], fixture.NativeQueueEnrollment);
            await Assert.ThrowsAsync<PostgresException>(() => source.ObserveAsync());
        }
        finally { await migration.Database.ExecuteSqlRawAsync(restore); }
    }

    [Test]
    public async Task RestampedChangedFunctionIsRejectedAgainstSourceDefinedBody()
    {
        var rows = await SeedAsync();
        await using var migration = fixture.CreateDbContext();
        string function = PostgresHumanEventAssignmentSourceContract.ObserveFunctionSql(migration);
        string signature = function + "(uuid,integer,uuid,uuid[])";
        string original = await migration.Database.SqlQueryRaw<string>(
            "SELECT pg_catalog.pg_get_functiondef(pg_catalog.to_regprocedure({0})) AS \"Value\"", signature).SingleAsync();
        string comment = await migration.Database.SqlQueryRaw<string>(
            "SELECT pg_catalog.obj_description(pg_catalog.to_regprocedure({0}),'pg_proc') AS \"Value\"", signature).SingleAsync();
        await migration.Database.ExecuteSqlRawAsync($"""
            CREATE OR REPLACE FUNCTION {function}(p_actor uuid,p_purpose integer,p_tenant uuid,p_events uuid[])
            RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
            AS 'BEGIN RETURN pg_catalog.jsonb_build_object(); END';
            """);
        try
        {
            await migration.Database.ExecuteSqlRawAsync($"""
                DO $restamp$ BEGIN
                    EXECUTE (SELECT pg_catalog.format('COMMENT ON FUNCTION %s IS %L', {Text(signature)},
                        'human-event-assignment-source:v1:' || pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(prosrc,'UTF8')),'hex'))
                        FROM pg_catalog.pg_proc WHERE oid = pg_catalog.to_regprocedure({Text(signature)}));
                END $restamp$;
                """);
            await using var context = Open(rows.Tenant);
            var source = new HumanEventAssignmentSourceFence(context, await SubjectAsync(rows.User),
                PolicySourceSetPurpose.RuntimeTenant, rows.Tenant, [rows.Event], fixture.NativeQueueEnrollment);
            await Assert.ThrowsAsync<PostgresException>(() => source.ObserveAsync());
        }
        finally
        {
            await migration.Database.ExecuteSqlRawAsync(original);
            await migration.Database.ExecuteSqlRawAsync($"COMMENT ON FUNCTION {signature} IS {Text(comment)}");
        }
    }

    private async Task<(Guid Tenant, Guid User, Guid Actor, Guid Event)> SeedAsync()
    {
        await using var context = fixture.CreateDbContext();
        await PostgresPolicySourceSetContract.ApplyAsync(context, fixture.NativeQueueEnrollment);
        await PostgresHumanPrincipalSourceContract.ApplyAsync(context, fixture.NativeQueueEnrollment);
        await PostgresHumanEventAssignmentSourceContract.ApplyAsync(context, fixture.NativeQueueEnrollment);
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = "Assignment source",
            Slug = $"assignment-{Guid.CreateVersion7():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            CreatedAt = DateTime.UtcNow,
            Pii = new UserPii { Email = string.Empty, FirstName = "Assignment", LastName = "Source" }
        };
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            Pii = new ActorPii { DisplayName = "Assignment source" },
            CreatedAt = DateTime.UtcNow
        };
        var parent = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            Title = "Assignment source",
            ActorId = actor.Id,
            Actor = actor,
            OrganizerActorId = actor.Id,
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventFormatId = (int)EventFormatEnum.Local,
            EventFormat = null!,
            EventStatus = null!,
            CreatedAt = DateTime.UtcNow
        };
        context.AddRange(tenant, user, actor, parent);
        await context.SaveChangesAsync();
        return (tenant.Id, user.Id, actor.Id, parent.Id);
    }

    private async Task<HumanCommitSubject> SubjectAsync(Guid id) =>
        await HumanCommitSubject.ResolveAsync(
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], "fixture")),
            new ResolveCurrentUserIdByIdentityRequestHandler(new ExternalLoginAuthenticationRepository(
                new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global)))), default)
        ?? throw new InvalidOperationException("A resolved human is required.");

    private ExploreDbContext Open(Guid tenant)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime));
        options.AddInterceptors(PostgresTenantSessionInterceptor.Instance);
        return new ExploreDbContext(options.Options) { TenantContext = new TenantIdentity(tenant) };
    }

    private static string Table(ExploreDbContext context, Type type)
    {
        var entity = context.Model.FindEntityType(type)!;
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
    }

    private static string Epoch(ExploreDbContext context) => context.GetService<ISqlGenerationHelper>()
        .DelimitIdentifier("human_event_assignment_source_epoch", context.Model.FindEntityType(typeof(User))!.GetSchema());
    private sealed record TenantIdentity(Guid TenantId) : ITenantContext;
    private static string Text(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}
