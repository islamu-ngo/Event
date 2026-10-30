using System.Data;
using System.Security.Cryptography;
using Explore.Application.Configuration;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Domain;
using Explore.Domain.Ai;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Privacy.ErasureAuthority.Repositories;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed partial class PostgresAiActorContextReadContractTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task WorkerPrivacyFenceReadsOnlyOriginalSubjectWithoutPrivateTableGrants(bool fenced)
    {
        var seed = await SeedAsync();
        await using var grants = await GrantOwnerGraphAsync();
        Guid id = Guid.CreateVersion7();
        if (fenced)
        {
            string schema = $"ai_erasure_{Guid.CreateVersion7():N}";
            DbContextOptions<CoLocatedPrivacyErasureAuthorityDbContext> AuthorityOptions(bool migration)
            {
                var builder = TestDbContextOptions.Create<CoLocatedPrivacyErasureAuthorityDbContext>();
                var setup = fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator);
                // Migration composes the canonical model for SQL schema rewriting.
                // Execution maps the installed schema but retains the actual setup login.
                var options = migration ? setup : fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime)
                    with
                { Username = setup.Username, Password = setup.Password };
                PrimaryDatabaseProviderComposition.ConfigureCoLocatedPrivacyErasureAuthority(builder,
                    options with { Schema = schema });
                return builder.Options;
            }
            await using (var migration = new CoLocatedPrivacyErasureAuthorityDbContext(AuthorityOptions(true)))
                await migration.Database.MigrateAsync();
            // Setup appends through the real authority under its Migrator credential;
            // the AI Runtime credential receives no authority schema or table access.
            await using var authority = new CoLocatedPrivacyErasureAuthorityDbContext(AuthorityOptions(false));
            var retained = new CoLocatedPostgresPrivacyErasureAuthorityRepository(
                authority, TimeProvider.System, Options.Create(new PrivacyErasureOptions()));
            var fact = await retained.AppendAsync(PrivacyErasureRequest.Create(
                Guid.CreateVersion7(), PrivacyErasureSubjectKind.User, seed.User,
                PrivacyErasureReasonCode.AccountDeletion, new PrivacyErasureOptions().CurrentPolicyVersion));
            await using var mirror = fixture.CreateDbContext();
            mirror.PrivacyErasureSagas.Add(PrivacyErasureSaga.Start(fact, fact.AuthoritySequence,
                RandomNumberGenerator.GetBytes(32), fact.RecordedAtUtc.AddHours(1), fact.RecordedAtUtc));
            await mirror.SaveChangesAsync();
        }
        await using (var migration = fixture.CreateDbContext())
        {
            migration.Add(new AiConversation
            {
                Id = id,
                TenantId = seed.Tenant,
                UserId = seed.User,
                ActorId = seed.Personal,
                CreatedAt = DateTime.UtcNow,
                ConcurrencyStamp = Guid.CreateVersion7()
            });
            await migration.SaveChangesAsync();
        }
        await using var runtime = Open(PrimaryDatabaseRole.Runtime, seed.Tenant);
        await runtime.Database.OpenConnectionAsync();
        var repository = new AiConversationRepository(runtime, fixture.NativeQueueEnrollment);
        await using (var owner = await runtime.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var conversation = await repository.GetByIdForUpdateAsync(id, seed.Personal, default);
            await Assert.That(conversation).IsNotNull();
            await Assert.That(await repository.IsPrivacyFencedAsync(conversation!, default)).IsEqualTo(fenced);
            await Assert.That(runtime.Database.CurrentTransaction).IsSameReferenceAs(owner);
            await owner.RollbackAsync();
        }
        var denied = await Assert.ThrowsAsync<PostgresException>(() =>
            runtime.PrivacyErasureSagas.AsNoTracking().AnyAsync(row => row.SubjectId == seed.User));
        await Assert.That(denied!.SqlState).IsEqualTo("42501");
    }

    [Test]
    [Arguments("missing")]
    [Arguments("subject")]
    [Arguments("tenant")]
    [Arguments("actor")]
    [Arguments("replacement")]
    public async Task WorkerPrivacyFenceRejectsUnacquiredOrChangedGraphOwner(string changed)
    {
        var seed = await SeedAsync();
        await using var grants = await GrantOwnerGraphAsync();
        Guid id = Guid.CreateVersion7();
        await using (var migration = fixture.CreateDbContext())
        {
            migration.Add(new AiConversation
            {
                Id = id,
                TenantId = seed.Tenant,
                UserId = seed.User,
                ActorId = seed.Personal,
                CreatedAt = DateTime.UtcNow,
                ConcurrencyStamp = Guid.CreateVersion7()
            });
            await migration.SaveChangesAsync();
        }
        await using var runtime = Open(PrimaryDatabaseRole.Runtime, seed.Tenant);
        await runtime.Database.OpenConnectionAsync();
        var repository = new AiConversationRepository(runtime, fixture.NativeQueueEnrollment);
        await using var first = await runtime.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var conversation = changed == "missing"
            ? await repository.GetByIdWithDetailsAsync(id, default)
            : await repository.GetByIdForUpdateAsync(id, seed.Personal, default);
        if (changed == "subject") conversation!.UserId = Guid.CreateVersion7();
        if (changed == "tenant") conversation!.TenantId = Guid.CreateVersion7();
        if (changed == "actor") conversation!.ActorId = Guid.CreateVersion7();
        if (changed == "replacement")
        {
            await first.RollbackAsync();
            await first.DisposeAsync();
        }
        await using var replacement = changed == "replacement"
            ? await runtime.Database.BeginTransactionAsync(IsolationLevel.Serializable) : null;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.IsPrivacyFencedAsync(conversation!, default));
        if (replacement is not null) await replacement.RollbackAsync();
        else await first.RollbackAsync();
    }

    [Test]
    [Arguments("missing")]
    [Arguments("body")]
    [Arguments("acl")]
    [Arguments("role")]
    [Arguments("source")]
    public async Task WorkerPrivacyFenceRejectsUnavailableOrDriftedNativeAuthority(string changed)
    {
        var seed = await SeedAsync();
        await using var grants = await GrantOwnerGraphAsync();
        await using var runtime = Open(PrimaryDatabaseRole.Runtime, seed.Tenant);
        await runtime.Database.OpenConnectionAsync();
        await using var owner = await runtime.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var repository = new AiConversationRepository(runtime, fixture.NativeQueueEnrollment);
        var conversation = new AiConversation
        {
            Id = Guid.CreateVersion7(),
            TenantId = seed.Tenant,
            UserId = seed.User,
            ActorId = seed.Personal,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        await repository.Create(conversation);
        // Acquire the existing graph before source drift; the read must attest again.
        conversation = (await repository.GetByIdForUpdateAsync(conversation.Id, seed.Personal, default))!;
        await using var migration = fixture.CreateDbContext();
        var names = new HumanPrincipalSourceSql(migration);
        string function = PostgresAiConversationOwnerFenceContract.PrivacyFunctionSql(migration);
        string signature = function + "(uuid,uuid)";
        await migration.Database.OpenConnectionAsync();
        await using var capture = migration.Database.GetDbConnection().CreateCommand();
        capture.CommandText = "SELECT pg_catalog.pg_get_functiondef(pg_catalog.to_regprocedure(@signature))";
        capture.Parameters.Add(new NpgsqlParameter("signature", signature));
        string originalBody = (string)(await capture.ExecuteScalarAsync())!;
        string change = changed switch
        {
            "missing" => $"DROP FUNCTION {signature}",
            "body" => $"""
                CREATE OR REPLACE FUNCTION {function}(p_tenant uuid,p_principal uuid)
                    RETURNS boolean LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                    AS $drift$ BEGIN RETURN false; END; $drift$
                """,
            "acl" => $"GRANT EXECUTE ON FUNCTION {signature} TO PUBLIC",
            "role" => $"ALTER ROLE {PostgresAiConversationOwnerFenceContract.OwnerRole} INHERIT",
            "source" => $"REVOKE SELECT (subject_id) ON {names.Table(typeof(PrivacyErasureSaga))} FROM {PostgresAiConversationOwnerFenceContract.OwnerRole}",
            _ => throw new ArgumentOutOfRangeException(nameof(changed))
        };
        try
        {
            await migration.Database.ExecuteSqlRawAsync(change);
            await Assert.ThrowsAsync<PostgresException>(() =>
                repository.IsPrivacyFencedAsync(conversation, default));
        }
        finally
        {
            await owner.RollbackAsync();
            string restore = changed switch
            {
                "missing" => $"""
                    SET ROLE {PostgresAiConversationOwnerFenceContract.OwnerRole};
                    {originalBody};
                    REVOKE ALL ON FUNCTION {signature} FROM PUBLIC;
                    GRANT EXECUTE ON FUNCTION {signature} TO {PostgresAiConversationOwnerFenceContract.RuntimeRole};
                    RESET ROLE;
                    """,
                "body" => originalBody,
                "acl" => $"REVOKE EXECUTE ON FUNCTION {signature} FROM PUBLIC",
                "role" => $"ALTER ROLE {PostgresAiConversationOwnerFenceContract.OwnerRole} NOINHERIT",
                "source" => $"GRANT SELECT (subject_id) ON {names.Table(typeof(PrivacyErasureSaga))} TO {PostgresAiConversationOwnerFenceContract.OwnerRole}",
                _ => throw new ArgumentOutOfRangeException(nameof(changed))
            };
            if (changed == "missing")
                await migration.Database.ExecuteSqlRawAsync(
                    $"GRANT CREATE ON SCHEMA {names.Quote(names.Schema)} TO {PostgresAiConversationOwnerFenceContract.OwnerRole}");
            try
            {
                await migration.Database.ExecuteSqlRawAsync(restore);
            }
            finally
            {
                if (changed == "missing")
                    await migration.Database.ExecuteSqlRawAsync(
                        $"REVOKE CREATE ON SCHEMA {names.Quote(names.Schema)} FROM {PostgresAiConversationOwnerFenceContract.OwnerRole}");
            }
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OwnerFenceEventDraftRequiresOrganizerEligibilityWithoutRestrictingConversationContext(bool organization)
    {
        var seed = await SeedAsync();
        await using (var migration = fixture.CreateDbContext())
        {
            if (organization)
                await migration.Set<OrganizationTenant>().Where(row => row.Id == seed.OrganizationPlacement)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsOrganizerEligible, false));
            else
                await migration.Set<GroupTenant>().Where(row => row.Id == seed.GroupPlacement)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsOrganizerEligible, false));
        }
        await using var runtime = Open(PrimaryDatabaseRole.Runtime, seed.Tenant);
        await runtime.Database.OpenConnectionAsync();
        await using var owner = await runtime.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        Guid actor = organization ? seed.OrganizationActor : seed.GroupActor;
        await Assert.That(await PostgresAiConversationOwnerFenceContract.FenceAsync(runtime,
            fixture.NativeQueueEnrollment, seed.Tenant, seed.User, actor, actor, false, default)).IsTrue();
        await Assert.That(await PostgresAiConversationOwnerFenceContract.FenceAsync(runtime,
            fixture.NativeQueueEnrollment, seed.Tenant, seed.User, actor, actor, false, default,
            requireOrganizer: true)).IsFalse();
        await owner.RollbackAsync();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OwnerFenceTrackedForeignKeysCannotBorrowAnotherOwner(bool actor)
    {
        var seed = await SeedAsync();
        var foreign = await SeedAsync();
        await using var grants = await GrantOwnerGraphAsync();
        var conversation = new AiConversation
        {
            Id = Guid.CreateVersion7(),
            TenantId = seed.Tenant,
            UserId = seed.User,
            ActorId = seed.Personal,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        await using var runtime = Open(PrimaryDatabaseRole.Runtime, seed.Tenant);
        await runtime.Database.OpenConnectionAsync();
        await using var owner = await runtime.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await new AiConversationRepository(runtime, fixture.NativeQueueEnrollment).Create(conversation);
        if (actor) conversation.ActorId = foreign.Personal;
        else conversation.UserId = foreign.User;
        var denied = await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.SaveChangesAsync());
        await Assert.That(denied!.Message).IsEqualTo("tracked_tenant_authority_mismatch");
        await owner.RollbackAsync();
        await using var verification = fixture.CreateDbContext();
        await Assert.That(await verification.Set<AiConversation>().AnyAsync(row => row.Id == conversation.Id)).IsFalse();
    }

    [Test]
    public async Task OwnerFenceTrackedForeignKeysCannotReuseAnEndedTransaction()
    {
        var seed = await SeedAsync();
        await using var grants = await GrantOwnerGraphAsync();
        var conversation = new AiConversation
        {
            Id = Guid.CreateVersion7(),
            TenantId = seed.Tenant,
            UserId = seed.User,
            ActorId = seed.Personal,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        await using var runtime = Open(PrimaryDatabaseRole.Runtime, seed.Tenant);
        await runtime.Database.OpenConnectionAsync();
        await using (var first = await runtime.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await new AiConversationRepository(runtime, fixture.NativeQueueEnrollment).Create(conversation);
            await first.CommitAsync();
        }
        await using var replacement = await runtime.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        conversation.AddMessage(AiMessageRole.User, "Unadmitted replacement", seed.User, DateTime.UtcNow);
        var denied = await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.SaveChangesAsync());
        await Assert.That(denied!.Message).IsEqualTo("tracked_tenant_authority_mismatch");
        await replacement.RollbackAsync();
        await using var verification = fixture.CreateDbContext();
        await Assert.That(await verification.Set<AiMessage>()
            .AnyAsync(row => row.ConversationId == conversation.Id)).IsFalse();
    }

    [Test]
    public async Task OwnerFenceWorkerPersistsGraphWithoutHumanAdmissionOrPrivateRuntimeGrants()
    {
        var seed = await SeedAsync();
        await using var grants = await GrantOwnerGraphAsync();
        Guid id = Guid.CreateVersion7();
        await using var runtime = Open(PrimaryDatabaseRole.Runtime, seed.Tenant);
        await runtime.Database.OpenConnectionAsync();
        var repository = new AiConversationRepository(runtime, fixture.NativeQueueEnrollment);
        await using (var owner = await runtime.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await repository.Create(new AiConversation
            {
                Id = id,
                TenantId = seed.Tenant,
                UserId = seed.User,
                ActorId = seed.GroupActor,
                CreatedAt = DateTime.UtcNow,
                ConcurrencyStamp = Guid.CreateVersion7()
            });
            await Assert.That(runtime.Database.CurrentTransaction).IsSameReferenceAs(owner);
            await owner.CommitAsync();
        }
        await using (var owner = await runtime.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var conversation = await repository.GetByIdForUpdateAsync(id, seed.GroupActor, default);
            await Assert.That(conversation).IsNotNull();
            await Assert.That(await repository.IsPrivacyFencedAsync(conversation!, default)).IsFalse();
            var run = conversation!.QueueRun("fixture", "fixture", DateTime.UtcNow);
            run.Start(DateTime.UtcNow);
            await repository.Update(conversation);
            await Assert.That(await repository.IsPrivacyFencedAsync(conversation, default)).IsFalse();
            conversation.AddMessage(AiMessageRole.Assistant, "Worker result", null, DateTime.UtcNow);
            conversation.CompleteRun(run, DateTime.UtcNow);
            await repository.Update(conversation);
            await PostgresAiActorContextReadContract.RequireReadyAsync(runtime, fixture.NativeQueueEnrollment);
            await Assert.That(runtime.Database.CurrentTransaction).IsSameReferenceAs(owner);
            await owner.CommitAsync();
        }
        var saved = await repository.GetByIdWithDetailsAsync(id, default);
        await Assert.That(saved!.Messages.Single().Content).IsEqualTo("Worker result");
        await Assert.That(saved.Runs.Single().Status).IsEqualTo(AiRunStatus.Succeeded);
    }

    [Test]
    [Arguments("user")]
    [Arguments("tenant")]
    [Arguments("actor")]
    [Arguments("placement")]
    [Arguments("member")]
    [Arguments("scope")]
    public async Task OwnerFenceRejectsRevokedMutationButAllowsOnlyPrivacyRedaction(string revoked)
    {
        var seed = await SeedAsync();
        await using var grants = await GrantOwnerGraphAsync();
        Guid id = Guid.CreateVersion7();
        await using var migration = fixture.CreateDbContext();
        var conversation = new AiConversation
        {
            Id = id,
            TenantId = seed.Tenant,
            UserId = seed.User,
            ActorId = seed.GroupActor,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        conversation.AddMessage(AiMessageRole.User, "Private transcript", seed.User, DateTime.UtcNow);
        migration.Add(conversation);
        await migration.SaveChangesAsync();
        var names = new HumanPrincipalSourceSql(migration);
        string change = revoked switch
        {
            "user" => $"UPDATE {names.Table(typeof(User))} SET is_deleted = TRUE WHERE id = '{seed.User}'",
            "tenant" => $"UPDATE {names.Table(typeof(Tenant))} SET tenant_status_id = {(int)TenantStatusEnum.Suspended} WHERE id = '{seed.Tenant}'",
            "actor" => $"UPDATE {names.Table(typeof(Actor))} SET is_suspended = TRUE WHERE id = '{seed.GroupActor}'",
            "placement" => $"UPDATE {names.Table(typeof(GroupTenant))} SET approval_status_id = {(int)ApprovalStatusEnum.Pending} WHERE id = '{seed.GroupPlacement}'",
            "member" => $"UPDATE {names.Table(typeof(GroupMember))} SET is_deleted = TRUE WHERE id = '{seed.GroupMember}'",
            "scope" => $"UPDATE {names.Table(typeof(Role))} SET role_scope_id = {(int)RoleScopeEnum.Platform} WHERE id = {seed.GroupRole}",
            _ => throw new ArgumentOutOfRangeException(nameof(revoked))
        };
        await migration.Database.ExecuteSqlRawAsync(change);
        await using var runtime = Open(PrimaryDatabaseRole.Runtime, seed.Tenant);
        await runtime.Database.OpenConnectionAsync();
        await using var owner = await runtime.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var repository = new AiConversationRepository(runtime, fixture.NativeQueueEnrollment);
        await Assert.That(await repository.GetByIdForUpdateAsync(id, seed.GroupActor, default)).IsNull();
        var privacy = await repository.GetByIdForPrivacyUpdateAsync(id, default);
        await Assert.That(privacy).IsNotNull();
        privacy!.Messages.Single().Content = "[redacted]";
        await repository.Update(privacy);
        privacy.AddMessage(AiMessageRole.Assistant, "Not a privacy operation", null, DateTime.UtcNow);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.Update(privacy));
        await owner.RollbackAsync();
    }

    [Test]
    [Arguments("actor")]
    [Arguments("user")]
    [Arguments("tenant")]
    [Arguments("tenant-status")]
    [Arguments("tenant-member")]
    [Arguments("group")]
    [Arguments("placement")]
    [Arguments("member")]
    [Arguments("role")]
    [Arguments("permission")]
    [Arguments("grant")]
    public async Task OwnerFenceSourceLockConflictsWithRevocationUntilOriginalTransactionEnds(string source)
    {
        var seed = await SeedAsync();
        await using var runtime = Open(PrimaryDatabaseRole.Runtime, seed.Tenant);
        await runtime.Database.OpenConnectionAsync();
        await using var owner = await runtime.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.That(await PostgresAiConversationOwnerFenceContract.FenceAsync(runtime,
            fixture.NativeQueueEnrollment, seed.Tenant, seed.User, seed.GroupActor, seed.GroupActor, false, default)).IsTrue();
        await using var writer = fixture.CreateDbContext();
        await writer.Database.OpenConnectionAsync();
        var names = new HumanPrincipalSourceSql(writer);
        int permission = await writer.Set<Permission>().Where(row => row.MasterCode == Explore.Domain.Constants.PermissionCodes.EventCreate)
            .Select(row => row.Id).SingleAsync();
        Guid tenantMember = await writer.TenantUsers.Where(row => row.TenantId == seed.Tenant && row.UserId == seed.User)
            .Select(row => row.Id).SingleAsync();
        (Type Type, string Key, object Value) probe = source switch
        {
            "actor" => (typeof(Actor), "Id", seed.GroupActor),
            "user" => (typeof(User), "Id", seed.User),
            "tenant" => (typeof(Tenant), "Id", seed.Tenant),
            "tenant-status" => (typeof(TenantStatus), "Id", (int)TenantStatusEnum.Active),
            "tenant-member" => (typeof(TenantUser), "Id", tenantMember),
            "group" => (typeof(Group), "Id", seed.Group),
            "placement" => (typeof(GroupTenant), "Id", seed.GroupPlacement),
            "member" => (typeof(GroupMember), "Id", seed.GroupMember),
            "role" => (typeof(Role), "Id", seed.GroupRole),
            "permission" => (typeof(Permission), "Id", permission),
            "grant" => (typeof(RolePermission), "RoleId", seed.GroupRole),
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };
        var (type, key, value) = probe;
        // NOWAIT is the bounded database signal: no scheduling assumptions or sleeps.
        var error = await Assert.ThrowsAsync<PostgresException>(() => writer.Database.ExecuteSqlRawAsync(
            $"SELECT {names.Column(type, key)} FROM {names.Table(type)} WHERE {names.Column(type, key)} = {{0}} FOR UPDATE NOWAIT", value));
        await Assert.That(error!.SqlState).IsEqualTo(PostgresErrorCodes.LockNotAvailable);
        await owner.RollbackAsync();
        await writer.Database.ExecuteSqlRawAsync(
            $"UPDATE {names.Table(typeof(GroupMember))} SET is_deleted = TRUE WHERE id = {{0}}", seed.GroupMember);
        await using var fresh = await runtime.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.That(await PostgresAiConversationOwnerFenceContract.FenceAsync(runtime,
            fixture.NativeQueueEnrollment, seed.Tenant, seed.User, seed.GroupActor, seed.GroupActor, false, default)).IsFalse();
        await fresh.RollbackAsync();
    }

    [Test]
    public async Task OwnerFenceRejectsTransactionReplacementAndWrongTenant()
    {
        var seed = await SeedAsync();
        await using var runtime = Open(PrimaryDatabaseRole.Runtime, seed.Tenant);
        await runtime.Database.OpenConnectionAsync();
        await using (var weak = await runtime.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted))
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => PostgresAiConversationOwnerFenceContract.FenceAsync(
                runtime, fixture.NativeQueueEnrollment, seed.Tenant, seed.User, seed.Personal, seed.Personal, false, default));
            await weak.RollbackAsync();
        }
        await using var owner = await runtime.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => PostgresAiConversationOwnerFenceContract.FenceAsync(
            runtime, fixture.NativeQueueEnrollment, Guid.CreateVersion7(), seed.User, seed.Personal, seed.Personal, false, default));
        await owner.RollbackAsync();
    }

    [Test]
    public async Task OwnerFenceTrackedRootCannotCrossTransactionAndRetentionNeedsNoHumanAdmission()
    {
        var seed = await SeedAsync();
        await using var grants = await GrantOwnerGraphAsync();
        var root = new AiConversation
        {
            Id = Guid.CreateVersion7(),
            TenantId = seed.Tenant,
            UserId = seed.User,
            ActorId = seed.Personal,
            CreatedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        root.AddMessage(AiMessageRole.User, "Expired", seed.User, root.CreatedAt);
        await using (var migration = fixture.CreateDbContext())
        {
            migration.Add(root);
            await migration.SaveChangesAsync();
        }
        await using var runtime = Open(PrimaryDatabaseRole.Runtime, seed.Tenant);
        await runtime.Database.OpenConnectionAsync();
        var repository = new AiConversationRepository(runtime, fixture.NativeQueueEnrollment);
        AiConversation held;
        await using (var owner = await runtime.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            held = (await repository.GetByIdForUpdateAsync(root.Id, seed.Personal, default))!;
            await owner.CommitAsync();
        }
        await using var replacement = await runtime.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.Update(held));
        runtime.ChangeTracker.Clear();
        var result = await repository.RedactExpiredConversationsAsync(DateTime.UtcNow, 30, DateTime.UtcNow, false, default);
        await Assert.That(result.RedactedConversations).IsEqualTo(1);
        await PostgresAiActorContextReadContract.RequireReadyAsync(runtime, fixture.NativeQueueEnrollment);
        await replacement.CommitAsync();
        await Assert.That(await repository.GetByIdWithDetailsAsync(root.Id, default)).IsNull();
    }

    [Test]
    [Arguments("body", "ai_conversation_owner_body_drift")]
    [Arguments("search-path", "native_privilege_guard_not_ready")]
    [Arguments("acl", "native_privilege_guard_not_ready")]
    [Arguments("role", "native_privilege_guard_not_ready")]
    [Arguments("membership", "native_privilege_guard_not_ready")]
    [Arguments("column", "ai_conversation_owner_source_catalog_drift")]
    [Arguments("policy", "ai_conversation_owner_policy_drift")]
    [Arguments("private-grant", "ai_conversation_owner_private_source_privilege")]
    public async Task OwnerFenceInstallerRejectsDriftWithoutRepair(string mutation, string reason)
    {
        await using var migration = fixture.CreateDbContext();
        await using var transaction = await migration.Database.BeginTransactionAsync();
        var sql = new HumanPrincipalSourceSql(migration);
        string function = PostgresAiConversationOwnerFenceContract.FunctionSql(migration);
        string signature = function + "(uuid,uuid,uuid,uuid,boolean,boolean)";
        string change = mutation switch
        {
            "body" => $"""
                CREATE OR REPLACE FUNCTION {function}(p_tenant uuid,p_principal uuid,p_actor uuid,p_admitted uuid,p_privacy boolean,p_organizer boolean)
                    RETURNS boolean LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                    AS $drift$ BEGIN RETURN true; END; $drift$
                """,
            "search-path" => $"ALTER FUNCTION {signature} SET search_path = public,pg_catalog",
            "acl" => $"GRANT EXECUTE ON FUNCTION {signature} TO PUBLIC",
            "role" => $"ALTER ROLE {PostgresAiConversationOwnerFenceContract.OwnerRole} INHERIT",
            "membership" => $"GRANT {PostgresAiConversationOwnerFenceContract.OwnerRole} TO {sql.Quote(fixture.NativeQueueEnrollment.RuntimeLogin)} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE",
            "column" => $"ALTER TABLE {sql.Table(typeof(Actor))} ADD COLUMN owner_fence_unexpected integer",
            "policy" => $"ALTER POLICY ai_conversation_owner_fence ON {sql.Table(typeof(TenantUser))} USING (true)",
            "private-grant" => $"GRANT SELECT (id) ON {sql.Table(typeof(User))} TO {sql.Quote(fixture.NativeQueueEnrollment.RuntimeLogin)}",
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        await migration.Database.ExecuteSqlRawAsync(change);
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresAiConversationOwnerFenceContract.ApplyAsync(migration, fixture.NativeQueueEnrollment));
        await Assert.That(error!.MessageText).IsEqualTo(reason);
        await transaction.RollbackAsync();
        await PostgresAiConversationOwnerFenceContract.ApplyAsync(migration, fixture.NativeQueueEnrollment);
    }

    private async Task<OwnerGraphGrants> GrantOwnerGraphAsync()
    {
        await using var migration = fixture.CreateDbContext();
        var names = new HumanPrincipalSourceSql(migration);
        string tables = string.Join(",", new[] { typeof(AiConversation), typeof(AiMessage), typeof(AiRun),
            typeof(AiProposedAction), typeof(AiConversationReference), typeof(AiToolExecution) }.Select(names.Table));
        string lookups = string.Join(",", new[] { typeof(AiConversationStatusLookup), typeof(AiMessageRoleLookup),
            typeof(AiRunStatusLookup), typeof(AiReferenceKindLookup), typeof(AiProposedActionKindLookup),
            typeof(AiProposedActionStatusLookup), typeof(AiProviderKindLookup) }.Select(names.Table));
        string login = names.Quote(fixture.NativeQueueEnrollment.RuntimeLogin);
        await migration.Database.OpenConnectionAsync();
        await using var command = migration.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"GRANT SELECT,INSERT,UPDATE,DELETE ON {tables} TO {login}; GRANT SELECT ON {lookups} TO {login}";
        await command.ExecuteNonQueryAsync();
        return new(fixture, $"REVOKE SELECT,INSERT,UPDATE,DELETE ON {tables} FROM {login}; REVOKE SELECT ON {lookups} FROM {login}");
    }

    private sealed class OwnerGraphGrants(Fixtures.PostgreSqlContainerFixture fixture, string revoke) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await using var migration = fixture.CreateDbContext();
            await migration.Database.ExecuteSqlRawAsync(revoke);
        }
    }
}
