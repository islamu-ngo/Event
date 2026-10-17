using System.Data;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Settings;
using Explore.Application.Features.Settings.Handlers.Commands;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Explore.Persistence;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class MixedSettingsNativeAuthenticationTests(PostgreSqlContainerFixture postgres)
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SignedLocalMixedBatchRetainsOriginalGlobalOwnerAndSelectedTarget(bool tenant)
    {
        await using var fixture = await ResolverConfigurationNativeFixture.CreateAsync(postgres, mixed: true, tenantScope: tenant);
        string key = tenant ? GovernanceSettingKeys.Email.SmtpPort : GovernanceSettingKeys.TenantDelegation.LockSmtp;
        string other = tenant ? "unregistered.mixed.key" : GovernanceSettingKeys.TenantDelegation.LockAnalytics;
        await using (var seed = postgres.CreateDbContext())
        {
            foreach (string settingKey in new[] { key, GovernanceSettingKeys.TenantDelegation.LockSmtp }
                .Concat(tenant ? [] : new[] { other }).Distinct(StringComparer.Ordinal))
            {
                string value = settingKey == GovernanceSettingKeys.Email.SmtpPort ? "2525" : tenant ? "false" : "true";
                var row = await seed.SystemSettings.SingleOrDefaultAsync(setting => setting.SettingKey == settingKey);
                if (row is null)
                {
                    row = new SystemSetting
                    {
                        Id = Guid.CreateVersion7(), SettingKey = settingKey, Value = value,
                        ValueType = settingKey == GovernanceSettingKeys.Email.SmtpPort
                            ? SettingValueType.Integer : SettingValueType.Boolean,
                        Category = settingKey == GovernanceSettingKeys.Email.SmtpPort ? "Email" : "TenantDelegation",
                        CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime, CreatedBy = fixture.Actor
                    };
                    seed.SystemSettings.Add(row);
                }
                row.Value = value;
                row.IsLocked = false;
            }
            if (tenant)
                seed.TenantSettingOverrides.Add(new TenantSetting
                {
                    Id = Guid.CreateVersion7(), TenantId = fixture.Tenant, Tenant = null!,
                    SettingKey = key, Value = "2525", CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime,
                    CreatedBy = fixture.Actor
                });
            await seed.SaveChangesAsync();
        }
        await fixture.InRequestAsync(async () =>
        {
            var owner = fixture.Owner;
            if (owner.Database.GetDbConnection().State != ConnectionState.Open)
                await owner.Database.OpenConnectionAsync();
            var connection = (NpgsqlConnection)owner.Database.GetDbConnection();
            int backend = connection.ProcessID;
            await using (var login = connection.CreateCommand())
            {
                login.CommandText = "SELECT session_user::text";
                await Assert.That((string)(await login.ExecuteScalarAsync())!).IsEqualTo(postgres.NativeQueueEnrollment.GlobalLogin);
            }
            await Assert.That(fixture.Services.GetRequiredService<ExploreDbContextExecutionLease>().PolicyPurpose)
                .IsEqualTo(PolicyMutationPurpose.MixedSettings);
            var command = new UpdateSettingBatchCommand
            {
                Category = tenant ? "Email" : "TenantDelegation",
                Scope = tenant ? SettingScope.Tenant : SettingScope.Instance,
                Mode = tenant ? BatchUpdateMode.BestEffort : BatchUpdateMode.Strict,
                Values = new Dictionary<string, string> { [key] = tenant ? "2526" : "false", [other] = "false" }
            };
            var response = await ActivatorUtilities.CreateInstance<UpdateSettingBatchCommandHandler>(fixture.Services)
                .ExecuteAsync(command, CancellationToken.None);
            await Assert.That(response.Success).IsTrue();
            await Assert.That(response.Results.Single(result => result.Key == key).Applied).IsTrue();
            await Assert.That(response.Results.Single(result => result.Key == other).Applied).IsEqualTo(!tenant);
            await Assert.That(ReferenceEquals(owner, fixture.Services.GetRequiredService<ExploreDbContext>())).IsTrue();
            await Assert.That(ReferenceEquals(connection, owner.Database.GetDbConnection())).IsTrue();
            await Assert.That(connection.ProcessID).IsEqualTo(backend);
            await Assert.That(owner.Database.CurrentTransaction).IsNull();
        });
        await using var independent = postgres.CreateDbContext();
        if (tenant)
        {
            var stored = await independent.TenantSettingOverrides.AsNoTracking().SingleAsync(row =>
                row.TenantId == fixture.Tenant && row.SettingKey == key);
            await Assert.That(stored.Value).IsEqualTo("2526");
            await Assert.That(stored.UpdatedBy).IsEqualTo(fixture.Actor);
            await Assert.That(await independent.TenantSettingOverrides.AnyAsync(row =>
                row.TenantId == fixture.Tenant && row.SettingKey == other)).IsFalse();
        }
        else
        {
            await Assert.That(await independent.SystemSettings.Where(row => row.SettingKey == key || row.SettingKey == other)
                .CountAsync(row => row.Value == "false" && row.UpdatedBy == fixture.Actor)).IsEqualTo(2);
        }
    }
}
