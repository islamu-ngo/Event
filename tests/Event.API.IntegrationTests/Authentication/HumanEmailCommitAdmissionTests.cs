using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.EmailDispatch;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
public sealed class HumanEmailCommitAdmissionTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DisableConfirmationMustSurviveAllBlockingFinalAdmission(bool expire)
    {
        await using var fixture = await SmtpAuthenticatedFixture.CreateAsync();
        await fixture.ExecuteAsync(tenantOwner: false, async services =>
        {
            var snapshot = await ReadImpactAsync(services);
            await Assert.That(snapshot).IsNotNull();
            var token = services.GetRequiredService<IEmailDeliveryDisableTokenService>()
                .Issue(fixture.Subject.PrincipalId, snapshot!);
            var preparation = services.GetRequiredService<IHumanEmailMutationPreparation>();
            var confirmation = new EmailDeliveryDisableConfirmation(null, fixture.Subject.PrincipalId,
                snapshot!.Revision, token.Token, EmailDeliveryDisableConfirmation.RequiredAcknowledgement);
            var prepared = await preparation.PrepareDisableAsync(confirmation);
            var operation = preparation.ExecutePreparedAsync(prepared, async inner =>
                    {
                        var result = await services.GetRequiredService<IEmailDeliverySettingsWriter>()
                            .DisableAsync(confirmation, inner);
                        await Assert.That(result.Status).IsEqualTo(EmailDeliverySettingsWriteStatus.Applied);
                        services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async finalToken =>
                        {
                            await services.GetRequiredService<ExploreDbContext>().SystemSettings.AsNoTracking()
                                .SingleAsync(row => row.SettingKey == GovernanceSettingKeys.Email.DeliveryEnabled, finalToken);
                            if (expire)
                                fixture.Clock.Advance(token.ExpiresAtUtc - fixture.Clock.GetUtcNow());
                        }, _ => { });
                        return true;
                    }, CancellationToken.None);
            if (expire)
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => operation);
            else
                await Assert.That(await operation).IsTrue();
        });
        await fixture.ExecuteAsync(tenantOwner: false, async services =>
        {
            var snapshot = await ReadImpactAsync(services);
            await Assert.That(snapshot!.CanDisable).IsEqualTo(expire);
        });
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SelectedHumanOwnerCannotSwitchToInternalEmailSetup(bool disable)
    {
        await using var fixture = await SmtpAuthenticatedFixture.CreateAsync();
        await fixture.ExecuteAsync(tenantOwner: false, async services =>
        {
            var writer = new InternalEmailDeliverySettingsWriter(
                services.GetRequiredService<ExploreDbContext>(),
                services.GetRequiredService<ISettingMutationLock>(), services.GetRequiredService<IUnitOfWork>());
            await Assert.ThrowsAsync<InvalidOperationException>(() => disable
                ? writer.DisableInternalAsync(null, fixture.Subject.PrincipalId)
                : writer.ApplyInternalAsync([new(null, GovernanceSettingKeys.Email.SmtpPort,
                    EmailDeliverySettingMutationKind.SetValue, "2525")], fixture.Subject.PrincipalId));
        });
        await fixture.ExecuteAsync(tenantOwner: false, async services =>
        {
            var snapshot = await ReadImpactAsync(services);
            await Assert.That(snapshot!.CanDisable).IsTrue();
        });
    }

    [Test]
    [Arguments("unchanged")]
    [Arguments("own_revocation")]
    [Arguments("credential_expiry")]
    public async Task BorrowedHumanEmailWriteRequiresCurrentOriginalAuthorityAtCommit(string change)
    {
        await using var fixture = await ResourceVisitorAuthenticatedFixture.CreateAsync();
        var grant = new PlatformUserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = fixture.UserId,
            User = null!,
            RoleId = (int)RoleEnum.Admin,
            Role = null!,
            GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
        };
        fixture.Context.PlatformUserRoles.Add(grant);
        await fixture.Context.SaveChangesAsync();
        await using var selected = await fixture.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.EmailInstance, CancellationToken.None);
        var context = selected.Services.GetRequiredService<ExploreDbContext>();
        var subject = selected.Services.GetRequiredService<IHumanCommitScopePreparation>().Subject!;
        string key = GovernanceSettingKeys.Email.FromName;
        string value = $"\"human-email-{Guid.CreateVersion7():N}\"";
        var preparation = selected.Services.GetRequiredService<IHumanEmailMutationPreparation>();
        System.Collections.Immutable.ImmutableArray<EmailDeliverySettingMutation> effects =
            [new(null, key, EmailDeliverySettingMutationKind.SetValue, value)];
        var prepared = await preparation.PrepareApplyAsync(effects, subject.PrincipalId);
        var operation = preparation.ExecutePreparedAsync(prepared, async inner =>
                {
                    var result = await selected.Services.GetRequiredService<IEmailDeliverySettingsWriter>()
                        .ApplyAsync(effects, subject.PrincipalId, inner);
                    await Assert.That(result.Status).IsEqualTo(EmailDeliverySettingsWriteStatus.Applied);
                    if (change == "own_revocation")
                        await context.PlatformUserRoles.Where(row => row.Id == grant.Id).ExecuteDeleteAsync(inner);
                    selected.Services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async finalToken =>
                    {
                        await context.SystemSettings.AsNoTracking().SingleAsync(row => row.SettingKey == key, finalToken);
                        if (change == "credential_expiry")
                            fixture.Clock.Advance(fixture.Lifetime.ExpiresAt.Add(fixture.Lifetime.ClockSkew)
                                .AddTicks(1) - fixture.Clock.GetUtcNow());
                    }, _ => { });
                    return true;
                }, CancellationToken.None);
        if (change == "unchanged") await Assert.That(await operation).IsTrue();
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(() => operation);
        await using var observer = fixture.CreateIndependentContext();
        await Assert.That(await observer.SystemSettings.AsNoTracking()
            .AnyAsync(row => row.SettingKey == key && row.Value == value)).IsEqualTo(change == "unchanged");
        await Assert.That(await observer.PlatformUserRoles.AsNoTracking().AnyAsync(row => row.Id == grant.Id)).IsTrue();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    private static async Task<EmailDeliveryDisableImpactSnapshot?> ReadImpactAsync(IServiceProvider services)
    {
        var preparation = services.GetRequiredService<IHumanEmailMutationPreparation>();
        var prepared = await preparation.PreparePreviewAsync(null);
        return await preparation.ExecutePreparedAsync(prepared,
            token => services.GetRequiredService<IEmailDeliveryDisableImpactReader>().ReadAsync(null, token));
    }
}
