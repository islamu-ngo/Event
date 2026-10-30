using System.Data;
using Explore.Domain;
using Explore.Persistence.Security;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed partial class PostgresAiActorContextReadContractTests
{
    [Test]
    [Arguments("body", "ai_actor_context_body_drift")]
    [Arguments("search-path", "native_privilege_guard_not_ready")]
    [Arguments("acl", "native_privilege_guard_not_ready")]
    [Arguments("role", "native_privilege_guard_not_ready")]
    [Arguments("membership", "native_privilege_guard_not_ready")]
    [Arguments("column", "ai_actor_context_source_catalog_drift")]
    [Arguments("policy", "ai_actor_context_policy_drift")]
    public async Task InstallerRefusesExistingDriftInsteadOfNormalizingIt(string mutation, string reason)
    {
        var seed = await SeedAsync();
        // Establish a real admitted Runtime read before the transactional drift.
        await Assert.That((await ReadFreshAsync(seed)).Count).IsEqualTo(3);
        await using var migration = fixture.CreateDbContext();
        await using var transaction = await migration.Database.BeginTransactionAsync();
        var sql = new HumanPrincipalSourceSql(migration);
        string function = PostgresAiActorContextReadContract.FunctionSql(migration);
        string change = mutation switch
        {
            "body" => $"""
                CREATE OR REPLACE FUNCTION {function}(p_tenant uuid,p_principal uuid)
                    RETURNS TABLE(actor_id uuid,actor_type_id integer,user_id uuid,organization_id uuid,group_id uuid,
                        display_name text,organization_name text,group_name text)
                    LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                    AS $drift$ BEGIN RETURN; END; $drift$
                """,
            "search-path" => $"ALTER FUNCTION {function}(uuid,uuid) SET search_path = public,pg_catalog",
            "acl" => $"GRANT EXECUTE ON FUNCTION {function}(uuid,uuid) TO PUBLIC",
            "role" => $"ALTER ROLE {PostgresAiActorContextReadContract.OwnerRole} INHERIT",
            "membership" => $"""
                GRANT {PostgresAiActorContextReadContract.OwnerRole}
                    TO {sql.Quote(fixture.NativeQueueEnrollment.RuntimeLogin)} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE
                """,
            "column" => $"ALTER TABLE {sql.Table(typeof(ActorPii))} ADD COLUMN actor_context_unexpected integer",
            "policy" => $"ALTER POLICY ai_actor_context_read ON {sql.Table(typeof(TenantUser))} USING (true)",
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        await migration.Database.ExecuteSqlRawAsync(change);
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresAiActorContextReadContract.ApplyAsync(migration, fixture.NativeQueueEnrollment));
        await Assert.That(error!.SqlState).IsEqualTo(PostgresErrorCodes.ObjectNotInPrerequisiteState);
        await Assert.That(error.MessageText).IsEqualTo(reason);
        await transaction.RollbackAsync();
        await Assert.That((await ReadFreshAsync(seed)).Count).IsEqualTo(3);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CatalogProofKeepsEachOwnersSourceInventory(bool conversationSource)
    {
        await using var migration = fixture.CreateDbContext();
        await using var transaction = await migration.Database.BeginTransactionAsync();
        var sql = new HumanPrincipalSourceSql(migration);
        Type source = conversationSource ? typeof(PrivacyErasureSaga) : typeof(ActorPii);
        await migration.Database.ExecuteSqlRawAsync(
            $"ALTER TABLE {sql.Table(source)} ADD COLUMN family_unexpected integer");

        await PostgresEventCreationPublisherReadContract.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        PostgresException? error;
        if (conversationSource)
        {
            await PostgresAiActorContextReadContract.ApplyAsync(migration, fixture.NativeQueueEnrollment);
            error = await Assert.ThrowsAsync<PostgresException>(() =>
                PostgresAiConversationOwnerFenceContract.ApplyAsync(migration, fixture.NativeQueueEnrollment));
        }
        else
        {
            await PostgresAiConversationOwnerFenceContract.ApplyAsync(migration, fixture.NativeQueueEnrollment);
            error = await Assert.ThrowsAsync<PostgresException>(() =>
                PostgresAiActorContextReadContract.ApplyAsync(migration, fixture.NativeQueueEnrollment));
        }
        await Assert.That(error!.SqlState).IsEqualTo(PostgresErrorCodes.ObjectNotInPrerequisiteState);
        await Assert.That(error.MessageText).IsEqualTo(conversationSource
            ? "ai_conversation_owner_source_catalog_drift" : "ai_actor_context_source_catalog_drift");
        await transaction.RollbackAsync();
    }

    [Test]
    [Arguments(PrimaryDatabaseRole.Runtime)]
    [Arguments(PrimaryDatabaseRole.Global)]
    public async Task RawPrivateGrantIsRejectedBeforeReinstallationCanHideIt(PrimaryDatabaseRole role)
    {
        var seed = await SeedAsync();
        await using var migration = fixture.CreateDbContext();
        await using var transaction = await migration.Database.BeginTransactionAsync();
        var sql = new HumanPrincipalSourceSql(migration);
        string login = role == PrimaryDatabaseRole.Runtime
            ? fixture.NativeQueueEnrollment.RuntimeLogin : fixture.NativeQueueEnrollment.GlobalLogin;
        await migration.Database.ExecuteSqlRawAsync(
            $"GRANT SELECT ({sql.Column(typeof(ActorPii), "DisplayName")}) ON {sql.Table(typeof(ActorPii))} TO {sql.Quote(login)}");
        await PostgresEventCreationPublisherReadContract.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        await PostgresAiConversationOwnerFenceContract.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresAiActorContextReadContract.ApplyAsync(migration, fixture.NativeQueueEnrollment));
        await Assert.That(error!.MessageText).IsEqualTo("ai_actor_context_private_source_privilege");
        await transaction.RollbackAsync();
        await Assert.That((await ReadFreshAsync(seed)).Count).IsEqualTo(3);
    }

    [Test]
    public async Task RuntimeCannotReuseMissingOrChangedTenantSessionSelection()
    {
        var seed = await SeedAsync();
        foreach (string selected in new[] { "", Guid.CreateVersion7().ToString("D") })
        {
            await using var context = Open(PrimaryDatabaseRole.Runtime, seed.Tenant);
            await context.Database.OpenConnectionAsync();
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            // Deliberately corrupt the session, without changing the bound CLR tenant.
            await context.Database.ExecuteSqlRawAsync(
                "SELECT pg_catalog.set_config('app.current_tenant_id',{0},true)", selected);
            var error = await Assert.ThrowsAsync<PostgresException>(() => ReadAsync(context, seed));
            await Assert.That(error!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
            await transaction.RollbackAsync();
        }
    }
}
