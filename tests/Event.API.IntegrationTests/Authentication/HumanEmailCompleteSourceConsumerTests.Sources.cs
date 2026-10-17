using System.Collections.Immutable;
using Cerbos.Api.V1.Effect;
using Cerbos.Sdk;
using Explore.Application.Authorization;
using Explore.Application.Constants;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.EmailDispatch;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Mail;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Services;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanEmailCompleteSourceConsumerTests
{
    [Test]
    [Arguments("active")]
    [Arguments("revoked")]
    [Arguments("extended")]
    public async Task TenantEmailCannotExtendItsOriginalSupportHorizon(string change)
    {
        await using var fixture = await CreateAsync();
        var expires = fixture.Clock.GetUtcNow().AddMinutes(5);
        await Assert.That(fixture.Lifetime.IsValidAt(expires)).IsTrue();
        Guid sessionId;
        await using (var seed = fixture.CreateIndependentContext())
        {
            foreach (string key in new[] { GovernanceSettingKeys.SupportAccess.Enabled, GovernanceSettingKeys.SupportAccess.AllowWriteMode })
            {
                var row = await seed.SystemSettings.SingleOrDefaultAsync(setting => setting.SettingKey == key);
                if (row is null) seed.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = "true" });
                else row.Value = "true";
            }
            var session = SupportAccessSession.Start(fixture.Subject.PrincipalId, fixture.TenantId,
                SupportAccessModeEnum.Write, "maintenance", "Email administration", "fixture-ticket",
                fixture.Clock.GetUtcNow().AddMinutes(-1), expires);
            seed.SupportAccessSessions.Add(session);
            await seed.SaveChangesAsync();
            sessionId = session.Id;
        }
        await fixture.ExecuteAsync(true, async services =>
        {
            services.GetRequiredService<IHttpContextAccessor>().HttpContext!.Request
                .Headers[SupportAccessHeaderNames.SessionId] = sessionId.ToString("D");
            await Assert.That((await services.GetRequiredService<ISupportAccessSessionService>().GetCurrentAsync()).IsActive).IsTrue();
            var admission = services.GetRequiredService<IHumanEmailMutationPreparation>();
            ImmutableArray<EmailDeliverySettingMutation> effects =
                [new(fixture.TenantId, GovernanceSettingKeys.Email.SmtpPort, EmailDeliverySettingMutationKind.SetValue, "2525")];
            var prepared = await admission.PrepareApplyAsync(effects, fixture.Subject.PrincipalId);
            var context = services.GetRequiredService<ExploreDbContext>();
            var operation = admission.ExecutePreparedAsync(prepared, async token =>
            {
                var result = await services.GetRequiredService<EmailDeliverySettingsWriter>()
                    .ApplyAsync(effects, fixture.Subject.PrincipalId, token);
                if (change == "revoked")
                    await context.SupportAccessSessions.Where(row => row.Id == sessionId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.StatusId,
                            (int)SupportAccessSessionStatusEnum.Revoked), token);
                if (change == "extended")
                {
                    await context.SupportAccessSessions.Where(row => row.Id == sessionId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.ExpiresAtUtc, expires.AddHours(1)), token);
                    services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async finalToken =>
                    {
                        await context.SupportAccessSessions.AsNoTracking().SingleAsync(row => row.Id == sessionId, finalToken);
                        fixture.Clock.Advance(expires - fixture.Clock.GetUtcNow());
                    }, _ => { });
                }
                return result;
            });
            if (change == "active")
                await Assert.That((await operation).Status).IsEqualTo(EmailDeliverySettingsWriteStatus.Applied);
            else
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => operation);
        });
        await using var observer = fixture.CreateIndependentContext();
        await Assert.That(await observer.TenantSettingOverrides.AnyAsync(row => row.TenantId == fixture.TenantId
            && row.SettingKey == GovernanceSettingKeys.Email.SmtpPort && row.Value == "2525")).IsEqualTo(change == "active");
        await Assert.That(await observer.SupportAccessSessions.Where(row => row.Id == sessionId)
            .Select(row => row.ExpiresAtUtc).SingleAsync()).IsEqualTo(expires);
    }

    [Test]
    [Arguments("allow")]
    [Arguments("deny")]
    [Arguments("route-before-acquisition")]
    [Arguments("route-before-commit")]
    [Arguments("principal-before-acquisition")]
    [Arguments("principal-before-commit")]
    public async Task InstanceTenantEmailRetainsExactOriginalSelectedPolicy(string change)
    {
        EmailPolicyTransport? transport = null;
        await using var fixture = await CreateAsync(configure: registrations =>
            registrations.AddScoped<IHumanEmailMutationPreparation>(services =>
            {
                transport = new(services.GetRequiredService<ExploreDbContext>(),
                    change == "deny" ? Effect.Deny : Effect.Allow);
                var options = Options.Create(new CerbosSettings { GrpcEndpoint = "http://email-platform-pdp.invalid:3593" });
                var cerbos = ActivatorUtilities.CreateInstance<CerbosAuthorizationService>(
                    services, transport.Client, transport.Factory, options);
                return ActivatorUtilities.CreateInstance<HumanEmailMutationPreparation>(
                    services, cerbos, Options.Create(new AuthorizationProviderDeploymentOptions()), options);
            }));
        await using (var setup = fixture.CreateIndependentContext())
            await SetProviderAsync(setup, "cerbos");
        await fixture.ExecuteAsync(true, async services =>
        {
            var admission = services.GetRequiredService<IHumanEmailMutationPreparation>();
            var command = new SetInstanceTenantSettingCommand(fixture.TenantId, GovernanceSettingKeys.Email.SmtpPort, "2525");
            var pending = admission.PrepareAsync(command, "2525");
            if (change == "deny")
                await Assert.ThrowsAsync<AuthorizationException>(() => pending);
            else
            {
                var prepared = await pending;
                if (change == "route-before-acquisition")
                {
                    await using var other = fixture.CreateIndependentContext();
                    await SetProviderAsync(other, "local");
                }
                if (change == "principal-before-acquisition")
                {
                    await using var other = fixture.CreateIndependentContext();
                    await other.TenantUserRoleGrants.Where(row => row.TenantId == fixture.TenantId
                        && row.TenantUser.UserId == fixture.Subject.PrincipalId).ExecuteDeleteAsync();
                }
                var context = services.GetRequiredService<ExploreDbContext>();
                var operation = admission.ExecutePreparedAsync(prepared, async token =>
                {
                    var result = await services.GetRequiredService<EmailDeliverySettingsWriter>().ApplyInstanceTenantAsync(
                        [new(command.TenantId, command.Key, EmailDeliverySettingMutationKind.SetValue, "2525")],
                        fixture.Subject.PrincipalId, token);
                    if (change == "route-before-commit")
                        await SetProviderAsync(context, "local", token);
                    if (change == "principal-before-commit")
                        await context.TenantUserRoleGrants.Where(row => row.TenantId == fixture.TenantId
                            && row.TenantUser.UserId == fixture.Subject.PrincipalId).ExecuteDeleteAsync(token);
                    return result;
                });
                if (change == "allow")
                    await Assert.That((await operation).Status).IsEqualTo(EmailDeliverySettingsWriteStatus.Applied);
                else
                    await Assert.ThrowsAsync<UnauthorizedAccessException>(() => operation);
            }
            var sent = transport!.Requests.Single();
            var resource = sent.Resources.Single();
            await Assert.That(resource.Resource.Kind).IsEqualTo(ResourceKinds.InstanceSetting);
            await Assert.That(resource.Resource.Id).IsEqualTo(SetInstanceTenantSettingCommand.SettingKey);
            await Assert.That(resource.Actions.Single()).IsEqualTo(AuthorizationActions.InstanceSettings.Update);
            await Assert.That(transport.Endpoint).IsEqualTo("http://email-platform-pdp.invalid:3593");
        });
        await using var observer = fixture.CreateIndependentContext();
        await Assert.That(await observer.TenantSettingOverrides.AnyAsync(row => row.TenantId == fixture.TenantId
            && row.SettingKey == GovernanceSettingKeys.Email.SmtpPort && row.Value == "2525")).IsEqualTo(change == "allow");
    }

    [Test]
    public async Task InstanceAdministratorIsNotATenantConfigurationShortcut()
    {
        await using var fixture = await CreateAsync(instanceAdministrator: true, tenantAdministrator: false);
        await fixture.ExecuteAsync(true, async services =>
        {
            // The same actor may preview/disable this tenant, but ordinary tenant configuration
            // must retain tenant-admin-only rather than borrow that disjunction.
            var preview = await PreviewAsync(services, fixture.TenantId);
            await Assert.That(preview.IsSuccess).IsTrue();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                services.GetRequiredService<EmailDeliverySettingsWriter>().ApplyAsync(
                    [new(fixture.TenantId, GovernanceSettingKeys.Email.SmtpPort, EmailDeliverySettingMutationKind.SetValue, "2525")],
                    fixture.Subject.PrincipalId));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                services.GetRequiredService<EmailDeliverySettingsWriter>().ApplyInstanceTenantAsync(
                    [new(fixture.TenantId, GovernanceSettingKeys.Email.SmtpPort, EmailDeliverySettingMutationKind.SetValue, "2525")],
                    fixture.Subject.PrincipalId));
        });
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PreviewTokenRemainsPrivateUntilFinalCredentialGate(bool expire)
    {
        SmtpAuthenticatedFixture fixture = null!;
        bool observed = false;
        await using var owned = fixture = await CreateAsync(configure: services =>
            services.Replace(ServiceDescriptor.Scoped<IEmailDeliveryDisableImpactReader>(provider =>
                new AfterImpactRead(new EmailDeliveryDisableImpactReader(
                    provider.GetRequiredService<ExploreDbContext>(), provider), () =>
                {
                    observed = true;
                    if (expire)
                        fixture.Clock.Advance(fixture.Lifetime.ExpiresAt.Add(fixture.Lifetime.ClockSkew)
                            .AddTicks(1) - fixture.Clock.GetUtcNow());
                }))));
        await fixture.ExecuteAsync(false, async services =>
        {
            if (expire) await Assert.ThrowsAsync<UnauthorizedAccessException>(() => PreviewAsync(services, null));
            else await Assert.That((await PreviewAsync(services, null)).Id!.ConfirmationToken).IsNotNull();
            await Assert.That(observed).IsTrue();
            await Assert.That(services.GetRequiredService<ExploreDbContext>().Database.CurrentTransaction).IsNull();
        });
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OriginalDisableConfirmationSurvivesOwnRevisionButNotExpiry(bool expire)
    {
        await using var fixture = await CreateAsync();
        await fixture.ExecuteAsync(false, async services =>
        {
            var preview = (await PreviewAsync(services, null)).Id!;
            var confirmation = new EmailDeliveryDisableConfirmation(null, fixture.Subject.PrincipalId,
                preview.ExpectedRevision, preview.ConfirmationToken, EmailDeliveryDisableConfirmation.RequiredAcknowledgement);
            var admission = services.GetRequiredService<IHumanEmailMutationPreparation>();
            var prepared = await admission.PrepareDisableAsync(confirmation);
            var context = services.GetRequiredService<ExploreDbContext>();
            var operation = admission.ExecutePreparedAsync(prepared, async token =>
            {
                var result = await services.GetRequiredService<EmailDeliverySettingsWriter>().DisableAsync(confirmation, token);
                services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async finalToken =>
                {
                    await context.SystemSettings.AsNoTracking().SingleAsync(
                        row => row.SettingKey == GovernanceSettingKeys.Email.DeliveryEnabled, finalToken);
                    if (expire) fixture.Clock.Advance(preview.ExpiresAtUtc!.Value - fixture.Clock.GetUtcNow());
                }, _ => { });
                return result;
            });
            if (expire) await Assert.ThrowsAsync<UnauthorizedAccessException>(() => operation);
            else await Assert.That((await operation).Status).IsEqualTo(EmailDeliverySettingsWriteStatus.Applied);
        });
        await using var observer = fixture.CreateIndependentContext();
        await Assert.That(await observer.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Email.DeliveryEnabled)
            .Select(row => row.Value).SingleAsync()).IsEqualTo(expire ? "true" : "false");
    }

    [Test]
    public async Task AcquiredPreviewCannotReadUsingAnotherPhysicalContext()
    {
        await using var fixture = await CreateAsync();
        await fixture.ExecuteAsync(false, async services =>
        {
            var admission = services.GetRequiredService<IHumanEmailMutationPreparation>();
            var prepared = await admission.PreparePreviewAsync(null);
            await using var other = fixture.CreateIndependentContext();
            var reader = new EmailDeliveryDisableImpactReader(other, services);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                admission.ExecutePreparedAsync(prepared, token => reader.ReadAsync(null, token)));
        });
    }

    private static async Task SetProviderAsync(ExploreDbContext context, string provider, CancellationToken token = default)
    {
        var row = await context.SystemSettings.SingleOrDefaultAsync(
            setting => setting.SettingKey == GovernanceSettingKeys.Security.AuthorizationProvider, token);
        if (row is null)
            context.SystemSettings.Add(new SystemSetting
            {
                SettingKey = GovernanceSettingKeys.Security.AuthorizationProvider,
                Value = $"\"{provider}\""
            });
        else row.Value = $"\"{provider}\"";
        await context.SaveChangesAsync(token);
    }

    private sealed class AfterImpactRead(IEmailDeliveryDisableImpactReader inner, Action after)
        : IEmailDeliveryDisableImpactReader
    {
        public async Task<EmailDeliveryDisableImpactSnapshot?> ReadAsync(Guid? tenantId, CancellationToken cancellationToken = default)
        {
            var snapshot = await inner.ReadAsync(tenantId, cancellationToken);
            after();
            return snapshot;
        }
    }

    /// <summary>The only substitute is the external PDP; native sources and credential custody remain real.</summary>
    private sealed class EmailPolicyTransport
    {
        internal ICerbosClient Client { get; } = Substitute.For<ICerbosClient>();
        internal ICerbosClientFactory Factory { get; } = Substitute.For<ICerbosClientFactory>();
        internal List<Cerbos.Api.V1.Request.CheckResourcesRequest> Requests { get; } = [];
        internal string? Endpoint { get; private set; }

        internal EmailPolicyTransport(ExploreDbContext owner, Effect effect)
        {
            Factory.GetOrCreate(Arg.Any<string>()).Returns(call =>
            {
                Endpoint = call.Arg<string>();
                return Client;
            });
            Client.CheckResourcesAsync(Arg.Any<Cerbos.Sdk.Builder.CheckResourcesRequest>(), Arg.Any<Metadata>())
                .Returns(call =>
                {
                    if (owner.Database.CurrentTransaction is not null)
                        throw new InvalidOperationException("Email PDP preparation must finish before acquiring the owner.");
                    var sent = call.ArgAt<Cerbos.Sdk.Builder.CheckResourcesRequest>(0).ToCheckResourcesRequest().Clone();
                    Requests.Add(sent);
                    var response = new Cerbos.Api.V1.Response.CheckResourcesResponse { RequestId = sent.RequestId };
                    var result = new Cerbos.Api.V1.Response.CheckResourcesResponse.Types.ResultEntry
                    {
                        Resource = new() { Kind = sent.Resources.Single().Resource.Kind, Id = sent.Resources.Single().Resource.Id }
                    };
                    result.Actions.Add(sent.Resources.Single().Actions.Single(), effect);
                    response.Results.Add(result);
                    return new Cerbos.Sdk.Response.CheckResourcesResponse(response);
                });
        }
    }
}
