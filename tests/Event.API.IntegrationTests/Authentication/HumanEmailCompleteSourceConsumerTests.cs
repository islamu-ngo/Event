using System.Collections.Immutable;
using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EmailDispatch;
using Explore.Application.Features.EmailDispatch;
using Explore.Application.Features.EmailDispatch.Handlers.Commands;
using Explore.Application.Responses;
using Explore.Domain.Constants;
using Explore.Infrastructure.Mail;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
public sealed partial class HumanEmailCompleteSourceConsumerTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OriginalEmailOwnerCommitsExactConfigurationAndReleasesPreview(bool tenant)
    {
        await using var fixture = await CreateAsync();
        await fixture.ExecuteAsync(tenant, async services =>
        {
            Guid? target = tenant ? fixture.TenantId : null;
            var result = await services.GetRequiredService<EmailDeliverySettingsWriter>().ApplyAsync(
                [new(target, GovernanceSettingKeys.Email.SmtpPort, EmailDeliverySettingMutationKind.SetValue, "2525")],
                fixture.Subject.PrincipalId);
            await Assert.That(result.Status).IsEqualTo(EmailDeliverySettingsWriteStatus.Applied);
            var preview = await PreviewAsync(services, target);
            await Assert.That(preview.IsSuccess).IsTrue();
            await Assert.That(preview.Id!.ConfirmationToken).IsNotNull();
            await Assert.That(services.GetRequiredService<ExploreDbContext>().Database.CurrentTransaction).IsNull();
        });
        await using var observer = fixture.CreateIndependentContext();
        string stored = tenant
            ? await observer.TenantSettingOverrides.Where(row => row.TenantId == fixture.TenantId
                && row.SettingKey == GovernanceSettingKeys.Email.SmtpPort).Select(row => row.Value).SingleAsync()
            : await observer.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Email.SmtpPort)
                .Select(row => row.Value).SingleAsync();
        await Assert.That(stored).IsEqualTo("2525");
    }

    [Test]
    public async Task AcquiredSourcesWithoutPrivateEffectCannotReadImpactOrBorrowWriter()
    {
        await using var fixture = await CreateAsync();
        await fixture.ExecuteAsync(false, async services =>
        {
            var source = await services.GetRequiredService<IHumanEmailSourceFactory>().CreateAsync();
            var prepared = await source.PrepareAsync(false);
            await using var transaction = await services.GetRequiredService<ExploreDbContext>()
                .Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await source.AcquireAsync(prepared);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                services.GetRequiredService<IEmailDeliveryDisableImpactReader>().ReadAsync(null));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                services.GetRequiredService<EmailDeliverySettingsWriter>().ApplyAsync(
                    [new(null, GovernanceSettingKeys.Email.SmtpPort, EmailDeliverySettingMutationKind.SetValue, "2525")],
                    fixture.Subject.PrincipalId));
            await transaction.RollbackAsync();
        });
    }

    [Test]
    public async Task RevokedOriginalCredentialCannotEnterThePreparedCallback()
    {
        await using var fixture = await CreateAsync();
        await fixture.ExecuteAsync(false, async services =>
        {
            var admission = services.GetRequiredService<IHumanEmailMutationPreparation>();
            var prepared = await admission.PreparePreviewAsync(null);
            await using (var revoke = fixture.CreateIndependentContext())
                await revoke.LocalIdentityUsers.Where(row => row.Id == fixture.Subject.PrincipalId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.SecurityStamp, Guid.CreateVersion7().ToString("D")));
            bool entered = false;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                admission.ExecutePreparedAsync(prepared, _ =>
                {
                    entered = true;
                    return Task.FromResult(true);
                }));
            await Assert.That(entered).IsFalse();
        });
    }

    [Test]
    public async Task PrivatePreparationRejectsCallerImplementationAndAnotherIssuer()
    {
        await using var fixture = await CreateAsync();
        IPreparedHumanEmailMutation? foreign = null;
        await fixture.ExecuteAsync(false, async services =>
            foreign = await services.GetRequiredService<IHumanEmailMutationPreparation>().PreparePreviewAsync(null));
        await fixture.ExecuteAsync(false, async services =>
        {
            var admission = services.GetRequiredService<IHumanEmailMutationPreparation>();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                admission.ExecutePreparedAsync(new ForgedPreparation(), _ => Task.FromResult(true)));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                admission.ExecutePreparedAsync(foreign!, _ => Task.FromResult(true)));
        });
    }

    [Test]
    [Arguments("preview")]
    [Arguments("effect")]
    [Arguments("actor")]
    public async Task AcquiredProofCannotBeRepurposedForAnotherEmailEffect(string change)
    {
        await using var fixture = await CreateAsync();
        await fixture.ExecuteAsync(false, async services =>
        {
            var admission = services.GetRequiredService<IHumanEmailMutationPreparation>();
            ImmutableArray<EmailDeliverySettingMutation> effects =
                [new(null, GovernanceSettingKeys.Email.SmtpPort, EmailDeliverySettingMutationKind.SetValue, "2525")];
            var prepared = change == "preview"
                ? await admission.PreparePreviewAsync(null)
                : await admission.PrepareApplyAsync(effects, fixture.Subject.PrincipalId);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => admission.ExecutePreparedAsync(prepared, token =>
                services.GetRequiredService<EmailDeliverySettingsWriter>().ApplyAsync(
                    change == "effect" ? [effects[0] with { Value = "2526" }] : effects,
                    change == "actor" ? Guid.CreateVersion7() : fixture.Subject.PrincipalId, token)));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                admission.ExecutePreparedAsync(prepared, _ => Task.FromResult(true)));
        });
        await using var observer = fixture.CreateIndependentContext();
        await Assert.That(await observer.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Email.SmtpPort)
            .Select(row => row.Value).SingleAsync()).IsEqualTo("587");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NonadministratorSourceCustodyDoesNotGrantEmailAction(bool tenant)
    {
        await using var fixture = await CreateAsync(instanceAdministrator: false, tenantAdministrator: false);
        await fixture.ExecuteAsync(tenant, async services =>
        {
            Guid? target = tenant ? fixture.TenantId : null;
            var admission = services.GetRequiredService<IHumanEmailMutationPreparation>();
            var prepared = await admission.PreparePreviewAsync(target);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => admission.ExecutePreparedAsync(prepared,
                token => services.GetRequiredService<IEmailDeliveryDisableImpactReader>().ReadAsync(target, token)));
        });
    }

    [Test]
    [Arguments("unchanged")]
    [Arguments("stamp")]
    [Arguments("expiry")]
    public async Task TerminalSourceAndTimeGateRollsBackConfiguration(string change)
    {
        await using var fixture = await CreateAsync();
        await fixture.ExecuteAsync(false, async services =>
        {
            var admission = services.GetRequiredService<IHumanEmailMutationPreparation>();
            ImmutableArray<EmailDeliverySettingMutation> effects =
                [new(null, GovernanceSettingKeys.Email.SmtpPort, EmailDeliverySettingMutationKind.SetValue, "2525")];
            var prepared = await admission.PrepareApplyAsync(effects, fixture.Subject.PrincipalId);
            var context = services.GetRequiredService<ExploreDbContext>();
            var operation = admission.ExecutePreparedAsync(prepared, async token =>
            {
                var result = await services.GetRequiredService<EmailDeliverySettingsWriter>()
                    .ApplyAsync(effects, fixture.Subject.PrincipalId, token);
                if (change == "stamp")
                    await context.LocalIdentityUsers.Where(row => row.Id == fixture.Subject.PrincipalId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.SecurityStamp,
                            Guid.CreateVersion7().ToString("D")), token);
                // Registered after the consumer, so the final clock check must occur after
                // this real blocking source read, not in the consumer's earlier source callback.
                services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async finalToken =>
                {
                    await context.SystemSettings.AsNoTracking().SingleAsync(
                        row => row.SettingKey == GovernanceSettingKeys.Email.SmtpPort, finalToken);
                    if (change == "expiry")
                        fixture.Clock.Advance(fixture.Lifetime.ExpiresAt.Add(fixture.Lifetime.ClockSkew)
                            .AddTicks(1) - fixture.Clock.GetUtcNow());
                }, _ => { });
                return result;
            });
            if (change == "unchanged")
                await Assert.That((await operation).Status).IsEqualTo(EmailDeliverySettingsWriteStatus.Applied);
            else
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => operation);
            await Assert.That(context.Database.CurrentTransaction).IsNull();
        });
        await using var observer = fixture.CreateIndependentContext();
        await Assert.That(await observer.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Email.SmtpPort)
            .Select(row => row.Value).SingleAsync()).IsEqualTo(change == "unchanged" ? "2525" : "587");
    }

    private static Task<SmtpAuthenticatedFixture> CreateAsync(
        bool instanceAdministrator = true, bool tenantAdministrator = true,
        Action<IServiceCollection>? configure = null) =>
        SmtpAuthenticatedFixture.CreateAsync(instanceAdministrator, tenantAdministrator, services =>
        {
            services.AddHumanEmailSources(IdentityDatabaseTopology.Colocated);
            services.AddHumanEmailMutationPreparation();
            configure?.Invoke(services);
        });

    private static Task<BaseCommandResponse<EmailDeliveryDisablePreviewDto>> PreviewAsync(
        IServiceProvider services, Guid? target) =>
        ActivatorUtilities.CreateInstance<PreviewEmailDeliveryDisableCommandHandler>(services)
            .ExecuteAsync(new(target), CancellationToken.None);

    private sealed class ForgedPreparation : IPreparedHumanEmailMutation;
}
