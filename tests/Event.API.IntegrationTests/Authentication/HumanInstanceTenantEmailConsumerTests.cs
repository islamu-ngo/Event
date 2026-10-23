using System.Data.Common;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.InstanceAdmin.Handlers.Commands;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using Explore.Application.Features.Settings.Handlers.Commands;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed class HumanInstanceTenantEmailConsumerTests
{
    [Test]
    [Arguments("set", "unchanged")]
    [Arguments("lock", "unchanged")]
    [Arguments("unlock", "unchanged")]
    [Arguments("set", "revoked-before-owner")]
    [Arguments("lock", "revoked-before-owner")]
    [Arguments("unlock", "revoked-before-owner")]
    [Arguments("set", "terminal-expiry")]
    [Arguments("lock", "terminal-expiry")]
    [Arguments("unlock", "terminal-expiry")]
    [Arguments("batch", "unchanged")]
    [Arguments("batch", "revoked-before-owner")]
    [Arguments("batch", "terminal-expiry")]
    public async Task TypedEmailConsumerUsesOriginalDedicatedSourceAndExactEffect(string operation, string change)
    {
        await using var fixture = await SmtpAuthenticatedFixture.CreateAsync(tenantAdministrator: true);
        const string key = GovernanceSettingKeys.Email.SmtpPort;
        Guid settingId = Guid.CreateVersion7();
        await using (var seed = fixture.CreateIndependentContext())
        {
            seed.TenantSettingOverrides.Add(new TenantSetting
            {
                Id = settingId,
                TenantId = fixture.TenantId,
                Tenant = null!,
                SettingKey = key,
                Value = "2525",
                IsLocked = operation == "unlock",
                CreatedBy = fixture.Subject.PrincipalId,
                CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime
            });
            await seed.SaveChangesAsync();
        }
        var interception = new ConsumerInterception();
        await fixture.ExecuteAsync(tenantOwner: true, async services =>
        {
            interception.Owner = services.GetRequiredService<ExploreDbContext>();
            interception.BeforeOwner = change == "revoked-before-owner" ? async token =>
            {
                await using var writer = fixture.CreateIndependentContext();
                if (operation == "batch")
                    await writer.TenantUserRoleGrants.Where(row => row.TenantId == fixture.TenantId
                        && row.TenantUser.UserId == fixture.Subject.PrincipalId).ExecuteDeleteAsync(token);
                else
                    await writer.PlatformUserRoles.Where(row => row.UserId == fixture.Subject.PrincipalId)
                        .ExecuteDeleteAsync(token);
            }
            : null;
            interception.AfterEffect = change == "terminal-expiry" ? () =>
                services.GetRequiredService<IHumanActionCommitFinalizer>().Register(_ =>
                {
                    fixture.Clock.Advance(fixture.Lifetime.ExpiresAt.Add(fixture.Lifetime.ClockSkew)
                        .AddTicks(1) - fixture.Clock.GetUtcNow());
                    return Task.CompletedTask;
                }, _ => { }) : null;
            var attempt = ExecuteAsync();
            async Task<bool> ExecuteAsync()
            {
                if (operation == "batch")
                    return (await ActivatorUtilities.CreateInstance<UpdateSettingBatchCommandHandler>(services)
                        .ExecuteAsync(new UpdateSettingBatchCommand
                        {
                            Category = "Email",
                            Scope = Explore.Domain.Settings.SettingScope.Tenant,
                            Values = new Dictionary<string, string> { [key] = "2526" }
                        }, CancellationToken.None)).Success;
                var result = await (operation switch
                {
                    "set" => ActivatorUtilities.CreateInstance<SetInstanceTenantSettingCommandHandler>(services)
                        .ExecuteAsync(new SetInstanceTenantSettingCommand(fixture.TenantId, key, "2526"), CancellationToken.None),
                    "lock" => ActivatorUtilities.CreateInstance<LockInstanceTenantSettingCommandHandler>(services)
                        .ExecuteAsync(new LockInstanceTenantSettingCommand(fixture.TenantId, key), CancellationToken.None),
                    _ => ActivatorUtilities.CreateInstance<UnlockInstanceTenantSettingCommandHandler>(services)
                        .ExecuteAsync(new UnlockInstanceTenantSettingCommand(fixture.TenantId, key), CancellationToken.None)
                });
                return result.IsSuccess;
            }
            if (change == "unchanged")
                await Assert.That(await attempt).IsTrue();
            else
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => attempt);
            await Assert.That(interception.Owner.Database.CurrentTransaction).IsNull();
        }, interception, commandInterceptor: new EffectInterception(interception, settingId));
        await Assert.That(interception.BeforeObserved).IsEqualTo(change == "revoked-before-owner");
        await Assert.That(interception.AfterObserved).IsEqualTo(change == "terminal-expiry");
        await using var observer = fixture.CreateIndependentContext();
        var persisted = await observer.TenantSettingOverrides.AsNoTracking()
            .SingleAsync(row => row.TenantId == fixture.TenantId && row.SettingKey == key);
        await Assert.That(persisted.Value).IsEqualTo(operation is "set" or "batch" && change == "unchanged" ? "2526" : "2525");
        await Assert.That(persisted.IsLocked).IsEqualTo(change == "unchanged"
            ? operation == "lock" : operation == "unlock");
    }

    private sealed class ConsumerInterception : DbTransactionInterceptor
    {
        internal ExploreDbContext? Owner { get; set; }
        internal Func<CancellationToken, Task>? BeforeOwner { get; set; }
        internal Action? AfterEffect { get; set; }
        internal bool BeforeObserved { get; private set; }
        internal bool AfterObserved { get; private set; }

        internal void ObserveEffect(DbCommand command, CommandEventData data, Guid settingId)
        {
            if (ReferenceEquals(data.Context, Owner) && Owner?.Database.CurrentTransaction is not null
                && (command.CommandText.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
                    || command.CommandText.TrimStart().StartsWith("INSERT", StringComparison.OrdinalIgnoreCase))
                && command.Parameters.Cast<DbParameter>().Any(parameter =>
                    Guid.TryParse(parameter.Value?.ToString(), out Guid target) && target == settingId)
                && AfterEffect is { } observe)
            {
                AfterEffect = null;
                AfterObserved = true;
                observe();
            }
        }

        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection, TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            if (ReferenceEquals(eventData.Context, Owner) && BeforeOwner is { } observe)
            {
                BeforeOwner = null;
                BeforeObserved = true;
                await observe(cancellationToken);
            }
            return result;
        }
    }

    private sealed class EffectInterception(ConsumerInterception state, Guid settingId) : DbCommandInterceptor
    {
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            state.ObserveEffect(command, eventData, settingId);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            state.ObserveEffect(command, eventData, settingId);
            return ValueTask.FromResult(result);
        }
    }
}
