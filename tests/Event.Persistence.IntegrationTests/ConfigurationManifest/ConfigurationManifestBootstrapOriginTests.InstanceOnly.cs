using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Compilation;
using Explore.Application.Features.ConfigurationManifest.Ingestion;
using Explore.Application.Features.ConfigurationManifest.Validation;
using Explore.Domain;
using Explore.Domain.Settings.Definitions;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.ConfigurationManifest;

public sealed partial class ConfigurationManifestBootstrapOriginTests
{
    [Test]
    [Arguments(ConfigurationManifestMode.Off, false)]
    [Arguments(ConfigurationManifestMode.Off, true)]
    [Arguments(ConfigurationManifestMode.ValidateOnly, false)]
    [Arguments(ConfigurationManifestMode.ValidateOnly, true)]
    public async Task InstanceOnlyStartup_OffDoesNotApplyAndValidateOnlyReadsOriginalBytes(
        ConfigurationManifestMode mode, bool explicitEmptyTenants)
    {
        await fixture.ResetAsync();
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var host = ConfigurationManifestBootstrapTestHost.ForPostgres(fixture, mode);
        await using var observer = fixture.CreateDbContext();
        var before = await InstanceOnlyBootstrapAssertions.ReadThemeAsync(observer, token);
        byte[] bytes = InstanceOnlyBootstrapAssertions.Artifact(explicitEmptyTenants);
        await File.WriteAllBytesAsync(host.ArtifactPath, bytes, token);

        ConfigurationManifestApplyPlan? observed = null;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var owner = (ConfigurationManifestBootstrapOwnerFactory)scope.ServiceProvider
                .GetRequiredService<IConfigurationManifestBootstrapOwnerFactory>();
            owner.AfterOriginalObservation = async (context, plan, ct) =>
            {
                await AssertTenantlessGlobalSessionAsync(context, ct);
                observed = plan;
            };
            await scope.ServiceProvider.GetRequiredService<IConfigurationManifestStartupRunner>().RunAsync(token);
        }

        if (mode == ConfigurationManifestMode.ValidateOnly)
        {
            await Assert.That(observed).IsNotNull();
            await Assert.That(observed!.Mode).IsEqualTo(ConfigurationManifestMode.ValidateOnly);
            await Assert.That(observed.Digest).IsEqualTo(InstanceOnlyBootstrapAssertions.Digest(bytes));
            await Assert.That(observed.Tenants.Length).IsEqualTo(0);
        }
        else
        {
            await Assert.That(observed).IsNull();
        }

        var after = await InstanceOnlyBootstrapAssertions.ReadThemeAsync(observer, token);
        await Assert.That((after?.Id, after?.Value, after?.UpdatedAt))
            .IsEqualTo((before?.Id, before?.Value, before?.UpdatedAt));
        await AssertNoBootstrapAsync();
        await InstanceOnlyBootstrapAssertions.AssertNoTenantGraphAsync(observer, token);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task InstanceOnlyStartup_PostgresCommitsAndReplaysWithoutTenantAuthority(bool explicitEmptyTenants)
    {
        await fixture.ResetAsync();
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var host = ConfigurationManifestBootstrapTestHost.ForPostgres(fixture);
        await using var observer = fixture.CreateDbContext();
        await InstanceOnlyBootstrapAssertions.AssertApplyAndReplayAsync(host, observer, explicitEmptyTenants,
            OutboxMessageStatus.Pending, AssertTenantlessGlobalSessionAsync, token);
    }

    private async Task AssertTenantlessGlobalSessionAsync(ExploreDbContext context, CancellationToken token)
    {
        await Assert.That(context.TenantFilterTenantId).IsNull();
        await Assert.That(context.IsTenantFilterBypassed).IsFalse();
        await using var probe = context.Database.GetDbConnection().CreateCommand();
        probe.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        probe.CommandText = """
            SELECT current_user::text
            WHERE NULLIF(pg_catalog.current_setting('app.current_tenant_id', true), '') IS NULL
            """;
        await Assert.That((string?)await probe.ExecuteScalarAsync(token))
            .IsEqualTo(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global).Username);
    }
}

public sealed partial class ConfigurationManifestAtomicPersistenceTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task InstanceOnlyStartup_SqliteCommitsAndReplaysWithoutTenantWrites(bool explicitEmptyTenants)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        string databasePath = TemporaryDatabasePath();
        try
        {
            var options = CreateOptions(databasePath);
            await InitializeAsync(options);
            var factory = new ConfigurationManifestApplicationTestSupport.TestDbContextFactory(options);
            await using var host = ConfigurationManifestApplicationTestSupport.CreateHandler(factory);
            await using var observer = factory.CreateDbContext();
            await InstanceOnlyBootstrapAssertions.AssertApplyAndReplayAsync(host, observer, explicitEmptyTenants,
                OutboxMessageStatus.Completed, assertSelection: null, token: token);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
}

internal static class InstanceOnlyBootstrapAssertions
{
    private const string ManifestName = "instance-only-startup";
    private static string ThemeKey => AppearanceSettingDefinitions.DefaultThemeMode.Key;

    // Keep exact file bytes: the DTO serializer can normalize omission to an empty array.
    internal static byte[] Artifact(bool explicitEmptyTenants)
    {
        byte[] bytes = Encoding.UTF8.GetBytes($$"""
        {
          "$schema": "{{ConfigurationManifestContractMetadata.SchemaId}}",
          "apiVersion": "{{ConfigurationManifestContractMetadata.ApiVersion}}",
          "kind": "{{ConfigurationManifestContractMetadata.Kind}}",
          "metadata": { "name": "{{ManifestName}}" },
          "spec": {
            "instance": { "settings": { "{{ThemeKey}}": "dark" }, "documents": {}, "legalDocuments": {} }{{(explicitEmptyTenants ? ", \"tenants\": []" : "")}}
          }
        }
        """);
        var manifest = JsonSerializer.Deserialize(
            bytes, ConfigurationPortabilityJsonContext.Default.ConfigurationManifestV1Alpha2)
            ?? throw new InvalidOperationException("The startup fixture artifact decoded to null.");
        var validation = ConfigurationManifestValidator.Validate(manifest);
        if (!validation.IsValid)
            throw new InvalidOperationException("Startup fixture contract: " + string.Join(
                "; ", validation.Errors.Select(error => $"{error.Code}:{error.Path}")));
        return bytes;
    }

    internal static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    internal static Task<SystemSetting?> ReadThemeAsync(ExploreDbContext context, CancellationToken token) =>
        context.SystemSettings.AsNoTracking().SingleOrDefaultAsync(row => row.SettingKey == ThemeKey, token);

    internal static async Task AssertApplyAndReplayAsync(ConfigurationManifestBootstrapTestHost host, ExploreDbContext observer,
        bool explicitEmptyTenants, OutboxMessageStatus effectStatus, Func<ExploreDbContext, CancellationToken, Task>? assertSelection,
        CancellationToken token)
    {
        await observer.SystemSettings.Where(row => row.SettingKey == ThemeKey).ExecuteDeleteAsync(token);
        byte[] original = Artifact(explicitEmptyTenants);
        var first = await RunAsync(original);
        await Assert.That(first.InstanceChangedSettingKeyNames).IsEquivalentTo([ThemeKey]);
        await Assert.That(first.InstanceSectionDigest).IsNotNull();
        await Assert.That(first.BootstrapGeneration).IsEqualTo(1);
        var theme = await ReadThemeAsync(observer, token);
        await Assert.That(theme).IsNotNull();
        await Assert.That(theme!.Value).IsEqualTo("\"dark\"");
        await Assert.That(theme.CreatedBy).IsNull();
        await Assert.That(theme.UpdatedBy).IsNull();

        var effect = await observer.OutboxMessages.IgnoreQueryFilters().AsNoTracking().SingleAsync(token);
        await Assert.That(first.EffectOutboxId).IsEqualTo(effect.Id);
        await Assert.That(effect.AggregateId).IsEqualTo(first.Id);
        await Assert.That(effect.Status).IsEqualTo(effectStatus);
        var instructions = ConfigurationManifestEffectPayload.Read(effect.Payload, effect.CreatedAt);
        await Assert.That(instructions.InstanceSettingKeys).IsEquivalentTo([ThemeKey]);
        await Assert.That(instructions.Tenants.Length).IsEqualTo(0);
        await Assert.That(instructions.NotificationTimeUtc).IsEqualTo(first.StartedAt);

        byte[] alternate = Artifact(!explicitEmptyTenants);
        await Assert.That(Digest(alternate)).IsNotEqualTo(Digest(original));
        foreach (byte[] content in new[] { original, alternate })
        {
            var replay = await RunAsync(content);
            await Assert.That(replay.Id).IsNotEqualTo(first.Id);
            await Assert.That(replay.BootstrapGeneration).IsEqualTo(first.BootstrapGeneration);
            await Assert.That(replay.InstanceSectionDigest).IsEqualTo(first.InstanceSectionDigest);
            await Assert.That(replay.InstanceChangedSettingKeyNames.Count).IsEqualTo(0);
            await Assert.That(replay.InstanceChangedDocumentKeyNames.Count).IsEqualTo(0);
            await Assert.That(replay.EffectOutboxId).IsNull();
        }

        var retained = await ReadThemeAsync(observer, token);
        await Assert.That(retained!.Id).IsEqualTo(theme.Id);
        await Assert.That(retained.Value).IsEqualTo(theme.Value);
        await Assert.That(retained.UpdatedAt).IsEqualTo(theme.UpdatedAt);
        await Assert.That(await observer.OutboxMessages.IgnoreQueryFilters().CountAsync(token)).IsEqualTo(1);
        await Assert.That(await observer.ConfigurationManifestOperations.CountAsync(token)).IsEqualTo(3);

        async Task<ConfigurationManifestOperation> RunAsync(byte[] bytes)
        {
            await File.WriteAllBytesAsync(host.ArtifactPath, bytes, token);
            await using var scope = host.Services.CreateAsyncScope();
            if (assertSelection is not null)
            {
                var owner = (ConfigurationManifestBootstrapOwnerFactory)scope.ServiceProvider
                    .GetRequiredService<IConfigurationManifestBootstrapOwnerFactory>();
                owner.AfterOriginalObservation = (context, _, ct) => assertSelection(context, ct);
            }
            try
            {
                await scope.ServiceProvider.GetRequiredService<IConfigurationManifestStartupRunner>().RunAsync(token);
            }
            catch (ConfigurationManifestStartupException failure)
            {
                Console.WriteLine($"BOOTSTRAP_FAILURE_CODE={failure.FailureCode}");
                throw;
            }
            var operation = await observer.ConfigurationManifestOperations.AsNoTracking()
                .OrderByDescending(row => row.Id).FirstAsync(token);
            await Assert.That(operation.Mode).IsEqualTo(ConfigurationManifestAuditMode.Bootstrap);
            await Assert.That(operation.Status).IsEqualTo(ConfigurationManifestOperationStatus.Applied);
            await Assert.That(operation.ManifestName).IsEqualTo(ManifestName);
            await Assert.That(operation.Digest).IsEqualTo(Digest(bytes));
            await Assert.That((operation.RequestedTenantCount, operation.CreatedTenantCount,
                operation.SkippedExistingTenantCount, operation.FailedTenantCount)).IsEqualTo((0, 0, 0, 0));
            await AssertNoTenantGraphAsync(observer, token);
            return operation;
        }
    }

    internal static async Task AssertNoTenantGraphAsync(ExploreDbContext context, CancellationToken token)
    {
        // Observer reads deliberately include all tenants and soft-deleted rows.
        await Assert.That(await context.Tenants.IgnoreQueryFilters().AnyAsync(token)).IsFalse();
        await Assert.That(await context.Set<EventDiscoveryRevision>().IgnoreQueryFilters().AnyAsync(token)).IsFalse();
        await Assert.That(await context.TenantSettingOverrides.IgnoreQueryFilters().AnyAsync(token)).IsFalse();
        await Assert.That(await context.TenantSettingsDocuments.IgnoreQueryFilters().AnyAsync(token)).IsFalse();
        await Assert.That(await context.TenantOnboardingStates.IgnoreQueryFilters().AnyAsync(token)).IsFalse();
        await Assert.That(await context.TenantUsers.IgnoreQueryFilters().AnyAsync(token)).IsFalse();
        await Assert.That(await context.TenantUserRoleGrants.IgnoreQueryFilters().AnyAsync(token)).IsFalse();
        await Assert.That(await context.PlatformUserRoles.IgnoreQueryFilters().AnyAsync(token)).IsFalse();
        await Assert.That(await context.ConfigurationManifestTenantResults.IgnoreQueryFilters().AnyAsync(token)).IsFalse();
    }
}
