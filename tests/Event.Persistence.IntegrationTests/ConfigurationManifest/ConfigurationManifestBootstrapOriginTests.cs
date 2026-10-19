using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Ingestion;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Settings.Definitions;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Event.Persistence.IntegrationTests.ConfigurationManifest;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class ConfigurationManifestBootstrapOriginTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task OriginalAbsentSetting_UsesApiCoordinationWithoutNativeTableOrRowFences()
    {
        await fixture.ResetAsync();
        await using (var setup = fixture.CreateDbContext())
            await setup.SystemSettings
                .Where(setting => setting.SettingKey == AppearanceSettingDefinitions.DefaultThemeMode.Key)
                .ExecuteDeleteAsync();
        await using var host = ConfigurationManifestBootstrapTestHost.ForPostgres(fixture);
        host.AfterObservation = async (actual, _, token) =>
        {
            var entity = actual.Model.FindEntityType(typeof(SystemSetting))!;
            string table = actual.GetService<ISqlGenerationHelper>()
                .DelimitIdentifier(entity.GetTableName()!, entity.GetSchema() ?? "public");
            await using var independent = fixture.CreateDbContext();
            await using var transaction = await independent.Database.BeginTransactionAsync();
            await using var probe = independent.Database.GetDbConnection().CreateCommand();
            probe.Transaction = transaction.GetDbTransaction();
            probe.CommandText = "SELECT pg_catalog.pg_try_advisory_xact_lock(@key)";
            var key = probe.CreateParameter();
            key.ParameterName = "key";
            key.Value = RelationalNamedLock.ComputeStableKey(
                "explore:setting-mutation:" + ConfigurationManifestLockKeys.InstanceManifest);
            probe.Parameters.Add(key);
            await Assert.That((bool)(await probe.ExecuteScalarAsync(token))!).IsFalse();

            // EXCLUSIVE permits ordinary reads but conflicts with SHARE and FOR UPDATE's
            // ROW SHARE lock. Non-participating database writers are intentionally unfenced.
            probe.Parameters.Clear();
            probe.CommandText = $"LOCK TABLE {table} IN EXCLUSIVE MODE NOWAIT";
            await probe.ExecuteNonQueryAsync(token);
            await transaction.RollbackAsync(token);
        };
        var source = ConfigurationManifestApplicationTestSupport.Source("api-coordination");
        var settings = source.Manifest.Spec.Instance.Settings.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        settings[AppearanceSettingDefinitions.DefaultThemeMode.Key] = System.Text.Json.JsonSerializer.SerializeToElement("dark");
        source = source with
        {
            Manifest = source.Manifest with
            {
                Spec = source.Manifest.Spec with
                {
                    Instance = source.Manifest.Spec.Instance with { Settings = settings }
                }
            }
        };
        var result = await host.ExecuteAsync(new ApplyConfigurationManifestCommand(source), CancellationToken.None);
        await Assert.That(result.IsSuccess).IsTrue();
        await using var verification = fixture.CreateDbContext();
        var restored = await verification.SystemSettings.SingleAsync(
            setting => setting.SettingKey == AppearanceSettingDefinitions.DefaultThemeMode.Key);
        await Assert.That(System.Text.Json.JsonSerializer.Deserialize<string>(restored.Value!)).IsEqualTo("dark");
        await Assert.That(restored.CreatedBy).IsNull();
    }

    [Test]
    public async Task ValidateOnly_DoesNotAcquireManifestMutexWhileReadingOriginalInputs()
    {
        await fixture.ResetAsync();
        await using var host = ConfigurationManifestBootstrapTestHost.ForPostgres(
            fixture, mode: ConfigurationManifestMode.ValidateOnly);
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.AfterObservation = async (_, plan, token) =>
        {
            await Assert.That(plan.Mode).IsEqualTo(ConfigurationManifestMode.ValidateOnly);
            observed.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
        };
        Task<Explore.Application.Responses.BaseCommandResponse<Guid>> validation = host.ExecuteAsync(
            new ApplyConfigurationManifestCommand(ConfigurationManifestApplicationTestSupport.Source("validation-lock")),
            CancellationToken.None);
        try
        {
            await observed.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await using var independent = fixture.CreateDbContext();
            await using var transaction = await independent.Database.BeginTransactionAsync();
            await using var probe = independent.Database.GetDbConnection().CreateCommand();
            probe.Transaction = transaction.GetDbTransaction();
            probe.CommandText = "SELECT pg_catalog.pg_try_advisory_xact_lock(@key)";
            var key = probe.CreateParameter();
            key.ParameterName = "key";
            key.Value = RelationalNamedLock.ComputeStableKey(
                "explore:setting-mutation:" + ConfigurationManifestLockKeys.InstanceManifest);
            probe.Parameters.Add(key);
            bool acquired = (bool)(await probe.ExecuteScalarAsync())!;
            await Assert.That(acquired).IsTrue();
            await transaction.RollbackAsync();
        }
        finally
        {
            release.TrySetResult();
        }
        await Assert.That((await validation.WaitAsync(TimeSpan.FromSeconds(30))).IsSuccess).IsTrue();
        await AssertNoBootstrapAsync();
    }

    [Test]
    public async Task RealGlobal_WithCounterfeitSourceAndAuthority_CannotOriginateBootstrap()
    {
        await fixture.ResetAsync();
        await using var host = new ConfigurationManifestBootstrapTestHost(
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global), () => fixture.CreateDbContext(),
            fixture.NativeQueueEnrollment.MigratorLogin, counterfeitAuthority: new CounterfeitAuthority());
        await using var scope = host.Services.CreateAsyncScope();
        var owner = scope.ServiceProvider.GetRequiredService<IConfigurationManifestBootstrapOwnerFactory>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            owner.ApplyAsync(new CounterfeitSource(), CancellationToken.None));

        await AssertNoBootstrapAsync();
    }

    [Test]
    public async Task RealGlobal_WithoutOriginalStartupFrame_CannotUseExecutor()
    {
        await fixture.ResetAsync();
        await using var host = ConfigurationManifestBootstrapTestHost.ForPostgres(fixture);
        await using var scope = host.Services.CreateAsyncScope();
        var executor = scope.ServiceProvider.GetRequiredService<IConfigurationManifestBootstrapExecutor>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            executor.ApplyOriginalStartupAsync(CancellationToken.None));

        await AssertNoBootstrapAsync();
    }

    [Test]
    public async Task ParsedBootstrapDto_CannotReplaceDeploymentFileOrigin()
    {
        await fixture.ResetAsync();
        await using var host = ConfigurationManifestBootstrapTestHost.ForPostgres(fixture);
        await using var scope = host.Services.CreateAsyncScope();
        var applier = scope.ServiceProvider.GetRequiredService<IConfigurationManifestApplier>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            applier.ApplyAsync(ConfigurationManifestApplicationTestSupport.Source("dto-origin"), CancellationToken.None));

        await AssertNoBootstrapAsync();
    }

    [Test]
    public async Task OriginalStartup_CommitsRealGlobalMutationWithNoHumanActor()
    {
        await fixture.ResetAsync();
        await using var host = ConfigurationManifestBootstrapTestHost.ForPostgres(fixture);
        var result = await host.ExecuteAsync(new ApplyConfigurationManifestCommand(
            ConfigurationManifestApplicationTestSupport.Source("real-origin")), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await using var context = fixture.CreateDbContext();
        var tenant = await context.Tenants.IgnoreQueryFilters().SingleAsync(row => row.Slug == "real-origin");
        await Assert.That(tenant.CreatedBy).IsNull();
        await Assert.That(tenant.UpdatedBy).IsNull();
        await Assert.That(await context.TenantSettingOverrides.IgnoreQueryFilters()
            .AnyAsync(row => row.TenantId == tenant.Id && (row.CreatedBy != null || row.UpdatedBy != null))).IsFalse();
        await Assert.That(await context.TenantSettingsDocuments.IgnoreQueryFilters()
            .AnyAsync(row => row.TenantId == tenant.Id && (row.CreatedBy != null || row.UpdatedBy != null))).IsFalse();
        var operation = await context.ConfigurationManifestOperations.SingleAsync();
        await Assert.That(operation.Id).IsEqualTo(result.Id);
        await Assert.That(operation.Digest).IsEqualTo(host.ArtifactDigest);
        await Assert.That(operation.Mode).IsEqualTo(ConfigurationManifestAuditMode.Bootstrap);
        await Assert.That(operation.Status).IsEqualTo(ConfigurationManifestOperationStatus.Applied);
        var effect = await context.OutboxMessages.SingleAsync(row => row.Id == operation.EffectOutboxId);
        await Assert.That(effect.Status).IsEqualTo(OutboxMessageStatus.Pending);
        await Assert.That(effect.AggregateId).IsEqualTo(operation.Id);
        await Assert.That(effect.AggregateType).IsEqualTo(ConfigurationManifestEffectOutbox.AggregateType);
        await Assert.That(effect.EventType).IsEqualTo(ConfigurationManifestEffectOutbox.EventType);
        var instructions = ConfigurationManifestEffectPayload.Read(effect.Payload, effect.CreatedAt);
        await Assert.That(instructions.Version).IsEqualTo(ConfigurationManifestEffectPayload.CurrentVersion);
        await Assert.That(instructions.Tenants.Select(tenant => tenant.TenantId))
            .IsEquivalentTo([tenant.Id]);
        await Assert.That(instructions.NotificationTimeUtc).IsEqualTo(operation.StartedAt);
    }

    [Test]
    public async Task ReplacedFileAfterOriginalObservation_RollsBackWithoutConfigurationOrEvidence()
    {
        await fixture.ResetAsync();
        await using var host = ConfigurationManifestBootstrapTestHost.ForPostgres(fixture);
        host.AfterObservation = async (_, _, token) =>
        {
            string replacement = host.ArtifactPath + ".replacement";
            await File.WriteAllBytesAsync(replacement, await File.ReadAllBytesAsync(host.ArtifactPath, token), token);
            File.Move(replacement, host.ArtifactPath, overwrite: true);
        };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => host.ExecuteAsync(
            new ApplyConfigurationManifestCommand(ConfigurationManifestApplicationTestSupport.Source("changed-origin")),
            CancellationToken.None));

        await AssertNoBootstrapAsync();
    }

    [Test]
    public async Task AlteredNativeSourceWithUnchangedIdentity_CannotAdmitOriginalStartup()
    {
        await fixture.ResetAsync();
        await using var host = ConfigurationManifestBootstrapTestHost.ForPostgres(fixture);
        await using var migration = fixture.CreateDbContext();
        await migration.Database.OpenConnectionAsync();
        var sql = migration.GetService<ISqlGenerationHelper>();
        string function = sql.DelimitIdentifier("configuration_manifest_bootstrap_source",
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global).Schema);
        await using var inspect = migration.Database.GetDbConnection().CreateCommand();
        inspect.CommandText = "SELECT pg_catalog.pg_get_functiondef(pg_catalog.to_regprocedure(@signature))";
        var signature = inspect.CreateParameter();
        signature.ParameterName = "signature";
        signature.Value = function + "(jsonb)";
        inspect.Parameters.Add(signature);
        string original = (string)(await inspect.ExecuteScalarAsync())!;
        try
        {
            await migration.Database.ExecuteSqlRawAsync($"""
                CREATE OR REPLACE FUNCTION {function}(p_scope jsonb) RETURNS jsonb
                    LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                    AS $body$ BEGIN RETURN jsonb_build_object(); END; $body$;
                ALTER FUNCTION {function}(jsonb) OWNER TO {PostgresConfigurationManifestBootstrapContract.OwnerRole};
                """);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => host.ExecuteAsync(
                new ApplyConfigurationManifestCommand(ConfigurationManifestApplicationTestSupport.Source("altered-native")),
                CancellationToken.None));
        }
        finally
        {
            await migration.Database.ExecuteSqlRawAsync(original);
        }
        await AssertNoBootstrapAsync();
    }

    private async Task AssertNoBootstrapAsync()
    {
        await using var context = fixture.CreateDbContext();
        await Assert.That(await context.Tenants.IgnoreQueryFilters().CountAsync()).IsEqualTo(0);
        await Assert.That(await context.TenantSettingOverrides.IgnoreQueryFilters().CountAsync()).IsEqualTo(0);
        await Assert.That(await context.TenantSettingsDocuments.IgnoreQueryFilters().CountAsync()).IsEqualTo(0);
        await Assert.That(await context.ConfigurationManifestOperations.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.ConfigurationManifestTenantResults.IgnoreQueryFilters().CountAsync()).IsEqualTo(0);
        await Assert.That(await context.OutboxMessages.IgnoreQueryFilters().CountAsync()).IsEqualTo(0);
    }

    private sealed class CounterfeitSource : IConfigurationManifestBootstrapSource;
    private sealed class CounterfeitAuthority : IConfigurationManifestBootstrapSourceAuthority
    {
        public ConfigurationManifestBootstrapFile Claim(IConfigurationManifestBootstrapSource source) =>
            new([], new string('c', ConfigurationManifestOperation.DigestLength));
        public Task RequireOriginalAsync(IConfigurationManifestBootstrapSource source, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
