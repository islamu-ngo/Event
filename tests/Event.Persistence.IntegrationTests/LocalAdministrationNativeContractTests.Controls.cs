using System.Data;
using System.Text.Json.Nodes;
using Explore.Application.Contracts.Identity;
using Explore.Domain;
using Explore.Persistence.Identity;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Native = Explore.Persistence.Security.PostgresLocalAdministrationContract;

namespace Event.Persistence.IntegrationTests;

public sealed partial class LocalAdministrationNativeContractTests
{
    [Test]
    [Arguments("Status")]
    [Arguments("List")]
    public async Task ReadRequestsCannotAddAnUnrequestedCredentialSubject(string producer)
    {
        var source = await SeedAsync();
        var request = new Native.Request(producer, LocalAdministrationStage.Disclose, source.Actor, source.Tenant,
            producer == "Status" ? Guid.CreateVersion7() : null, LocalSubjectId: Guid.CreateVersion7());
        await using var caller = Open(PrimaryDatabaseRole.Runtime, source.Tenant);
        await caller.Database.OpenConnectionAsync();
        await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var rejected = await Assert.ThrowsAsync<PostgresException>(() =>
            Native.ReadAsync(caller, fixture.NativeQueueEnrollment, request, CancellationToken.None));
        await Assert.That(rejected!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        await owner.RollbackAsync();
    }

    [Test]
    [Arguments("actor")]
    [Arguments("operation")]
    [Arguments("producer")]
    public async Task EmptyPreimageCannotTransferToAnotherOriginalRequest(string component)
    {
        var source = await SeedAsync();
        var request = new Native.Request("Status", LocalAdministrationStage.Disclose, source.Actor, source.Tenant, Guid.CreateVersion7());
        await using var caller = Open(PrimaryDatabaseRole.Runtime, source.Tenant);
        await caller.Database.OpenConnectionAsync();
        await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        string before = await Native.ReadAsync(caller, fixture.NativeQueueEnrollment, request, CancellationToken.None);
        var substituted = component switch
        {
            "actor" => request with { ActorId = Guid.CreateVersion7() },
            "operation" => request with { OperationId = Guid.CreateVersion7() },
            _ => request with { Producer = "Reconcile", Stage = LocalAdministrationStage.ReadProvisioning }
        };
        var rejected = await Assert.ThrowsAsync<PostgresException>(() =>
            Native.AcquireAsync(caller, fixture.NativeQueueEnrollment, substituted, before, CancellationToken.None));
        await Assert.That(rejected!.SqlState).IsEqualTo(PostgresErrorCodes.SerializationFailure);
        await owner.RollbackAsync();
    }

    [Test]
    [Arguments("LocalIdentityUser")]
    [Arguments("LocalIdentityCredentialOperation")]
    [Arguments("CredentialToken")]
    [Arguments("User")]
    [Arguments("UserPii")]
    [Arguments("Actor")]
    [Arguments("ActorPii")]
    [Arguments("UserExternalLogin")]
    public async Task EveryLifecycleAndPiiWriterInvalidatesAnEarlierSerializableImage(string relation)
    {
        var source = await SeedAsync();
        var request = Create(source);
        await using var caller = Open(PrimaryDatabaseRole.Runtime, source.Tenant);
        await caller.Database.OpenConnectionAsync();
        await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        string observed = await Native.ReadAsync(caller, fixture.NativeQueueEnrollment, request, CancellationToken.None);
        await using (var writer = fixture.CreateDbContext())
        {
            var names = new HumanPrincipalSourceSql(writer);
            (Type Type, string Key) target = relation switch
            {
                "LocalIdentityUser" => (typeof(LocalIdentityUser), "Id"),
                "LocalIdentityCredentialOperation" => (typeof(LocalIdentityCredentialOperation), "Id"),
                "CredentialToken" => (typeof(IdentityUserToken<Guid>), "UserId"),
                "User" => (typeof(User), "Id"),
                "UserPii" => (typeof(UserPii), "UserId"),
                "Actor" => (typeof(Actor), "Id"),
                "ActorPii" => (typeof(ActorPii), "ActorId"),
                _ => (typeof(UserExternalLogin), "Id")
            };
            // Even an absent-row statement protects uniqueness, future binding
            // contributors and the list's page/count ranges, not only extant rows.
            await writer.Database.ExecuteSqlRawAsync(
                $"UPDATE {names.Table(target.Type)} SET {names.Column(target.Type, target.Key)} = {names.Column(target.Type, target.Key)} WHERE false");
        }
        var failure = await Assert.ThrowsAsync<PostgresException>(() =>
            Native.AcquireAsync(caller, fixture.NativeQueueEnrollment, request, observed, CancellationToken.None));
        await Assert.That(failure!.SqlState).IsEqualTo(PostgresErrorCodes.SerializationFailure);
        await owner.RollbackAsync();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExclusionRemainsHeldAfterCompletionUntilPhysicalSettlement(bool commit)
    {
        var source = await SeedAsync();
        var request = new Native.Request("Status", LocalAdministrationStage.Disclose, source.Actor, source.Tenant, Guid.CreateVersion7());
        await using var caller = Open(PrimaryDatabaseRole.Runtime, source.Tenant);
        await caller.Database.OpenConnectionAsync();
        await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        string observed = await Native.ReadAsync(caller, fixture.NativeQueueEnrollment, request, CancellationToken.None);
        await Native.AcquireAsync(caller, fixture.NativeQueueEnrollment, request, observed, CancellationToken.None);
        await Native.CompileAsync(caller, fixture.NativeQueueEnrollment, request, new(), CancellationToken.None);
        await Native.ApplyPlanAsync(caller, fixture.NativeQueueEnrollment, request, CancellationToken.None);
        await Native.CompleteAsync(caller, fixture.NativeQueueEnrollment, request, observed, CancellationToken.None);
        await using var writer = fixture.CreateDbContext();
        string epoch = new HumanPrincipalSourceSql(writer).Name(Native.SourceTable);
        await using (var contender = await writer.Database.BeginTransactionAsync())
        {
            var blocked = await Assert.ThrowsAsync<PostgresException>(() =>
                writer.Database.ExecuteSqlRawAsync($"SELECT id FROM {epoch} WHERE id = 1 FOR UPDATE NOWAIT"));
            await Assert.That(blocked!.SqlState).IsEqualTo(PostgresErrorCodes.LockNotAvailable);
            await contender.RollbackAsync();
        }
        if (commit) await owner.CommitAsync();
        else await owner.RollbackAsync();
        await using var released = await writer.Database.BeginTransactionAsync();
        await writer.Database.ExecuteSqlRawAsync($"SELECT id FROM {epoch} WHERE id = 1 FOR UPDATE NOWAIT");
        await released.RollbackAsync();
    }

    [Test]
    [Arguments(0, 1)]
    [Arguments(1, 0)]
    [Arguments(1, 101)]
    [Arguments(int.MaxValue, 100)]
    public async Task ListRejectsUnboundedOrOverflowingPagination(int page, int size)
    {
        var source = await SeedAsync();
        var request = new Native.Request("List", LocalAdministrationStage.Disclose, source.Actor, source.Tenant,
            PageNumber: page, PageSize: size);
        await using var caller = Open(PrimaryDatabaseRole.Runtime, source.Tenant);
        await caller.Database.OpenConnectionAsync();
        await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.ThrowsAsync<PostgresException>(() => Native.ReadAsync(caller, fixture.NativeQueueEnrollment, request, CancellationToken.None));
        await owner.RollbackAsync();
    }

    [Test]
    public async Task ListUsesStableCreatedAtAndIdOrderWithFullTotal()
    {
        var source = await SeedAsync();
        await StageAsync(Create(source), Creation());
        await StageAsync(Create(source), Creation());
        var request = new Native.Request("List", LocalAdministrationStage.Disclose, source.Actor, source.Tenant, PageSize: 1);
        var page = await StageAsync(request, new());
        await using var observer = fixture.CreateDbContext();
        Guid first = await observer.Set<LocalIdentityUser>().OrderBy(user => user.CreatedAt).ThenBy(user => user.Id)
            .Select(user => user.Id).FirstAsync();
        int total = await observer.Set<LocalIdentityUser>().CountAsync();
        await Assert.That(page.Image["Data"]!["Page"]!.AsArray().Count).IsEqualTo(1);
        await Assert.That(page.Image["Data"]!["Page"]![0]!.GetValue<string>()).IsEqualTo(first.ToString("D"));
        await Assert.That(page.Image["Data"]!["TotalCount"]!.GetValue<int>()).IsEqualTo(total);
    }

    [Test]
    [Arguments("PasswordHash")]
    [Arguments("FirstName")]
    [Arguments("AccessFailedCount")]
    [Arguments("PhoneNumber")]
    public async Task CompletionRejectsAlteredUnchangedScalarsInTheDisclosedImage(string property)
    {
        var source = await SeedAsync();
        var request = Create(source);
        await using var caller = Open(PrimaryDatabaseRole.Runtime, source.Tenant);
        await caller.Database.OpenConnectionAsync();
        await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        string observed = await Native.ReadAsync(caller, fixture.NativeQueueEnrollment, request, CancellationToken.None);
        await Native.AcquireAsync(caller, fixture.NativeQueueEnrollment, request, observed, CancellationToken.None);
        await Native.CompileAsync(caller, fixture.NativeQueueEnrollment, request, Creation(), CancellationToken.None);
        var applied = JsonNode.Parse(await Native.ApplyPlanAsync(caller, fixture.NativeQueueEnrollment, request, CancellationToken.None))!;
        var changed = applied["Image"]!.DeepClone();
        if (property == "AccessFailedCount") changed["Data"]!["LocalIdentityUser"]![0]![property] = 1;
        else changed["Data"]!["LocalIdentityUser"]![0]![property] = Guid.CreateVersion7().ToString("D");
        await Assert.ThrowsAsync<PostgresException>(() =>
            Native.CompleteAsync(caller, fixture.NativeQueueEnrollment, request, changed.ToJsonString(), CancellationToken.None));
        await owner.RollbackAsync();
    }

    [Test]
    [Arguments("User")]
    [Arguments("UserPii")]
    [Arguments("ActorPii")]
    public async Task BinderRejectsPartialGraphWithoutRepair(string retainedRelation)
    {
        var source = await SeedAsync();
        var create = Create(source);
        var pending = await StageAsync(create, Creation());
        var operation = pending.Image["Data"]!["LocalIdentityCredentialOperation"]![0]!;
        Guid subject = Guid.Parse(operation["LocalSubjectId"]!.GetValue<string>());
        var bind = create with
        {
            Stage = LocalAdministrationStage.Bind,
            ExpectedOperationConcurrencyStamp = Guid.Parse(operation["ConcurrencyStamp"]!.GetValue<string>())
        };
        if (retainedRelation == "User")
        {
            await using var migration = fixture.CreateDbContext();
            migration.Users.Add(new User
            {
                Id = subject,
                CreatedAt = Instant(),
                Pii = new UserPii { Email = create.Email!, FirstName = create.FirstName!, LastName = create.LastName! }
            });
            await migration.SaveChangesAsync();
        }
        else
        {
            await StageAsync(bind, new(UserConcurrencyStamp: Guid.CreateVersion7(), ActorConcurrencyStamp: Guid.CreateVersion7()));
            await using var migration = fixture.CreateDbContext();
            if (retainedRelation == "UserPii")
                await migration.Set<UserPii>().Where(row => row.UserId == subject).ExecuteDeleteAsync();
            else
            {
                Guid personal = Guid.Parse(operation["PersonalActorId"]!.GetValue<string>());
                await migration.Set<ActorPii>().Where(row => row.ActorId == personal).ExecuteDeleteAsync();
            }
        }
        await Assert.ThrowsAsync<PostgresException>(() =>
            StageAsync(bind, new(UserConcurrencyStamp: Guid.CreateVersion7(), ActorConcurrencyStamp: Guid.CreateVersion7())));
    }

    [Test]
    [Arguments("owner-login")]
    [Arguments("public-execute")]
    [Arguments("disabled-pii-trigger")]
    [Arguments("extra-column")]
    [Arguments("ambient-select")]
    [Arguments("executor-global")]
    [Arguments("private-pii-default")]
    [Arguments("private-pii-select")]
    [Arguments("private-pii-update")]
    public async Task CatalogAndAuthorityDriftFailReadinessAndNeverRepairOnReinstall(string drift)
    {
        var source = await SeedAsync();
        await using var migration = fixture.CreateDbContext();
        var names = new HumanPrincipalSourceSql(migration);
        string read = Native.FunctionSql(migration, "read") + "(jsonb)";
        string user = names.Table(typeof(LocalIdentityUser));
        string pii = names.Table(typeof(UserPii));
        string runtime = names.Quote(fixture.NativeQueueEnrollment.RuntimeLogin);
        string global = names.Quote(fixture.NativeQueueEnrollment.GlobalLogin);
        string revision = names.Column(typeof(UserPii), "_birthDateRevision");
        string birthDate = names.Column(typeof(UserPii), "_dateOfBirth");
        (string Change, string Restore) statements = drift switch
        {
            "owner-login" => ($"ALTER ROLE {Native.OwnerRole} LOGIN", $"ALTER ROLE {Native.OwnerRole} NOLOGIN"),
            "public-execute" => ($"GRANT EXECUTE ON FUNCTION {read} TO PUBLIC", $"REVOKE EXECUTE ON FUNCTION {read} FROM PUBLIC"),
            "disabled-pii-trigger" => ($"ALTER TABLE {pii} DISABLE TRIGGER local_administration_changed",
                $"ALTER TABLE {pii} ENABLE ALWAYS TRIGGER local_administration_changed"),
            "extra-column" => ($"ALTER TABLE {user} ADD COLUMN native_unreviewed text", $"ALTER TABLE {user} DROP COLUMN native_unreviewed"),
            "ambient-select" => ($"GRANT SELECT ON {user} TO {runtime}", $"REVOKE SELECT ON {user} FROM {runtime}"),
            "private-pii-default" => ($"ALTER TABLE {pii} ALTER COLUMN {revision} SET DEFAULT 1",
                $"ALTER TABLE {pii} ALTER COLUMN {revision} SET DEFAULT 0"),
            "private-pii-select" => ($"GRANT SELECT ({birthDate}) ON {pii} TO {Native.OwnerRole}",
                $"REVOKE SELECT ({birthDate}) ON {pii} FROM {Native.OwnerRole}"),
            "private-pii-update" => ($"GRANT UPDATE ({birthDate}) ON {pii} TO {Native.OwnerRole}",
                $"REVOKE UPDATE ({birthDate}) ON {pii} FROM {Native.OwnerRole}"),
            _ => ($"GRANT {Native.RuntimeRole} TO {global} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE",
                $"REVOKE {Native.RuntimeRole} FROM {global}")
        };
        await using var caller = Open(PrimaryDatabaseRole.Runtime, source.Tenant);
        await caller.Database.OpenConnectionAsync();
        await Native.RequireReadyAsync(caller, fixture.NativeQueueEnrollment);
        await migration.Database.ExecuteSqlRawAsync(statements.Change);
        try
        {
            await Assert.ThrowsAsync<PostgresException>(() => Native.RequireReadyAsync(caller, fixture.NativeQueueEnrollment));
            await Assert.ThrowsAsync<PostgresException>(() => Native.ApplyAsync(migration, fixture.NativeQueueEnrollment));
        }
        finally { await migration.Database.ExecuteSqlRawAsync(statements.Restore); }
    }

    [Test]
    public async Task RestampedBodyCannotForgeTheSourceDefinedImplementation()
    {
        var source = await SeedAsync();
        await using var migration = fixture.CreateDbContext();
        string function = Native.FunctionSql(migration, "read");
        string signature = function + "(jsonb)";
        string definition = await migration.Database.SqlQueryRaw<string>(
            "SELECT pg_catalog.pg_get_functiondef(pg_catalog.to_regprocedure({0})) AS \"Value\"", signature).SingleAsync();
        string stamp = await migration.Database.SqlQueryRaw<string>(
            "SELECT pg_catalog.obj_description(pg_catalog.to_regprocedure({0}),'pg_proc') AS \"Value\"", signature).SingleAsync();
        await migration.Database.ExecuteSqlRawAsync($"""
            CREATE OR REPLACE FUNCTION {function}(p_request jsonb) RETURNS jsonb
                LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog AS $body$
                BEGIN RETURN pg_catalog.jsonb_build_object(); END;
                $body$;
            DO $stamp$ DECLARE fingerprint text; BEGIN
                SELECT 'local-administration:v1:' || pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(prosrc,'UTF8')),'hex')
                    INTO fingerprint FROM pg_catalog.pg_proc WHERE oid = pg_catalog.to_regprocedure('{signature.Replace("'", "''", StringComparison.Ordinal)}');
                EXECUTE pg_catalog.format('COMMENT ON FUNCTION {signature} IS %L',fingerprint);
            END $stamp$;
            """);
        try
        {
            await using var caller = Open(PrimaryDatabaseRole.Runtime, source.Tenant);
            await caller.Database.OpenConnectionAsync();
            await Assert.ThrowsAsync<PostgresException>(() => Native.RequireReadyAsync(caller, fixture.NativeQueueEnrollment));
        }
        finally
        {
            await migration.Database.ExecuteSqlRawAsync(definition);
            await migration.Database.ExecuteSqlRawAsync($"COMMENT ON FUNCTION {signature} IS '{stamp.Replace("'", "''", StringComparison.Ordinal)}'");
        }
    }
}
