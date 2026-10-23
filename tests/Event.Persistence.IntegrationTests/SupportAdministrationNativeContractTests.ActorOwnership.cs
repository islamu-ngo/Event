using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Native = Explore.Persistence.Security.PostgresSupportAdministrationContract;

namespace Event.Persistence.IntegrationTests;

public sealed partial class SupportAdministrationNativeContractTests
{
    [Test]
    [Arguments("private")]
    [Arguments("public")]
    [Arguments("grant-option")]
    [Arguments("dormant-role")]
    public async Task ActorOnlyMembershipReadsCoexistWithSupportWithoutPrivateAuthorityOrRepair(string attack)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        CancellationToken token = timeout.Token;
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options,
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        await using var context = new ExploreDbContext(options.Options);
        context.BindTenantLease();
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(token);
            var connection = context.Database.GetDbConnection();
            int backend = ((NpgsqlConnection)connection).ProcessID;
            var names = new HumanPrincipalSourceSql(context);
            string table = names.Table(typeof(TenantUser));
            string runtime = names.Quote(fixture.NativeQueueEnrollment.RuntimeLogin);
            string columns = string.Join(",", context.Model.FindEntityType(typeof(TenantUser))!
                .GetProperties().Select(property => names.Column(typeof(TenantUser), property.Name)));
            await using (var change = new NpgsqlCommand($"""
                REVOKE ALL PRIVILEGES ON {table} FROM {runtime};
                REVOKE SELECT ({columns}), INSERT ({columns}), UPDATE ({columns}), REFERENCES ({columns}) ON {table} FROM {runtime};
                GRANT SELECT ({names.Column(typeof(TenantUser), nameof(TenantUser.TenantId))},
                    {names.Column(typeof(TenantUser), nameof(TenantUser.ActorId))},
                    {names.Column(typeof(TenantUser), nameof(TenantUser.IsDeleted))}) ON {table} TO {runtime};
                """, (NpgsqlConnection)connection, (NpgsqlTransaction)transaction.GetDbTransaction()))
                await change.ExecuteNonQueryAsync(token);
            string login = fixture.NativeQueueEnrollment.RuntimeLogin;
            string status = names.ColumnName(typeof(TenantUser), nameof(TenantUser.StatusId));
            await Assert.That(await context.Database.SqlQuery<bool>($"""
                SELECT NOT pg_catalog.has_table_privilege({login},{table},'SELECT,INSERT,UPDATE,DELETE')
                    AND NOT pg_catalog.has_any_column_privilege({login},{table},'INSERT,UPDATE,REFERENCES')
                    AND NOT pg_catalog.has_column_privilege({login},{table},{status},'SELECT') AS "Value"
                """).SingleAsync(token)).IsTrue();
            await Native.RequireReadyAsync(context, fixture.NativeQueueEnrollment, token);
            await Native.ApplyAsync(context, fixture.NativeQueueEnrollment, token);

            await transaction.CreateSavepointAsync("healthy_actor_support", token);
            string actorId = names.Column(typeof(TenantUser), nameof(TenantUser.ActorId));
            string otherRole = names.Quote("actor_support_" + Guid.CreateVersion7().ToString("N"));
            string attackSql = attack switch
            {
                "private" => $"GRANT SELECT ({names.Column(typeof(TenantUser), nameof(TenantUser.ModerationNote))}) ON {table} TO {runtime}",
                "public" => $"GRANT SELECT ({actorId}) ON {table} TO PUBLIC",
                "grant-option" => $"GRANT SELECT ({actorId}) ON {table} TO {runtime} WITH GRANT OPTION",
                "dormant-role" => $"CREATE ROLE {otherRole} NOLOGIN; GRANT SELECT ({actorId}) ON {table} TO {otherRole}; GRANT {otherRole} TO {runtime} WITH INHERIT FALSE, SET FALSE, ADMIN FALSE",
                _ => throw new ArgumentOutOfRangeException(nameof(attack))
            };
            await using (var change = new NpgsqlCommand(attackSql,
                (NpgsqlConnection)connection, (NpgsqlTransaction)transaction.GetDbTransaction()))
                await change.ExecuteNonQueryAsync(token);
            string attacked = await ReadAclAsync();
            await transaction.CreateSavepointAsync("before_actor_support_check", token);
            string diagnostic = attack == "public" ? "native_privilege_guard_not_ready"
                : "support_administration_ambient_scalar_authority";
            var readiness = await Assert.ThrowsAsync<PostgresException>(() =>
                Native.RequireReadyAsync(context, fixture.NativeQueueEnrollment, token));
            await Assert.That(readiness?.SqlState).IsEqualTo(PostgresErrorCodes.ObjectNotInPrerequisiteState);
            await Assert.That(readiness?.MessageText).IsEqualTo(diagnostic);
            await transaction.RollbackToSavepointAsync("before_actor_support_check", token);
            var installation = await Assert.ThrowsAsync<PostgresException>(() =>
                Native.ApplyAsync(context, fixture.NativeQueueEnrollment, token));
            await Assert.That(installation?.SqlState).IsEqualTo(PostgresErrorCodes.ObjectNotInPrerequisiteState);
            await Assert.That(installation?.MessageText).IsEqualTo(diagnostic);
            await transaction.RollbackToSavepointAsync("before_actor_support_check", token);
            await Assert.That(await ReadAclAsync()).IsEqualTo(attacked);

            await transaction.RollbackToSavepointAsync("healthy_actor_support", token);
            await Native.RequireReadyAsync(context, fixture.NativeQueueEnrollment, token);
            await Native.ApplyAsync(context, fixture.NativeQueueEnrollment, token);
            await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await Assert.That(context.Database.GetDbConnection()).IsSameReferenceAs(connection);
            await Assert.That(((NpgsqlConnection)connection).ProcessID).IsEqualTo(backend);
            await transaction.RollbackAsync(CancellationToken.None);

            async Task<string> ReadAclAsync()
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction.GetDbTransaction();
                command.CommandText = """
                    SELECT jsonb_agg(jsonb_build_array(attnum,attacl::text) ORDER BY attnum)::text
                    FROM pg_catalog.pg_attribute WHERE attrelid=pg_catalog.to_regclass(@table) AND attnum<>0
                    """;
                command.Parameters.Add(new NpgsqlParameter("table", table));
                return (string)(await command.ExecuteScalarAsync(token))!;
            }
        });
    }
}
