using Cerbos.Api.V1.Effect;
using Cerbos.Sdk;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authorization;
using Explore.Application.Constants;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanSettingsMutationPreparationTests
{
    [Test]
    [Arguments("allow")]
    [Arguments("deny")]
    [Arguments("route-changed-before-acquisition")]
    [Arguments("route-changed-before-commit")]
    public async Task AnnotatedInstanceTenantSettingRetainsExactPolicyQuestionAndDecision(string change)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(configureServices: services =>
        {
            services.AddScoped<SettingsPolicyTransport>(selected =>
                new(selected.GetRequiredService<ExploreDbContext>(), change == "deny" ? Effect.Deny : Effect.Allow));
            services.AddScoped<ICerbosClient>(selected => selected.GetRequiredService<SettingsPolicyTransport>().Client);
            services.AddScoped<ICerbosClientFactory>(selected => selected.GetRequiredService<SettingsPolicyTransport>().Factory);
            services.Configure<CerbosSettings>(options => options.GrpcEndpoint = "http://settings-platform-pdp.invalid:3593");
            services.Configure<AuthorizationProviderDeploymentOptions>(options => options.Provider = null);
        });
        await SeedPlatformAdministratorAsync(fixture);
        await using (var configure = fixture.Native.CreateDatabase())
            await SetProviderAsync(configure, "cerbos");
        await using var request = await Request.CreateAsync(fixture);
        await using var selection = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(new(PlatformDefaults.DefaultTenantId), CancellationToken.None);
        var services = selection.Services;
        await services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(CancellationToken.None);
        var context = services.GetRequiredService<ExploreDbContext>();
        var transport = services.GetRequiredService<SettingsPolicyTransport>();
        var preparation = services.GetRequiredService<IHumanSettingsMutationPreparation>();
        var command = new SetInstanceTenantSettingCommand(PlatformDefaults.DefaultTenantId, "event_list.page_size", "12");
        var preparationTask = preparation.PrepareAsync(command, "12");
        if (change == "deny")
            await Assert.ThrowsAsync<AuthorizationException>(() => preparationTask);
        else
        {
            var proof = await preparationTask;
            if (change == "route-changed-before-acquisition")
            {
                await using var revoke = fixture.Native.CreateDatabase();
                await SetProviderAsync(revoke, "local");
            }
            var execution = preparation.ExecutePreparedAsync(proof, async (admitted, token) =>
            {
                await preparation.RequireOwnedAsync(admitted, new(HumanSettingsOperation.InstanceTenantSettings,
                    SettingScope.Tenant, command.TenantId, null, command.Key, HumanSettingsMutationKind.SetValue, "12"), token);
                await services.GetRequiredService<ITenantSettingRepository>().SetValueAsync(
                    command.TenantId, command.Key, "12", token, fixture.Binding.LocalSubjectId);
                if (change == "route-changed-before-commit")
                    await SetProviderAsync(context, "local");
                return true;
            });
            if (change == "allow") await Assert.That(await execution).IsTrue();
            else await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution);
            await using var observer = fixture.Native.CreateDatabase();
            await Assert.That(await observer.Set<TenantSetting>().AsNoTracking()
                .Where(row => row.TenantId == command.TenantId && row.SettingKey == command.Key)
                .Select(row => row.Value).SingleOrDefaultAsync()).IsEqualTo(change == "allow" ? "12" : null);
        }
        await using (var observer = fixture.Native.CreateDatabase())
        {
            if (change == "deny")
                await Assert.That(await observer.Set<TenantSetting>().AnyAsync(row =>
                    row.TenantId == command.TenantId && row.SettingKey == command.Key)).IsFalse();
            await Assert.That(await observer.SystemSettings.Where(row =>
                row.SettingKey == GovernanceSettingKeys.Security.AuthorizationProvider)
                .Select(row => row.Value).SingleAsync())
                .IsEqualTo(change == "route-changed-before-acquisition" ? "\"local\"" : "\"cerbos\"");
        }
        var sent = transport.Requests.Single();
        var resource = sent.Resources.Single();
        await Assert.That(resource.Resource.Kind).IsEqualTo(ResourceKinds.InstanceSetting);
        await Assert.That(resource.Resource.Id).IsEqualTo(SetInstanceTenantSettingCommand.SettingKey);
        await Assert.That(resource.Actions.Single()).IsEqualTo(AuthorizationActions.InstanceSettings.Update);
        await Assert.That(transport.Endpoint).IsEqualTo("http://settings-platform-pdp.invalid:3593");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task UnannotatedInstanceWriterRetainsCurrentAdministratorWithoutPdp(bool revokeInOwner)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(configureServices: services =>
        {
            services.AddScoped<SettingsPolicyTransport>(selected =>
                new(selected.GetRequiredService<ExploreDbContext>(), Effect.Deny));
            services.AddScoped<ICerbosClient>(selected => selected.GetRequiredService<SettingsPolicyTransport>().Client);
            services.AddScoped<ICerbosClientFactory>(selected => selected.GetRequiredService<SettingsPolicyTransport>().Factory);
            services.Configure<CerbosSettings>(options => options.GrpcEndpoint = "http://unused-settings-pdp.invalid:3593");
            services.Configure<AuthorizationProviderDeploymentOptions>(options => options.Provider = "cerbos");
        });
        Guid grant = await SeedPlatformAdministratorAsync(fixture);
        await using var request = await Request.CreateAsync(fixture);
        await using var selection = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        var services = selection.Services;
        await services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(CancellationToken.None);
        var context = services.GetRequiredService<ExploreDbContext>();
        var transport = services.GetRequiredService<SettingsPolicyTransport>();
        var preparation = services.GetRequiredService<IHumanSettingsMutationPreparation>();
        var command = new UpdateSettingCommand { Key = "event_list.page_size", Value = "12", Scope = SettingScope.Instance };
        var proof = await preparation.PrepareAsync(command, "12");
        var originalValue = await context.SystemSettings.AsNoTracking().Where(row => row.SettingKey == command.Key)
            .Select(row => row.Value).SingleOrDefaultAsync();
        var execution = preparation.ExecutePreparedAsync(proof, async (admitted, token) =>
        {
            await preparation.RequireOwnedAsync(admitted, new(HumanSettingsOperation.InstanceSettings,
                SettingScope.Instance, null, null, command.Key, HumanSettingsMutationKind.SetValue, "12"), token);
            await services.GetRequiredService<ISystemSettingRepository>().UpsertAsync(new SystemSetting
            {
                SettingKey = command.Key,
                Value = "12",
                ValueType = SettingValueType.Integer,
                CreatedBy = fixture.Binding.LocalSubjectId,
                UpdatedBy = fixture.Binding.LocalSubjectId
            }, token);
            if (revokeInOwner)
                await context.PlatformUserRoles.Where(row => row.Id == grant).ExecuteDeleteAsync(token);
            return true;
        });
        if (revokeInOwner) await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution);
        else await Assert.That(await execution).IsTrue();
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.SystemSettings.AsNoTracking().Where(row => row.SettingKey == command.Key)
            .Select(row => row.Value).SingleOrDefaultAsync()).IsEqualTo(revokeInOwner ? originalValue : "12");
        await Assert.That(await observer.PlatformUserRoles.AnyAsync(row => row.Id == grant)).IsTrue();
        await Assert.That(transport.Requests.Count).IsEqualTo(0);
    }

    [Test]
    public async Task InstanceAdministratorIsNotAnOrdinaryTenantAdministratorShortcut()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await SeedPlatformAdministratorAsync(fixture);
        await using var request = await Request.CreateAsync(fixture);
        await using var selection = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(new(PlatformDefaults.DefaultTenantId), CancellationToken.None);
        var preparation = CreatePreparation(selection.Services, fixture);
        var command = new UpdateSettingCommand { Key = "event_list.page_size", Value = "12", Scope = SettingScope.Tenant };
        var proof = await preparation.PrepareAsync(command, "12");
        bool entered = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            preparation.ExecutePreparedAsync(proof, (_, _) =>
            {
                entered = true;
                return Task.FromResult(true);
            }));
        await Assert.That(entered).IsFalse();
    }

    [Test]
    [Arguments("active")]
    [Arguments("revoked")]
    [Arguments("extended-past-original-horizon")]
    public async Task OriginalSupportSelectionAndHorizonBoundTheOwner(string change)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await SeedPlatformAdministratorAsync(fixture);
        await using var request = await Request.CreateAsync(fixture);
        var expiry = fixture.Clock.GetUtcNow().AddMinutes(5);
        Guid sessionId;
        await using (var seed = fixture.Native.CreateDatabase())
        {
            foreach (string settingKey in new[] { GovernanceSettingKeys.SupportAccess.Enabled, GovernanceSettingKeys.SupportAccess.AllowWriteMode })
            {
                var setting = await seed.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == settingKey);
                if (setting is null) seed.SystemSettings.Add(new SystemSetting { SettingKey = settingKey, Value = "true" });
                else setting.Value = "true";
            }
            var session = SupportAccessSession.Start(fixture.Binding.LocalSubjectId, PlatformDefaults.DefaultTenantId,
                SupportAccessModeEnum.Write, "maintenance", "Settings administration", "fixture-ticket",
                fixture.Clock.GetUtcNow().AddMinutes(-1), expiry);
            seed.SupportAccessSessions.Add(session);
            await seed.SaveChangesAsync();
            sessionId = session.Id;
        }
        request.Context.Request.Headers[SupportAccessHeaderNames.SessionId] = sessionId.ToString("D");
        var services = request.Services;
        await Assert.That((await services.GetRequiredService<ISupportAccessSessionService>().GetCurrentAsync()).IsActive).IsTrue();
        var preparation = services.GetRequiredService<IHumanSettingsMutationPreparation>();
        var command = new UpdateSettingCommand { Key = "event_list.page_size", Value = "12", Scope = SettingScope.User };
        var proof = await preparation.PrepareAsync(command, "12");
        var context = services.GetRequiredService<ExploreDbContext>();
        var subject = services.GetRequiredService<IHumanCommitScopePreparation>().Subject!;
        var execution = preparation.ExecutePreparedAsync(proof, async (_, token) =>
        {
            await services.GetRequiredService<IUserPreferenceRepository>().Create(new UserPreference
            {
                TenantId = context.TenantFilterTenantId!.Value,
                Tenant = null!,
                UserId = subject.PrincipalId,
                SettingKey = command.Key,
                Value = "12",
                CreatedBy = subject.PrincipalId
            });
            if (change == "revoked")
                await context.SupportAccessSessions.Where(row => row.Id == sessionId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.StatusId,
                        (int)SupportAccessSessionStatusEnum.Revoked), token);
            if (change == "extended-past-original-horizon")
            {
                await context.SupportAccessSessions.Where(row => row.Id == sessionId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.ExpiresAtUtc, expiry.AddHours(1)), token);
                services.GetRequiredService<IHumanActionCommitFinalizer>().Register(_ =>
                {
                    fixture.Clock.Advance(expiry - fixture.Clock.GetUtcNow());
                    return Task.CompletedTask;
                }, _ => { });
            }
            return true;
        });
        if (change == "active") await Assert.That(await execution).IsTrue();
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution);
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.UserPreferences.AsNoTracking()
            .Where(row => row.TenantId == context.TenantFilterTenantId && row.UserId == subject.PrincipalId
                && row.SettingKey == command.Key).Select(row => row.Value).SingleOrDefaultAsync())
            .IsEqualTo(change == "active" ? "12" : null);
        await Assert.That(await observer.SupportAccessSessions.Where(row => row.Id == sessionId)
            .Select(row => row.ExpiresAtUtc).SingleAsync()).IsEqualTo(expiry);
        await Assert.That(await observer.SupportAccessSessions.Where(row => row.Id == sessionId)
            .Select(row => row.StatusId).SingleAsync()).IsEqualTo((int)SupportAccessSessionStatusEnum.Active);
    }

    private static async Task<Guid> SeedPlatformAdministratorAsync(LocalIdentityLifecycleHttpFixture fixture)
    {
        await using var seed = fixture.Native.CreateDatabase();
        seed.BindTenantLease();
        int roleId = await seed.Roles.Where(role => role.MasterCode == "platform.admin"
            && role.RoleScopeId == (int)RoleScopeEnum.Platform).Select(role => role.Id).SingleAsync();
        var grant = new PlatformUserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = fixture.Binding.LocalSubjectId,
            User = null!,
            RoleId = roleId,
            Role = null!,
            GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
        };
        seed.PlatformUserRoles.Add(grant);
        await seed.SaveChangesAsync();
        return grant.Id;
    }

    private static async Task SetProviderAsync(ExploreDbContext context, string provider)
    {
        var row = await context.SystemSettings.SingleOrDefaultAsync(
            setting => setting.SettingKey == GovernanceSettingKeys.Security.AuthorizationProvider);
        if (row is null)
            context.SystemSettings.Add(new SystemSetting
            {
                SettingKey = GovernanceSettingKeys.Security.AuthorizationProvider,
                Value = $"\"{provider}\""
            });
        else row.Value = $"\"{provider}\"";
        await context.SaveChangesAsync();
    }

    /// <summary>Only the external PDP transport is controlled; authentication, sources and owners are real.</summary>
    private sealed class SettingsPolicyTransport
    {
        internal ICerbosClient Client { get; } = Substitute.For<ICerbosClient>();
        internal ICerbosClientFactory Factory { get; } = Substitute.For<ICerbosClientFactory>();
        internal List<Cerbos.Api.V1.Request.CheckResourcesRequest> Requests { get; } = [];
        internal string? Endpoint { get; private set; }

        internal SettingsPolicyTransport(ExploreDbContext owner, Effect effect)
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
                        throw new InvalidOperationException("Settings PDP preparation must precede transaction acquisition.");
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
