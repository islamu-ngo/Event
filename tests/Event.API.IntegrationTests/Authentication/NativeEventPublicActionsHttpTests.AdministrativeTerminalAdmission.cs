using Explore.Application.Authentication;
using Explore.Application.Constants;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventPublicActionsHttpTests
{
    public static IEnumerable<(int Owner, string Change)> AdministrativeTerminalCases()
    {
        for (int owner = 0; owner < 16; owner++)
        {
            foreach (string change in new[]
            {
                "unchanged_instance", "unchanged_tenant", "binding", "stamp", "grant", "tenant_grant",
                "request", "route", "credential_after_reads"
            })
                yield return (owner, change);
            // Only these original requests carry trusted tenant facts. Other
            // definitions retain their original missing-facts support denial.
            if (owner is 2 or 8 or 13 or 14 or 15)
                foreach (string change in new[] { "support_active", "support_revoke", "support_extend_after_reads" })
                    yield return (owner, change);
            else
                yield return (owner, "support_missing_facts");
        }
    }

    [Test]
    [MethodDataSource(nameof(AdministrativeTerminalCases))]
    public async Task AdministrativeMutationRequiresOriginalSourcesAndLifetimeThroughPhysicalCommit(int owner, string change)
    {
        await using var fixture = await RetainedAdministrativeFixture.CreateAsync();
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var request = (AdministrativeRequest)scope;
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<ExploreDbContext>();
        Guid userId = fixture.Data.OutsiderId;
        bool session = owner is >= 7 and <= 12;
        int kind = owner == 0 ? 0 : session ? 2 : 1;
        Guid targetId;
        Guid parentId;
        Guid stamp;
        if (owner >= 13)
        {
            targetId = await SeedDeletionTemplate(db, owner != 13);
            parentId = owner == 13 ? targetId
                : await db.EventSessionTemplates.Where(row => row.Id == targetId)
                    .Select(row => row.EventTemplateId).SingleAsync();
            stamp = owner == 13 ? Guid.Empty : (await TemplateUpdateState(db, true, targetId)).Stamp;
        }
        else if (owner == 0)
        {
            targetId = await SeedOwnedDefinition(db, fixture.Data.PublicId, 0);
            parentId = fixture.Data.PublicId;
            stamp = (await DefinitionState(db, 0, targetId)).Stamp;
        }
        else
        {
            var target = await PrepareValueDefinition(db, fixture.Data.PublicId, session, owner is 6 or 12);
            targetId = target.Id;
            parentId = target.ParentId;
            stamp = (await DefinitionState(db, kind, targetId)).Stamp;
        }
        bool platform = change is not ("unchanged_tenant" or "tenant_grant");
        Guid grantId;
        if (platform)
            grantId = await GrantTemplateAdministrator(db, userId);
        else
        {
            var member = await db.TenantUsers.SingleAsync(row =>
                row.UserId == userId && row.TenantId == PlatformDefaults.DefaultTenantId);
            grantId = Guid.CreateVersion7();
            db.TenantUserRoleGrants.Add(new TenantUserRoleGrant
            {
                Id = grantId,
                TenantId = member.TenantId,
                Tenant = null!,
                TenantUserId = member.Id,
                TenantUser = member,
                RoleId = (int)RoleEnum.TenantAdmin,
                Role = null!,
                RoleScopeId = (int)RoleScopeEnum.Tenant,
                GrantedAt = fixture.Local.Clock.GetUtcNow().UtcDateTime
            });
            await db.SaveChangesAsync();
        }
        Guid? supportId = null;
        DateTimeOffset supportExpiry = fixture.Local.Clock.GetUtcNow().AddMinutes(5);
        if (change.StartsWith("support_", StringComparison.Ordinal))
        {
            foreach (string settingKey in new[] { GovernanceSettingKeys.SupportAccess.Enabled, GovernanceSettingKeys.SupportAccess.AllowWriteMode })
            {
                var setting = await db.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == settingKey);
                if (setting is null) db.SystemSettings.Add(new SystemSetting { SettingKey = settingKey, Value = "true" });
                else setting.Value = "true";
            }
            var support = SupportAccessSession.Start(userId, PlatformDefaults.DefaultTenantId,
                SupportAccessModeEnum.Write, "maintenance", "Administrative mutation admission", "fixture-ticket",
                fixture.Local.Clock.GetUtcNow().AddMinutes(-1), supportExpiry);
            db.SupportAccessSessions.Add(support);
            await db.SaveChangesAsync();
            supportId = support.Id;
            request.Context.Request.Headers[SupportAccessHeaderNames.SessionId] = support.Id.ToString("D");
            await Assert.That((await services.GetRequiredService<ISupportAccessSessionService>().GetCurrentAsync()).IsActive).IsTrue();
        }
        string key = "terminal-" + Guid.CreateVersion7().ToString("N");
        var originalIdentity = await fixture.Local.ReadIdentityAsync();
        await services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(default);
        bool bodyObserved = false;
        int bodies = 0;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var physicalOwner = new AdministrativeAfterBodyOwner(
            (ISerializableAttemptOwner)new EfCoreUnitOfWork(db), async token =>
        {
            bodies++;
            bodyObserved = await AdministrativeEffectAsync(db, owner, targetId, key, token);
            await Assert.That(bodyObserved).IsTrue();
            switch (change)
            {
                case "binding":
                    await db.UserExternalLogins.Where(row => row.Id == fixture.Local.Binding.ExternalLoginId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.ProviderKey, Guid.CreateVersion7().ToString("N")), token);
                    break;
                case "stamp":
                    await db.Set<LocalIdentityUser>().Where(row => row.Id == userId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.SecurityStamp, Guid.CreateVersion7().ToString("N")), token);
                    break;
                case "grant":
                    await db.PlatformUserRoles.Where(row => row.Id == grantId).ExecuteDeleteAsync(token);
                    break;
                case "tenant_grant":
                    await db.TenantUserRoleGrants.Where(row => row.Id == grantId).ExecuteUpdateAsync(
                        update => update.SetProperty(row => row.RevokedAt, fixture.Local.Clock.GetUtcNow().UtcDateTime), token);
                    break;
                case "request":
                    request.Context.User = new System.Security.Claims.ClaimsPrincipal(request.Context.User);
                    break;
                case "route":
                    await db.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Security.AuthorizationProvider)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.Value, "\"cerbos\""), token);
                    break;
                case "support_revoke":
                    await db.SupportAccessSessions.Where(row => row.Id == supportId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.StatusId, (int)SupportAccessSessionStatusEnum.Revoked), token);
                    break;
                case "support_extend_after_reads":
                    await db.SupportAccessSessions.Where(row => row.Id == supportId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.ExpiresAtUtc, supportExpiry.AddMinutes(5)), token);
                    break;
            }
            if (change is "credential_after_reads" or "support_extend_after_reads")
                services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async finalToken =>
                {
                    await Assert.That(await AdministrativeEffectAsync(db, owner, targetId, key, finalToken)).IsTrue();
                    DateTimeOffset expires = supportExpiry;
                    if (change == "credential_after_reads")
                    {
                        var lifetime = (await services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
                            .ObserveAsync(request.Context.User, finalToken))!;
                        expires = lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1);
                    }
                    fixture.Local.Clock.Advance(expires - fixture.Local.Clock.GetUtcNow());
                }, _ => { });
        });
        bool accepted = change is "unchanged_instance" or "unchanged_tenant" or "support_active";
        async Task InvokeAsync() => await InvokeAdministrativeOwnerAsync(
            services, physicalOwner, owner, targetId, parentId, stamp, key, deadline.Token);
        bool deniedBeforeBody = change == "support_missing_facts";
        if (deniedBeforeBody) await Assert.ThrowsAsync<AuthorizationException>(InvokeAsync);
        else if (accepted) await InvokeAsync();
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(InvokeAsync);
        await Assert.That(bodyObserved).IsEqualTo(!deniedBeforeBody);
        await Assert.That(bodies).IsEqualTo(deniedBeforeBody ? 0 : 1);
        await Assert.That(db.Database.CurrentTransaction).IsNull();
        await using var observer = fixture.Local.Native.CreateDatabase();
        await Assert.That(await AdministrativeEffectAsync(observer, owner, targetId, key, deadline.Token)).IsEqualTo(accepted);
        if (owner is 0 or 4 or 10)
            await Assert.That(await observer.AuditLogs.CountAsync(row => row.EntityId == targetId.ToString(), deadline.Token))
                .IsEqualTo(accepted ? 1 : 0);
        await Assert.That(await observer.UserExternalLogins.Where(row => row.Id == fixture.Local.Binding.ExternalLoginId)
            .Select(row => row.ProviderKey).SingleAsync(deadline.Token)).IsEqualTo(userId.ToString("D"));
        await Assert.That(await observer.Set<LocalIdentityUser>().Where(row => row.Id == userId)
            .Select(row => row.SecurityStamp).SingleAsync(deadline.Token)).IsEqualTo(originalIdentity.SecurityStamp);
        if (platform)
            await Assert.That(await observer.PlatformUserRoles.AnyAsync(row => row.Id == grantId, deadline.Token)).IsTrue();
        else
            await Assert.That(await observer.TenantUserRoleGrants.Where(row => row.Id == grantId)
                .Select(row => row.RevokedAt).SingleAsync(deadline.Token)).IsNull();
        if (!accepted && owner is 2 or 8)
            await Assert.That((await DefinitionState(observer, kind, targetId)).Stamp).IsEqualTo(stamp);
        if (!accepted && owner == 14)
            await Assert.That((await TemplateUpdateState(observer, true, targetId)).Stamp).IsEqualTo(stamp);
        if (supportId is { } id)
        {
            var support = await observer.SupportAccessSessions.AsNoTracking().SingleAsync(row => row.Id == id, deadline.Token);
            await Assert.That(support.StatusId).IsEqualTo((int)SupportAccessSessionStatusEnum.Active);
            await Assert.That(support.ExpiresAtUtc).IsEqualTo(supportExpiry);
        }
    }

    private static async Task<bool> AdministrativeEffectAsync(
        ExploreDbContext db, int owner, Guid targetId, string key, CancellationToken token) => owner switch
        {
            0 => !await db.CustomPropertyDefinitions.AsNoTracking().AnyAsync(row => row.Id == targetId, token),
            1 => await db.EventCustomPropertyDefinitions.AsNoTracking().AnyAsync(row => row.Key == key, token),
            2 => await db.EventCustomPropertyDefinitions.AsNoTracking().AnyAsync(row => row.Id == targetId && row.DisplayName == key, token),
            3 => await db.EventCustomPropertyDefinitions.IgnoreQueryFilters([QueryFilterNames.SoftDelete])
                .AsNoTracking().AnyAsync(row => row.Id == targetId && row.IsDeleted, token),
            4 => !await db.EventCustomPropertyDefinitions.AsNoTracking().AnyAsync(row => row.Id == targetId, token),
            5 or 6 => await db.EventCustomPropertyValues.AsNoTracking().AnyAsync(
                row => row.EventCustomPropertyDefinitionId == targetId && row.TextValue == key, token),
            7 => await db.EventSessionCustomPropertyDefinitions.AsNoTracking().AnyAsync(row => row.Key == key, token),
            8 => await db.EventSessionCustomPropertyDefinitions.AsNoTracking().AnyAsync(row => row.Id == targetId && row.DisplayName == key, token),
            9 => await db.EventSessionCustomPropertyDefinitions.IgnoreQueryFilters([QueryFilterNames.SoftDelete])
                .AsNoTracking().AnyAsync(row => row.Id == targetId && row.IsDeleted, token),
            10 => !await db.EventSessionCustomPropertyDefinitions.AsNoTracking().AnyAsync(row => row.Id == targetId, token),
            11 or 12 => await db.EventSessionCustomPropertyValues.AsNoTracking().AnyAsync(
                row => row.EventSessionCustomPropertyDefinitionId == targetId && row.TextValue == key, token),
            13 => await db.EventSessionTemplates.AsNoTracking().AnyAsync(row => row.SessionTemplateKey == key, token),
            14 => await db.EventSessionTemplates.AsNoTracking().AnyAsync(row => row.Id == targetId && row.DisplayName == key, token),
            15 => await db.EventSessionTemplates.IgnoreQueryFilters([QueryFilterNames.SoftDelete])
                .AsNoTracking().AnyAsync(row => row.Id == targetId && row.IsDeleted, token),
            _ => throw new ArgumentOutOfRangeException(nameof(owner))
        };

    private sealed class AdministrativeAfterBodyOwner(
        ISerializableAttemptOwner inner, Func<CancellationToken, Task> afterBody) : ISerializableAttemptOwner
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default) =>
            inner.ExecuteAsync(async token =>
            {
                var result = await operation(token);
                await afterBody(token);
                return result;
            }, cancellationToken);
    }
}
