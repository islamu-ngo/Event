using Explore.Application.Constants;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
public sealed class HumanImportSupportHorizonTests
{
    [Test]
    [Arguments("unchanged", false)]
    [Arguments("unchanged", true)]
    [Arguments("extend_before_acquisition", false)]
    [Arguments("extend_before_acquisition", true)]
    [Arguments("extend_before_commit", false)]
    [Arguments("extend_before_commit", true)]
    [Arguments("revoke_before_commit", false)]
    [Arguments("revoke_before_commit", true)]
    public async Task OriginalSupportHorizonControlsActualImportAdmission(string change, bool reprepare)
    {
        await using var fixture = await ResourceVisitorAuthenticatedFixture.CreateAsync();
        var context = fixture.Context;
        var member = await context.TenantUsers.SingleAsync(
            row => row.TenantId == fixture.TenantId && row.UserId == fixture.UserId);
        context.TenantUserRoleGrants.Add(new TenantUserRoleGrant
        {
            Id = Guid.CreateVersion7(),
            TenantId = fixture.TenantId,
            Tenant = null!,
            TenantUserId = member.Id,
            TenantUser = member,
            RoleId = (int)RoleEnum.TenantAdmin,
            Role = null!,
            RoleScopeId = (int)RoleScopeEnum.Tenant,
            GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
        });
        context.PlatformUserRoles.Add(new PlatformUserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = fixture.UserId,
            User = null!,
            RoleId = (int)RoleEnum.Admin,
            Role = null!,
            GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
        });
        foreach (string key in new[]
                 { GovernanceSettingKeys.SupportAccess.Enabled, GovernanceSettingKeys.SupportAccess.AllowWriteMode })
        {
            var setting = await context.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == key);
            if (setting is null)
                context.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = "true" });
            else
                setting.Value = "true";
        }
        var expires = fixture.Clock.GetUtcNow().AddMinutes(5);
        var session = SupportAccessSession.Start(fixture.UserId, fixture.TenantId, SupportAccessModeEnum.Write,
            "maintenance", "Import admission", "fixture-ticket", fixture.Clock.GetUtcNow().AddMinutes(-1), expires);
        context.SupportAccessSessions.Add(session);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        fixture.Services.GetRequiredService<IHttpContextAccessor>().HttpContext!.Request
            .Headers[SupportAccessHeaderNames.SessionId] = session.Id.ToString("D");
        var support = await fixture.Services.GetRequiredService<ISupportAccessSessionService>().GetCurrentAsync();
        await Assert.That(support.IsActive).IsTrue();
        await Assert.That(fixture.Lifetime.IsValidAt(expires)).IsTrue();

        var human = fixture.Services.GetRequiredService<IHumanCommitScopePreparation>();
        await human.PrepareAsync(CancellationToken.None);
        var admission = fixture.Services.GetRequiredService<IHumanConfigurationImportAdmission>();
        var prepared = await admission.PrepareAsync(human.Subject!, ConfigurationImportTarget.ForTenant(fixture.TenantId),
                null, CancellationToken.None);
        string originalName = await context.Tenants.Where(row => row.Id == fixture.TenantId)
            .Select(row => row.FullName).SingleAsync();
        if (change == "extend_before_acquisition")
        {
            await context.SupportAccessSessions.Where(row => row.Id == session.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.ExpiresAtUtc, expires.AddMinutes(10)));
            fixture.Clock.Advance(expires - fixture.Clock.GetUtcNow());
        }
        bool bodyReached = false;
        async Task<bool> ExecuteAsync()
        {
            var attempt = reprepare
                ? await admission.PrepareAsync(human.Subject!, prepared.Authority.Target,
                    prepared.Authority, CancellationToken.None)
                : prepared;
            return await ((ISerializableAttemptOwner)fixture.Services.GetRequiredService<IUnitOfWork>())
                .ExecuteAsync(async token =>
            {
                await attempt.AcquireAsync(token);
                bodyReached = true;
                await context.Tenants.Where(row => row.Id == fixture.TenantId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.FullName, "Admitted import"), token);
                if (change == "extend_before_commit")
                    await context.SupportAccessSessions.Where(row => row.Id == session.Id)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.ExpiresAtUtc, expires.AddMinutes(10)), token);
                if (change == "revoke_before_commit")
                    await context.SupportAccessSessions.Where(row => row.Id == session.Id)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.StatusId,
                            (int)SupportAccessSessionStatusEnum.Revoked), token);
                fixture.Services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async finalToken =>
                {
                    await context.Tenants.AsNoTracking().SingleAsync(row => row.Id == fixture.TenantId, finalToken);
                    if (change == "extend_before_commit")
                        fixture.Clock.Advance(expires - fixture.Clock.GetUtcNow());
                }, _ => { });
                return true;
            }, CancellationToken.None);
        }
        var execution = ExecuteAsync();
        if (change == "unchanged")
            await Assert.That(await execution).IsTrue();
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution);
        await Assert.That(bodyReached).IsEqualTo(change != "extend_before_acquisition");
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await using var observer = fixture.CreateIndependentContext();
        await Assert.That(await observer.Tenants.AsNoTracking().Where(row => row.Id == fixture.TenantId)
            .Select(row => row.FullName).SingleAsync())
            .IsEqualTo(change == "unchanged" ? "Admitted import" : originalName);
        var persisted = await observer.SupportAccessSessions.AsNoTracking().SingleAsync(row => row.Id == session.Id);
        await Assert.That(persisted.ExpiresAtUtc)
            .IsEqualTo(change == "extend_before_acquisition" ? expires.AddMinutes(10) : expires);
        await Assert.That(persisted.StatusId).IsEqualTo((int)SupportAccessSessionStatusEnum.Active);
    }
}
