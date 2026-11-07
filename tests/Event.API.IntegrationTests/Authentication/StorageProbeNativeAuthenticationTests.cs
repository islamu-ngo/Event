using System.Security.Claims;
using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Constants;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Features.InstanceOnboarding.Handlers.Queries;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using Explore.Application.Features.TenantStorageSettings.Handlers.Commands;
using Explore.Application.Features.TenantStorageSettings.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Database;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class StorageProbeNativeAuthenticationTests(PostgreSqlContainerFixture postgres)
{
    [Test]
    public async Task CoLocatedPrivacyAuthorityUsesDeclaredRuntimeStorageRights()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var host = new TenantBrandingNativeAuthenticationHost(postgres, Guid.CreateVersion7(), []);
        await host.EnsurePrivacyAuthorityReadyAsync();
        await using var scope = host.Services.CreateAsyncScope();
        var authority = scope.ServiceProvider.GetRequiredService<CoLocatedPrivacyErasureAuthorityDbContext>();
        var selected = await authority.Database.SqlQueryRaw<string>(
            "SELECT current_user::text AS \"Value\" WHERE current_user=session_user").SingleAsync(token);
        await Assert.That(selected).IsEqualTo(postgres.NativeQueueEnrollment.RuntimeLogin);
        await PostgresCoLocatedPrivacyAuthorityStoragePermissions.RequireReadyAsync(
            authority, postgres.NativeQueueEnrollment, token);
        var fence = (IPrivacyIdentityFenceAuthority)scope.ServiceProvider.GetRequiredService<IPrivacyErasureAuthority>();
        await Assert.That(await fence.ExecuteSerializedAsync(_ => Task.FromResult(true), token)).IsTrue();
        await Assert.That(await fence.ExecuteSerializedAsync(_ => Task.FromResult(true), token)).IsTrue();
        await Assert.That(await authority.AuthorityCounters.AsNoTracking().CountAsync(token)).IsEqualTo(1);
        await Assert.That(authority.Database.CurrentTransaction).IsNull();
        var entity = authority.Model.FindEntityType(typeof(PrivacyErasureCounter))!;
        var names = authority.GetService<ISqlGenerationHelper>();
        string table = names.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
        await authority.Database.OpenConnectionAsync(token);
        foreach (string column in new[] { "singleton", "store_id", "recovery_generation" })
        {
            string field = names.DelimitIdentifier(column);
            await using var update = authority.Database.GetDbConnection().CreateCommand();
            update.CommandText = $"UPDATE {table} SET {field}={field} WHERE false";
            var denied = await Assert.ThrowsAsync<PostgresException>(() =>
                update.ExecuteNonQueryAsync(token));
            await Assert.That(denied?.SqlState).IsEqualTo("42501");
        }
        await using var removal = authority.Database.GetDbConnection().CreateCommand();
        removal.CommandText = $"DELETE FROM {table} WHERE false";
        var delete = await Assert.ThrowsAsync<PostgresException>(() =>
            removal.ExecuteNonQueryAsync(token));
        await Assert.That(delete?.SqlState).IsEqualTo("42501");
        foreach (var role in Enum.GetValues<PrimaryDatabaseRole>()
            .Where(role => role is not PrimaryDatabaseRole.Runtime and not PrimaryDatabaseRole.Migrator))
        {
            await using var unrelated = new NpgsqlConnection(PrimaryDatabaseConfiguration.BuildConnectionString(
                postgres.GetNativeQueueOptions(role)).ConnectionString);
            await unrelated.OpenAsync(token);
            await using var query = unrelated.CreateCommand();
            query.CommandText = $"SELECT 1 FROM {table}";
            var denied = await Assert.ThrowsAsync<PostgresException>(async () => await query.ExecuteScalarAsync(token));
            await Assert.That(denied?.SqlState).IsEqualTo("42501");
        }
    }

    [Test]
    [Arguments("partial")]
    [Arguments("excess")]
    [Arguments("grant-option")]
    public async Task CoLocatedPrivacyAuthorityRefusesChangedRightsWithoutCommittingCaller(string change)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var host = new TenantBrandingNativeAuthenticationHost(postgres, Guid.CreateVersion7(), []);
        await host.EnsurePrivacyAuthorityReadyAsync();
        var options = new DbContextOptionsBuilder<CoLocatedPrivacyErasureAuthorityDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureCoLocatedPrivacyErasureAuthority(
            options, postgres.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        await using var authority = new CoLocatedPrivacyErasureAuthorityDbContext(options.Options);
        var names = authority.GetService<ISqlGenerationHelper>();
        var entity = authority.Model.FindEntityType(typeof(PrivacyErasureCounter))!;
        string table = names.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
        string runtime = names.DelimitIdentifier(postgres.NativeQueueEnrollment.RuntimeLogin);
        string column = names.DelimitIdentifier("last_sequence");
        await using var transaction = await authority.Database.BeginTransactionAsync(token);
        var physical = transaction.GetDbTransaction();
        string mutation = change switch
        {
            "partial" => $"REVOKE SELECT ({column}) ON TABLE {table} FROM {runtime}",
            "excess" => $"GRANT DELETE ON TABLE {table} TO {runtime}",
            "grant-option" => $"GRANT SELECT ({column}) ON TABLE {table} TO {runtime} WITH GRANT OPTION",
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };
        await authority.Database.ExecuteSqlRawAsync(mutation, token);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PostgresCoLocatedPrivacyAuthorityStoragePermissions.RequireReadyAsync(
                authority, postgres.NativeQueueEnrollment, token));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PostgresCoLocatedPrivacyAuthorityStoragePermissions.ApplyAsync(
                authority, postgres.NativeQueueEnrollment, token));
        await Assert.That(ReferenceEquals(physical, authority.Database.CurrentTransaction?.GetDbTransaction())).IsTrue();
        bool observed = change == "excess"
            ? await authority.Database.SqlQueryRaw<bool>(
                "SELECT has_table_privilege({0}, {1}, 'DELETE') AS \"Value\"",
                postgres.NativeQueueEnrollment.RuntimeLogin, table).SingleAsync(token)
            : await authority.Database.SqlQueryRaw<bool>(
                "SELECT has_column_privilege({0}, {1}, 'last_sequence', {2}) AS \"Value\"",
                postgres.NativeQueueEnrollment.RuntimeLogin, table,
                change == "partial" ? "SELECT" : "SELECT WITH GRANT OPTION").SingleAsync(token);
        await Assert.That(observed).IsEqualTo(change != "partial");
        await transaction.RollbackAsync(token);
        await PostgresCoLocatedPrivacyAuthorityStoragePermissions.RequireReadyAsync(
            authority, postgres.NativeQueueEnrollment, token);
    }

    [Test]
    [Arguments("instance")]
    [Arguments("setup-pending")]
    [Arguments("setup-absent")]
    [Arguments("tenant-administrator")]
    [Arguments("tenant-instance-administrator")]
    public async Task SignedOriginalBranchesKeepTheirPhysicalScopeAndReleaseAllSourcesForRemoteWork(string branch)
    {
        await using var fixture = await StorageProbeNativeFixture.CreateAsync(postgres, branch);
        await ExecuteAsync(fixture, branch);
        await Assert.That(fixture.Remote.Puts).IsEqualTo(1);
        await Assert.That(fixture.Remote.Deletes).IsEqualTo(1);
        await Assert.That(fixture.Remote.Objects.Single().Version).IsEqualTo("replacement-version");
        await Assert.That(fixture.Remote.SecretReads > 2).IsTrue();
        await Assert.That(fixture.Owner.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("material")]
    [Arguments("revision")]
    public async Task InstanceReadinessDriftCannotBeHiddenByUnchangedTenantTransport(string change)
    {
        await using var fixture = await StorageProbeNativeFixture.CreateAsync(postgres, "instance");
        await using (var seed = postgres.CreateDbContext())
        {
            seed.SecretBindings.AddRange(
                SecretBinding.CreateEnvironmentVariable(SecretDefinitionRegistry.Keys.Storage.AccessKeyId,
                    SecretScope.Tenant, fixture.Tenant, "PROBE_TENANT_ACCESS"),
                SecretBinding.CreateEnvironmentVariable(SecretDefinitionRegistry.Keys.Storage.SecretAccessKey,
                    SecretScope.Tenant, fixture.Tenant, "PROBE_TENANT_SECRET"));
            await seed.SaveChangesAsync();
        }
        fixture.Remote.InstanceCredential = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        fixture.Remote.InstanceRevision = Guid.CreateVersion7().ToString("N");
        fixture.Remote.AfterVersioning = () =>
        {
            if (change == "material")
                fixture.Remote.InstanceCredential = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            else
                fixture.Remote.InstanceRevision = Guid.CreateVersion7().ToString("N");
            return Task.CompletedTask;
        };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ExecuteAsync(fixture, "instance"));
        await Assert.That(fixture.Remote.Puts).IsEqualTo(0);
        await Assert.That(fixture.Remote.Deletes).IsEqualTo(0);
    }

    [Test]
    [Arguments("setting-metadata")]
    [Arguments("setting-replacement")]
    [Arguments("absent-binding")]
    [Arguments("binding-metadata")]
    [Arguments("principal-binding")]
    [Arguments("administrator")]
    [Arguments("principal")]
    [Arguments("support")]
    [Arguments("credential")]
    [Arguments("secret-revision")]
    [Arguments("secret-material")]
    [Arguments("expired")]
    [Arguments("connection-closed")]
    [Arguments("connection-reopened")]
    [Arguments("pending-write")]
    public async Task BeforeActualPutTheSameOriginalNativeAndHumanSourcesMustStillHold(string change)
    {
        await using var fixture = await StorageProbeNativeFixture.CreateAsync(postgres, "instance");
        if (change == "binding-metadata")
        {
            await using var seed = postgres.CreateDbContext();
            seed.SecretBindings.Add(SecretBinding.CreateEnvironmentVariable(
                SecretDefinitionRegistry.Keys.Storage.AccessKeyId, SecretScope.Instance, null, "PROBE_ORIGINAL_ACCESS"));
            await seed.SaveChangesAsync();
        }
        fixture.Remote.AfterVersioning = async () =>
        {
            if (change == "principal") fixture.Request.User = new ClaimsPrincipal(fixture.Request.User);
            if (change == "support") fixture.Request.Request.Headers[SupportAccessHeaderNames.SessionId] = Guid.CreateVersion7().ToString();
            if (change == "credential") fixture.Request.Request.Headers.Authorization = "Bearer replaced";
            if (change == "secret-revision") fixture.Remote.Revision = Guid.CreateVersion7().ToString("N");
            if (change == "secret-material") fixture.Remote.Credential = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            if (change == "expired") fixture.Clock.Set(fixture.Lifetime.ExpiresAt.Add(fixture.Lifetime.ClockSkew).AddTicks(1));
            if (change == "pending-write")
                fixture.Owner.SystemSettings.Add(new() { SettingKey = $"probe.pending.{Guid.CreateVersion7():N}", Value = "false" });
            if (change is "connection-closed" or "connection-reopened")
            {
                await fixture.Owner.Database.CloseConnectionAsync();
                if (change == "connection-reopened") await fixture.Owner.Database.OpenConnectionAsync();
            }
            await using var writer = postgres.CreateDbContext();
            if (change == "setting-metadata")
                await writer.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Storage.Endpoint)
                    .ExecuteUpdateAsync(set => set.SetProperty(row => row.Description, "Replacement validation metadata"));
            if (change == "setting-replacement")
            {
                var old = await writer.SystemSettings.SingleAsync(row => row.SettingKey == GovernanceSettingKeys.Storage.Endpoint);
                writer.Remove(old);
                await writer.SaveChangesAsync();
                writer.SystemSettings.Add(new()
                {
                    SettingKey = old.SettingKey,
                    Value = old.Value,
                    IsLocked = old.IsLocked,
                    ValueType = old.ValueType,
                    Description = old.Description,
                    Category = old.Category
                });
                await writer.SaveChangesAsync();
            }
            if (change == "absent-binding")
            {
                writer.SecretBindings.Add(SecretBinding.CreateEnvironmentVariable(
                    SecretDefinitionRegistry.Keys.Storage.SecretAccessKey, SecretScope.Instance, null, "PROBE_CHANGED_SECRET"));
                await writer.SaveChangesAsync();
            }
            if (change == "binding-metadata")
                await writer.SecretBindings.Where(row => row.SettingKey == SecretDefinitionRegistry.Keys.Storage.AccessKeyId)
                    .ExecuteUpdateAsync(set => set.SetProperty(row => row.LastValidationError, "Changed metadata only"));
            if (change == "principal-binding")
                await writer.UserExternalLogins.Where(row => row.Id == fixture.Binding).ExecuteDeleteAsync();
            if (change == "administrator")
                await writer.PlatformUserRoles.Where(row => row.Id == fixture.Grant).ExecuteDeleteAsync();
        };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ExecuteAsync(fixture, "instance"));
        await Assert.That(fixture.Remote.Puts).IsEqualTo(0);
        await Assert.That(fixture.Remote.Deletes).IsEqualTo(0);
    }

    [Test]
    [Arguments("setup-pending", "replace")]
    [Arguments("setup-pending", "complete")]
    [Arguments("setup-pending", "secret")]
    [Arguments("setup-pending", "lock")]
    [Arguments("setup-absent", "insert")]
    [Arguments("tenant-administrator", "revoke")]
    [Arguments("tenant-instance-administrator", "revoke")]
    [Arguments("tenant-administrator", "replace-alternative")]
    [Arguments("tenant-administrator", "tenant-binding")]
    [Arguments("tenant-instance-administrator", "tenant-setting")]
    public async Task ActualSetupAndTenantAlternativesCannotRefreshTheirOriginalAuthority(string branch, string change)
    {
        await using var fixture = await StorageProbeNativeFixture.CreateAsync(postgres, branch);
        fixture.Remote.AfterVersioning = async () =>
        {
            if (change == "secret") fixture.Request.Request.Headers["X-Setup-Secret"] = Guid.CreateVersion7().ToString("N");
            if (change == "lock") fixture.Services.GetRequiredService<ISetupSecretProvider>().Lock();
            await using var writer = postgres.CreateDbContext();
            if (change == "tenant-binding")
            {
                writer.SecretBindings.Add(SecretBinding.CreateEnvironmentVariable(
                    SecretDefinitionRegistry.Keys.Storage.AccessKeyId, SecretScope.Tenant, fixture.Tenant, "PROBE_TENANT_ACCESS"));
                await writer.SaveChangesAsync();
            }
            if (change == "tenant-setting")
            {
                writer.Set<TenantSetting>().Add(new()
                {
                    TenantId = fixture.Tenant,
                    Tenant = null!,
                    SettingKey = GovernanceSettingKeys.Storage.Endpoint,
                    Value = "\"https://probe.example.test\""
                });
                await writer.SaveChangesAsync();
            }
            if (change is "replace" or "insert")
            {
                await writer.InstanceBootstrapStates.ExecuteDeleteAsync();
                writer.InstanceBootstrapStates.Add(InstanceBootstrapState.CreateInteractivePending(
                    Guid.CreateVersion7(), DeploymentMode.SingleTenant, fixture.Clock.GetUtcNow().UtcDateTime));
                await writer.SaveChangesAsync();
            }
            if (change == "complete")
            {
                var pending = await writer.InstanceBootstrapStates.SingleAsync();
                pending.CompleteInteractive(fixture.Actor, fixture.Clock.GetUtcNow().UtcDateTime);
                await writer.SaveChangesAsync();
            }
            if (change is "revoke" or "replace-alternative")
            {
                if (branch == "tenant-administrator")
                    await writer.TenantUserRoleGrants.Where(row => row.Id == fixture.Grant).ExecuteDeleteAsync();
                else await writer.PlatformUserRoles.Where(row => row.Id == fixture.Grant).ExecuteDeleteAsync();
                if (change == "replace-alternative")
                {
                    writer.PlatformUserRoles.Add(new()
                    {
                        Id = Guid.CreateVersion7(),
                        UserId = fixture.Actor,
                        User = null!,
                        RoleId = (int)RoleEnum.Admin,
                        Role = null!,
                        GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
                    });
                    await writer.SaveChangesAsync();
                }
            }
        };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ExecuteAsync(fixture, branch));
        await Assert.That(fixture.Remote.Puts).IsEqualTo(0);
    }

    [Test]
    [Arguments("secret-provider")]
    [Arguments("cleanup")]
    [Arguments("return")]
    public async Task ReleasedProviderIntervalsCannotAcquireReplacementSources(string stage)
    {
        await using var fixture = await StorageProbeNativeFixture.CreateAsync(postgres, "instance");
        bool changed = false;
        async Task Change()
        {
            if (changed) return;
            changed = true;
            await using var writer = postgres.CreateDbContext();
            await writer.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Storage.Region)
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.Description, "Changed after release"));
        }
        if (stage == "secret-provider") fixture.Remote.DuringSecretResolution = Change;
        if (stage == "cleanup") fixture.Remote.AfterPut = Change;
        if (stage == "return") fixture.Remote.AfterDelete = Change;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ExecuteAsync(fixture, "instance"));
        await Assert.That(fixture.Remote.Puts).IsEqualTo(stage == "secret-provider" ? 0 : 1);
        await Assert.That(fixture.Remote.Deletes).IsEqualTo(stage == "return" ? 1 : 0);
    }

    private static Task ExecuteAsync(StorageProbeNativeFixture fixture, string branch) =>
        fixture.InRequestAsync(async () =>
        {
            if (branch.StartsWith("tenant", StringComparison.Ordinal))
                await Assert.That((await ActivatorUtilities.CreateInstance<TestTenantStorageProviderCommandHandler>(fixture.Services)
                    .ExecuteAsync(new TestTenantStorageProviderCommand { UserId = fixture.Actor }, default)).IsAvailable).IsTrue();
            else if (branch.StartsWith("setup", StringComparison.Ordinal))
                await Assert.That((await ActivatorUtilities.CreateInstance<TestInstanceStorageProviderQueryHandler>(fixture.Services)
                    .QueryAsync(new TestInstanceStorageProviderDuringSetupQuery(), default)).IsAvailable).IsTrue();
            else
                await Assert.That((await ActivatorUtilities.CreateInstance<TestInstanceStorageProviderQueryHandler>(fixture.Services)
                    .QueryAsync(new TestInstanceStorageProviderQuery { UserId = fixture.Actor }, default)).IsAvailable).IsTrue();
        });
}
