using System.Text.Json;
using System.Text.Json.Nodes;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Settings.Definitions;
using Explore.Persistence.Security;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class HumanImportPublicationNativeTests(PostgreSqlContainerFixture postgres)
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ActualHumanImportRequiresIndependentlyExpectedNativeSourceBody(bool restampedForeignBody)
    {
        await postgres.ResetAsync();
        await using var human = await OperatorIdentityEditorNativeFixture.CreateAsync(postgres);
        var cleanup = new List<string>();
        await using var administration = postgres.CreateDbContext();
        await PostgresConfigurationImportPublicationPolicyContract.ApplyAsync(administration);
        var sql = administration.GetService<ISqlGenerationHelper>();
        string login = postgres.NativeQueueEnrollment.GlobalLogin;
        string quotedLogin = sql.DelimitIdentifier(login);
        string[] roles =
        [
            PostgresConfigurationImportSnapshotContract.GlobalRole, PostgresConfigurationImportStateContract.GlobalRole,
            PostgresConfigurationImportLegalSourceContract.GlobalRole, PostgresConfigurationImportPublicationPolicyContract.GlobalRole,
            PostgresConfigurationImportReceiptContract.GlobalRole, PostgresConfigurationImportPaidPolicyContract.GlobalRole
        ];
        string key = EventSettingDefinitions.RequireApproval.Key;
        try
        {
            foreach (string role in roles)
            {
                if (await administration.Database.SqlQuery<bool>(
                        $"SELECT pg_catalog.pg_has_role({login}, {role}, 'MEMBER') AS \"Value\"").SingleAsync())
                {
                    await Assert.That(await administration.Database.SqlQuery<bool>(
                        $"SELECT pg_catalog.pg_has_role({login}, {role}, 'USAGE') AS \"Value\"").SingleAsync()).IsTrue();
                    continue;
                }
                await administration.Database.ExecuteSqlRawAsync(
                    $"GRANT {sql.DelimitIdentifier(role)} TO {quotedLogin} WITH INHERIT TRUE, SET FALSE");
                cleanup.Add($"REVOKE {sql.DelimitIdentifier(role)} FROM {quotedLogin}");
            }
            // Existing import discovery reads these non-tenant roots. No Runtime grant,
            // private identity table access or cross-tenant EF bypass is introduced.
            foreach (Type type in new[] { typeof(Tenant), typeof(TenantStatus), typeof(SystemSetting),
                         typeof(ConfigurationManifestOperation) })
            {
                var entity = administration.Model.FindEntityType(type)!;
                string table = sql.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
                if (await administration.Database.SqlQuery<bool>(
                        $"SELECT pg_catalog.has_table_privilege({login}, {table}, 'SELECT') AS \"Value\"").SingleAsync())
                    continue;
                await administration.Database.ExecuteSqlRawAsync($"GRANT SELECT ON {table} TO {quotedLogin}");
                cleanup.Add($"REVOKE SELECT ON {table} FROM {quotedLogin}");
            }
            if (!await administration.PaidEventPolicyVersions.AnyAsync())
            {
                administration.PaidEventPolicyVersions.Add(PaidEventPolicyVersion.CreateDefaultInstance());
                await administration.SaveChangesAsync();
            }
            await administration.SystemSettings.Where(row => row.SettingKey == key)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.Value, "false").SetProperty(row => row.IsLocked, false));
            Guid sessionId = Guid.Empty;
            await human.InRequestAsync(async () =>
            {
                var executor = human.Services.GetRequiredService<IConfigurationInstanceImportExecutor>();
                var snapshot = await executor.ReadSnapshotAsync(ConfigurationManifestExportView.Portable, default);
                var manifest = JsonNode.Parse(snapshot.Utf8Json.Span)!;
                manifest["spec"]!["instance"]!["settings"] = new JsonObject { [key] = true };
                var created = await executor.CreateAsync(JsonSerializer.SerializeToUtf8Bytes(manifest), default);
                sessionId = created.SessionId;
                var request = new ConfigurationImportPreviewRequest
                {
                    SelectedSectionKeys = ["instance.settings"],
                    Mappings = new Dictionary<string, string>(),
                    ApplyMode = ConfigurationImportApplyMode.ApplySelected,
                    GrantedApprovalCodes = []
                };
                await Assert.That((await executor.PreviewAsync(created.SessionId, created.AccessToken, request, default))
                    .IsApplyReady).IsTrue();
                if (restampedForeignBody)
                {
                    string schema = administration.Model.FindEntityType(typeof(SystemSetting))!.GetSchema()!;
                    string function = sql.DelimitIdentifier("configuration_import_publication_source", schema);
                    await using var alteration = await administration.Database.BeginTransactionAsync();
                    await administration.Database.ExecuteSqlRawAsync(
                        $"SET LOCAL ROLE {sql.DelimitIdentifier(PostgresConfigurationImportPublicationPolicyContract.OwnerRole)}");
                    await administration.Database.ExecuteSqlRawAsync($"""
                        CREATE OR REPLACE FUNCTION {function}(p_tenant uuid) RETURNS jsonb
                        LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                        AS $foreign_body$BEGIN RETURN pg_catalog.jsonb_build_object(); END;$foreign_body$;
                        DO $stamp$
                        BEGIN
                            EXECUTE (SELECT pg_catalog.format('COMMENT ON FUNCTION %s IS %L',
                                p.oid::regprocedure, 'event-configuration-import-publication:v1:' ||
                                pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(p.prosrc, 'UTF8')), 'hex'))
                                FROM pg_catalog.pg_proc p WHERE p.oid = '{function}(uuid)'::regprocedure);
                        END;$stamp$;
                        """);
                    await alteration.CommitAsync();
                }
                var apply = executor.ApplyAsync(created.SessionId, created.AccessToken, request, null, null, default);
                if (restampedForeignBody)
                    await Assert.ThrowsAsync<UnauthorizedAccessException>(() => apply);
                else
                    await Assert.That((await apply).Status).IsEqualTo(ConfigurationImportOperationStatus.Applied);
            });
            await using var observer = postgres.CreateDbContext();
            await Assert.That(await observer.SystemSettings.Where(row => row.SettingKey == key)
                .Select(row => row.Value).SingleAsync()).IsEqualTo(restampedForeignBody ? "false" : "true");
            await Assert.That(await observer.ConfigurationImportOperations.AnyAsync(row => row.SessionId == sessionId))
                .IsEqualTo(!restampedForeignBody);
            await Assert.That(await observer.Set<ConfigurationImportSession>().Where(row => row.SessionId == sessionId)
                .Select(row => row.State).SingleAsync())
                .IsEqualTo(restampedForeignBody ? ConfigurationImportSessionState.PreviewReady : ConfigurationImportSessionState.Consumed);
        }
        finally
        {
            await PostgresConfigurationImportPublicationPolicyContract.ApplyAsync(administration);
            foreach (string command in cleanup.AsEnumerable().Reverse())
                await administration.Database.ExecuteSqlRawAsync(command);
        }
    }
}
