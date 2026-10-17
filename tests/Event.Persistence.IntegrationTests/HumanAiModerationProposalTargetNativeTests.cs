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
public sealed class HumanAiModerationProposalTargetNativeTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task ExactTargetUsesOriginalTenantOwnerWithoutOpeningOrdinaryEventReads()
    {
        var rows = await SeedAsync();
        await using var context = Open(rows.Selected);
        var subject = await SubjectAsync(rows.User);
        var source = new HumanAiModerationProposalTargetSource(context, subject, rows.Event, fixture.NativeQueueEnrollment);
        var deniedRead = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Events.AnyAsync(value => value.Id == rows.Event));
        await Assert.That(deniedRead!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Task.FromResult(
            new HumanEventAssignmentSourceFence(context, subject, PolicySourceSetPurpose.RuntimeTenant,
                rows.Target, [rows.Event], fixture.NativeQueueEnrollment)));
        var observed = await source.ObserveAsync();
        await Assert.That(observed!.TenantId).IsEqualTo(rows.Target);
        await Assert.That(observed.Events.Single().Id).IsEqualTo(rows.Event);
        await Assert.That(observed.Assignments.Single().TenantId).IsEqualTo(rows.Target);
        var principal = new HumanPrincipalSourceFence(context, subject, PolicySourceSetPurpose.RuntimeTenant,
            enrollment: fixture.NativeQueueEnrollment);
        var sets = new PolicySourceSetFence(context, PolicySourceSetPurpose.RuntimeTenant, fixture.NativeQueueEnrollment);
        await using var owner = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var exclusion = await sets.AcquireAsync();
        var held = await source.AcquireAsync(exclusion, await principal.AcquireAsync(exclusion));
        await Assert.That((await held.ValidateAsync()).TenantId).IsEqualTo(rows.Target);
        await Assert.That(context.TenantFilterTenantId).IsEqualTo(rows.Selected);
        await owner.CreateSavepointAsync("ordinary_event_read");
        var deniedHeldRead = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Events.AnyAsync(value => value.Id == rows.Event));
        await Assert.That(deniedHeldRead!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        await owner.RollbackToSavepointAsync("ordinary_event_read");
        await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(owner);
        await using var contender = fixture.CreateDbContext();
        await using var attempt = await contender.Database.BeginTransactionAsync();
        await using var lockCommand = contender.Database.GetDbConnection().CreateCommand();
        lockCommand.Transaction = attempt.GetDbTransaction();
        lockCommand.CommandText = $"SELECT id FROM {Table(contender, typeof(Explore.Domain.Event))} WHERE id = @event FOR UPDATE NOWAIT";
        var target = lockCommand.CreateParameter();
        target.ParameterName = "event";
        target.DbType = DbType.Guid;
        target.Value = rows.Event;
        lockCommand.Parameters.Add(target);
        var blocked = await Assert.ThrowsAsync<PostgresException>(() => lockCommand.ExecuteNonQueryAsync());
        await Assert.That(blocked!.SqlState).IsEqualTo("55P03");
        await attempt.RollbackAsync();
        await owner.RollbackAsync();
    }

    [Test]
    [Arguments("ordinary")]
    [Arguments("admin_revoked")]
    [Arguments("target_deleted")]
    public async Task MissingCurrentAdministratorOrTargetCannotBeDiscovered(string change)
    {
        var rows = await SeedAsync();
        await using (var writer = fixture.CreateDbContext())
        {
            if (change is "ordinary" or "admin_revoked")
                await writer.PlatformUserRoles.Where(value => value.UserId == rows.User).ExecuteDeleteAsync();
            else
                await writer.Events.Where(value => value.Id == rows.Event)
                    .ExecuteUpdateAsync(update => update.SetProperty(value => value.IsDeleted, true));
        }
        await using var context = Open(rows.Selected);
        var source = new HumanAiModerationProposalTargetSource(
            context, await SubjectAsync(rows.User), rows.Event, fixture.NativeQueueEnrollment);
        if (change == "target_deleted")
            await Assert.That(await source.ObserveAsync()).IsNull();
        else
            await Assert.ThrowsAsync<PostgresException>(() => source.ObserveAsync());
    }

    [Test]
    [Arguments("public_execute")]
    [Arguments("writer_disabled")]
    public async Task NativeCapabilityAndSharedWriterDriftRejectDiscovery(string drift)
    {
        var rows = await SeedAsync();
        await using var migration = fixture.CreateDbContext();
        string routine = PostgresHumanAiModerationProposalTargetContract.ObserveFunctionSql(migration) + "(uuid,uuid,uuid)";
        string table = Table(migration, typeof(EventRoleAssignment));
        string change = drift == "public_execute" ? $"GRANT EXECUTE ON FUNCTION {routine} TO PUBLIC"
            : $"ALTER TABLE {table} DISABLE TRIGGER zz_human_event_assignment_source_write";
        string restore = drift == "public_execute" ? $"REVOKE EXECUTE ON FUNCTION {routine} FROM PUBLIC"
            : $"ALTER TABLE {table} ENABLE ALWAYS TRIGGER zz_human_event_assignment_source_write";
        await migration.Database.ExecuteSqlRawAsync(change);
        try
        {
            await using var context = Open(rows.Selected);
            var source = new HumanAiModerationProposalTargetSource(
                context, await SubjectAsync(rows.User), rows.Event, fixture.NativeQueueEnrollment);
            await Assert.ThrowsAsync<PostgresException>(() => source.ObserveAsync());
        }
        finally { await migration.Database.ExecuteSqlRawAsync(restore); }
    }

    [Test]
    public async Task TargetDeletionAfterOwnerSnapshotCannotBeAcceptedByAssignmentEpoch()
    {
        var rows = await SeedAsync();
        await using var context = Open(rows.Selected);
        var subject = await SubjectAsync(rows.User);
        var source = new HumanAiModerationProposalTargetSource(context, subject, rows.Event, fixture.NativeQueueEnrollment);
        await Assert.That(await source.ObserveAsync()).IsNotNull();
        var principal = new HumanPrincipalSourceFence(context, subject, PolicySourceSetPurpose.RuntimeTenant,
            enrollment: fixture.NativeQueueEnrollment);
        var sets = new PolicySourceSetFence(context, PolicySourceSetPurpose.RuntimeTenant, fixture.NativeQueueEnrollment);
        await using var owner = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var exclusion = await sets.AcquireAsync();
        await using (var revoke = fixture.CreateDbContext())
            await revoke.Events.Where(value => value.Id == rows.Event)
                .ExecuteUpdateAsync(update => update.SetProperty(value => value.IsDeleted, true));
        var human = await principal.AcquireAsync(exclusion);
        var conflict = await Assert.ThrowsAsync<PostgresException>(() => source.AcquireAsync(exclusion, human));
        await Assert.That(conflict!.SqlState).IsEqualTo("40001");
        await owner.RollbackAsync();
        await Assert.That(await source.ObserveAsync()).IsNull();
    }

    [Test]
    public async Task CurrentCatalogRejectsRestampedBodyAfterSerializableSnapshotWasEstablished()
    {
        var rows = await SeedAsync();
        await using var context = Open(rows.Selected);
        var subject = await SubjectAsync(rows.User);
        var source = new HumanAiModerationProposalTargetSource(context, subject, rows.Event, fixture.NativeQueueEnrollment);
        await source.ObserveAsync();
        var principal = new HumanPrincipalSourceFence(context, subject, PolicySourceSetPurpose.RuntimeTenant,
            enrollment: fixture.NativeQueueEnrollment);
        var sets = new PolicySourceSetFence(context, PolicySourceSetPurpose.RuntimeTenant, fixture.NativeQueueEnrollment);
        await using var owner = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var exclusion = await sets.AcquireAsync();
        var human = await principal.AcquireAsync(exclusion);
        await using var migration = fixture.CreateDbContext();
        string function = PostgresHumanAiModerationProposalTargetContract.ObserveFunctionSql(migration);
        string signature = function + "(uuid,uuid,uuid)";
        string original = await migration.Database.SqlQueryRaw<string>(
            "SELECT pg_catalog.pg_get_functiondef(pg_catalog.to_regprocedure({0})) AS \"Value\"", signature).SingleAsync();
        string comment = await migration.Database.SqlQueryRaw<string>(
            "SELECT pg_catalog.obj_description(pg_catalog.to_regprocedure({0}),'pg_proc') AS \"Value\"", signature).SingleAsync();
        await migration.Database.OpenConnectionAsync();
        await using var mutation = migration.Database.GetDbConnection().CreateCommand();
        mutation.CommandText = $"""
            CREATE OR REPLACE FUNCTION {function}(p_actor uuid,p_selected uuid,p_event uuid)
            RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
            AS 'BEGIN RETURN pg_catalog.jsonb_build_object(); END';
            """;
        await mutation.ExecuteNonQueryAsync();
        try
        {
            mutation.CommandText = $"""
                DO $restamp$ BEGIN
                    EXECUTE (SELECT pg_catalog.format('COMMENT ON FUNCTION %s IS %L', {Text(signature)},
                        'human-ai-moderation-proposal-target:v1:' ||
                            pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(prosrc,'UTF8')),'hex'))
                        FROM pg_catalog.pg_proc WHERE oid = pg_catalog.to_regprocedure({Text(signature)}));
                END $restamp$;
                """;
            await mutation.ExecuteNonQueryAsync();
            await Assert.ThrowsAsync<PostgresException>(() => source.AcquireAsync(exclusion, human));
            await owner.RollbackAsync();
        }
        finally
        {
            mutation.CommandText = original;
            await mutation.ExecuteNonQueryAsync();
            mutation.CommandText = $"COMMENT ON FUNCTION {signature} IS {Text(comment)}";
            await mutation.ExecuteNonQueryAsync();
        }
    }

    private async Task<(Guid Selected, Guid Target, Guid User, Guid Event)> SeedAsync()
    {
        await using var context = fixture.CreateDbContext();
        await PostgresPolicySourceSetContract.ApplyAsync(context, fixture.NativeQueueEnrollment);
        await PostgresHumanPrincipalSourceContract.ApplyAsync(context, fixture.NativeQueueEnrollment);
        await PostgresHumanEventAssignmentSourceContract.ApplyAsync(context, fixture.NativeQueueEnrollment);
        Tenant Tenant(string name) => new()
        {
            Id = Guid.CreateVersion7(),
            FullName = name,
            Slug = $"ai-moderation-{Guid.CreateVersion7():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        var selected = Tenant("Conversation owner");
        var target = Tenant("Moderation target");
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            CreatedAt = DateTime.UtcNow,
            Pii = new UserPii { Email = string.Empty, FirstName = "Moderation", LastName = "Source" }
        };
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            Pii = new ActorPii { DisplayName = "Moderation source" },
            CreatedAt = DateTime.UtcNow
        };
        var parent = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            TenantId = target.Id,
            Tenant = target,
            Title = "Exact moderation target",
            ActorId = actor.Id,
            Actor = actor,
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventFormatId = (int)EventFormatEnum.Local,
            EventFormat = null!,
            EventStatus = null!,
            CreatedAt = DateTime.UtcNow
        };
        int role = await context.Roles.Where(value => value.MasterCode == "platform.admin"
            && value.RoleScopeId == (int)RoleScopeEnum.Platform).Select(value => value.Id).SingleAsync();
        context.AddRange(selected, target, user, actor, parent, new PlatformUserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            User = user,
            RoleId = role,
            Role = null!,
            GrantedAt = DateTime.UtcNow
        }, EventRoleAssignment.Create(target.Id, parent.Id, user.Id, (int)RoleEnum.EventManager,
            EventRoleAssignmentStatus.Active, DateTime.UtcNow.AddDays(-1), null, user.Id));
        await context.SaveChangesAsync();
        return (selected.Id, target.Id, user.Id, parent.Id);
    }

    private async Task<HumanCommitSubject> SubjectAsync(Guid user) => await HumanCommitSubject.ResolveAsync(
        new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.ToString())], "fixture")),
        new ResolveCurrentUserIdByIdentityRequestHandler(new ExternalLoginAuthenticationRepository(
            new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global)))), default)
        ?? throw new InvalidOperationException("The original resolved human is required.");

    private ExploreDbContext Open(Guid tenant)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime));
        options.UseNpgsql(provider => provider.ExecutionStrategy(
            dependencies => new NonRetryingExecutionStrategy(dependencies)));
        options.AddInterceptors(PostgresTenantSessionInterceptor.Instance);
        return new ExploreDbContext(options.Options) { TenantContext = new TenantIdentity(tenant) };
    }

    private static string Table(ExploreDbContext context, Type type)
    {
        var entity = context.Model.FindEntityType(type)!;
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
    }

    private sealed record TenantIdentity(Guid TenantId) : ITenantContext;
    private static string Text(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}
