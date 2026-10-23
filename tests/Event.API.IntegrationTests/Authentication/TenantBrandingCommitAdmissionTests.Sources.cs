using Cerbos.Api.V1.Effect;
using Cerbos.Sdk;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Authorization;
using Explore.Application.Constants;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Documents;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Explore.Persistence.Repositories;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class TenantBrandingCommitAdmissionTests
{
    [Test]
    [Arguments("active")]
    [Arguments("revoke")]
    [Arguments("expire_after_reads")]
    public async Task OriginalForwardedSupportSurvivesUntilAcknowledgedBrandingCommit(string change)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        var seeded = await SeedAsync(fixture);
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var expiry = fixture.Clock.GetUtcNow().AddMinutes(5);
        Guid sessionId;
        await using (var seed = fixture.Native.CreateDatabase())
        {
            foreach (string key in new[] { GovernanceSettingKeys.SupportAccess.Enabled, GovernanceSettingKeys.SupportAccess.AllowWriteMode })
            {
                var setting = await seed.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == key);
                if (setting is null) seed.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = "true" });
                else setting.Value = "true";
            }
            seed.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(),
                UserId = fixture.Binding.LocalSubjectId,
                User = null!,
                RoleId = (int)RoleEnum.Admin,
                Role = null!,
                GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
            });
            var session = SupportAccessSession.Start(fixture.Binding.LocalSubjectId, PlatformDefaults.DefaultTenantId,
                SupportAccessModeEnum.Write, "maintenance", "Branding administration", "fixture-ticket",
                fixture.Clock.GetUtcNow().AddMinutes(-1), expiry);
            seed.SupportAccessSessions.Add(session);
            await seed.SaveChangesAsync();
            sessionId = session.Id;
        }
        request.Context.Request.Headers[SupportAccessHeaderNames.SessionId] = sessionId.ToString("D");
        var services = request.Services;
        await Assert.That((await services.GetRequiredService<ISupportAccessSessionService>().GetCurrentAsync()).IsActive).IsTrue();
        await services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(CancellationToken.None);
        var context = services.GetRequiredService<ExploreDbContext>();
        var lifetime = (await services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
            .ObserveAsync(request.Context.User, CancellationToken.None))!;
        await Assert.That(lifetime.IsValidAt(expiry)).IsTrue();
        bool observed = false;
        var owner = new AfterBody((ISerializableAttemptOwner)new EfCoreUnitOfWork(context), async token =>
        {
            observed = true;
            if (change == "revoke")
                await context.SupportAccessSessions.Where(row => row.Id == sessionId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.StatusId,
                        (int)SupportAccessSessionStatusEnum.Revoked), token);
            if (change == "expire_after_reads")
                services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async finalToken =>
                {
                    await context.TenantSettingsDocuments.AsNoTracking().SingleAsync(row =>
                        row.TenantId == PlatformDefaults.DefaultTenantId && row.DocumentKey == SettingsDocumentKeys.Tenant.Branding,
                        finalToken);
                    fixture.Clock.Advance(expiry - fixture.Clock.GetUtcNow());
                }, _ => { });
        });
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var execution = Handler(services, owner, new TypedSettingsDocumentResolver(
            new TenantSettingsDocumentRepository(context), cache), Preparation(services, fixture.Clock))
            .ExecuteAsync(Command(seeded.Stamp), CancellationToken.None);
        if (change == "active") await Assert.That((await execution).IsSuccess).IsTrue();
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution);
        await Assert.That(observed).IsTrue();
        await using var observer = fixture.Native.CreateDatabase();
        var document = await observer.TenantSettingsDocuments.AsNoTracking().SingleAsync(row =>
            row.TenantId == PlatformDefaults.DefaultTenantId && row.DocumentKey == SettingsDocumentKeys.Tenant.Branding);
        await Assert.That(document.ConcurrencyStamp == seeded.Stamp).IsEqualTo(change != "active");
        await Assert.That(await observer.SupportAccessSessions.Where(row => row.Id == sessionId)
            .Select(row => row.StatusId).SingleAsync()).IsEqualTo((int)SupportAccessSessionStatusEnum.Active);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExactSettingDecisionUsesPlatformRouteBeforeLocksAndHonorsPdpDenial(bool deny)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        var seeded = await SeedAsync(fixture);
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        await using (var seed = fixture.Native.CreateDatabase())
        {
            foreach (var (key, value) in new[]
            {
                (GovernanceSettingKeys.Cerbos.TenantCustomizationEnabled, "true"),
                (GovernanceSettingKeys.Cerbos.Mode, "\"custom_endpoint\""),
                (GovernanceSettingKeys.Cerbos.CustomEndpoint, "\"http://tenant-pdp.invalid:3593\"")
            })
            {
                var setting = await seed.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == key);
                if (setting is null) seed.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = value });
                else { setting.Value = value; setting.IsLocked = false; }
            }
            await seed.SaveChangesAsync();
        }
        var services = request.Services;
        await services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(CancellationToken.None);
        var context = services.GetRequiredService<ExploreDbContext>();
        var transport = new Transport(context, deny ? Effect.Deny : Effect.Allow);
        var options = Options.Create(new CerbosSettings { GrpcEndpoint = "http://platform-pdp.invalid:3593" });
        var preparation = new HumanTenantBrandingMutationPreparation(
            services.GetRequiredService<IHumanCommitScopePreparation>(), services.GetRequiredService<IHttpContextAccessor>(),
            services.GetRequiredService<IHumanAuthenticationLifetimeObserver>(), services.GetRequiredService<ITenantContext>(),
            fixture.Clock, ActivatorUtilities.CreateInstance<CerbosAuthorizationService>(
                services, transport.Client, transport.Factory, options),
            Options.Create(new AuthorizationProviderDeploymentOptions { Provider = "cerbos" }), options,
            () => services.GetRequiredService<IHumanPrincipalSourceFence>(),
            () => services.GetRequiredService<IHumanAuthorizationRouteSource>(),
            () => services.GetRequiredService<IHumanSupportAccessSourceFactory>(),
            () => services.GetRequiredService<IPolicySourceSetFence>(),
            () => services.GetRequiredService<IHumanActionCommitFinalizer>());
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var response = await Handler(services, (ISerializableAttemptOwner)new EfCoreUnitOfWork(context),
            new TypedSettingsDocumentResolver(new TenantSettingsDocumentRepository(context), cache), preparation)
            .ExecuteAsync(Command(seeded.Stamp), CancellationToken.None);
        await Assert.That(response.IsSuccess).IsEqualTo(!deny);
        var sent = transport.Requests.Single();
        var resource = sent.Resources.Single();
        await Assert.That(resource.Resource.Kind).IsEqualTo(ResourceKinds.TenantSetting);
        await Assert.That(resource.Resource.Id).IsEqualTo(PlatformDefaults.DefaultTenantId.ToString("D"));
        await Assert.That(resource.Actions.Single()).IsEqualTo(AuthorizationActions.Update);
        await Assert.That(resource.Resource.Attr["tenantId"].StringValue).IsEqualTo(PlatformDefaults.DefaultTenantId.ToString("D"));
        await Assert.That(resource.Resource.Attr["documentKey"].StringValue).IsEqualTo(SettingsDocumentKeys.Tenant.Branding);
        await Assert.That(transport.Endpoint).IsEqualTo("http://platform-pdp.invalid:3593");
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.TenantSettingsDocuments.Where(row =>
                row.TenantId == PlatformDefaults.DefaultTenantId && row.DocumentKey == SettingsDocumentKeys.Tenant.Branding)
            .Select(row => row.ConcurrencyStamp == seeded.Stamp).SingleAsync()).IsEqualTo(deny);
    }

    [Test]
    public async Task EqualCommandCannotBorrowCapabilityAndConsumedCapabilityCannotReplay()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        var seeded = await SeedAsync(fixture);
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var preparation = Preparation(request.Services, fixture.Clock);
        var command = Command(seeded.Stamp);
        var prepared = await preparation.PrepareAsync(command);
        var owner = request.Services.GetRequiredService<ISerializableAttemptOwner>();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => owner.ExecuteAsync(async token =>
        {
            await prepared.AcquireAsync(command with { }, token);
            return true;
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => owner.ExecuteAsync(async token =>
        {
            await prepared.AcquireAsync(command, token);
            return true;
        }));
        var fresh = await preparation.PrepareAsync(command);
        await owner.ExecuteAsync(async token =>
        {
            await fresh.AcquireAsync(command, token);
            return true;
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => owner.ExecuteAsync(async token =>
        {
            await fresh.AcquireAsync(command, token);
            return true;
        }));
    }

    [Test]
    public async Task IndependentLocalIdentityDoesNotInventSameOwnerSourceAuthority()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(topology: IdentityDatabaseTopology.External);
        var seeded = await SeedAsync(fixture);
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            Preparation(request.Services, fixture.Clock).PrepareAsync(Command(seeded.Stamp)));
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.TenantSettingsDocuments.Where(row =>
                row.TenantId == PlatformDefaults.DefaultTenantId && row.DocumentKey == SettingsDocumentKeys.Tenant.Branding)
            .Select(row => row.ConcurrencyStamp).SingleAsync()).IsEqualTo(seeded.Stamp);
    }

    /// <summary>Only the external PDP transport is controlled; all source leases and owners are real.</summary>
    private sealed class Transport
    {
        internal ICerbosClient Client { get; } = Substitute.For<ICerbosClient>();
        internal ICerbosClientFactory Factory { get; } = Substitute.For<ICerbosClientFactory>();
        internal List<Cerbos.Api.V1.Request.CheckResourcesRequest> Requests { get; } = [];
        internal string? Endpoint { get; private set; }

        internal Transport(ExploreDbContext owner, Effect effect)
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
                        throw new InvalidOperationException("Branding PDP evaluation must precede the owner transaction.");
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
