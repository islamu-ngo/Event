using System.Security.Claims;
using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Constants;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.InstanceOnboarding.Handlers.Queries;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using Explore.Application.Features.TenantStorageSettings.Handlers.Commands;
using Explore.Application.Features.TenantStorageSettings.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class StorageProbeNativeAuthenticationTests(PostgreSqlContainerFixture postgres)
{
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
