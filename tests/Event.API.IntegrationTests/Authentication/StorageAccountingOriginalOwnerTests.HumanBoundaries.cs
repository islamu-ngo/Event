using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Controllers;
using Explore.Application.Constants;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class StorageAccountingOriginalOwnerTests
{
    [Test]
    [Arguments("wrong-actor")]
    [Arguments("empty-actor")]
    [Arguments("not-administrator")]
    [Arguments("human-as-setup")]
    [Arguments("external-local")]
    [Arguments("wrong-selected-tenant")]
    public async Task AccountingRejectsSubstitutedActorSourceAndUnsupportedOriginalOwner(string change)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            topology: change == "external-local" ? IdentityDatabaseTopology.External : IdentityDatabaseTopology.Colocated);
        await using (var seed = fixture.Native.CreateDatabase())
        {
            if (change != "not-administrator")
                await SeedAccountingAdministratorAsync(seed, fixture.Binding.LocalSubjectId);
            await SeedAccountingAsync(seed);
        }
        await using var host = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureTestServices(RegisterAccounting));
        await using var request = await AccountingRequest.CreateAsync(host.Services, await fixture.SignInAsync(), null);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(change == "wrong-selected-tenant" ? new(PlatformDefaults.DefaultTenantId) : PolicyMutationScope.Instance,
                CancellationToken.None);
        var service = selected.Services.GetRequiredService<IInstanceStorageSettingService>();
        Task execution = change == "human-as-setup"
            ? service.RecalculateUsageAsync(new RecalculateInstanceStorageUsageDuringSetupCommand())
            : service.RecalculateUsageAsync(new RecalculateInstanceStorageUsageCommand
            {
                UserId = change == "wrong-actor" ? Guid.CreateVersion7()
                    : change == "empty-actor" ? Guid.Empty : fixture.Binding.LocalSubjectId
            });
        if (change == "external-local")
            await Assert.ThrowsAsync<NotSupportedException>(() => execution);
        else if (change == "wrong-selected-tenant")
            await Assert.ThrowsAsync<InvalidOperationException>(() => execution);
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution);
        await using var observer = fixture.Native.CreateDatabase();
        await AssertAccountingAsync(observer, false);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AccountingSupportSelectionCannotExtendItsOriginalHorizon(bool expire)
    {
        var gate = new AccountingGate();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: gate);
        DateTimeOffset expiry = fixture.Clock.GetUtcNow().AddMinutes(5);
        Guid sessionId;
        await using (var seed = fixture.Native.CreateDatabase())
        {
            await SeedAccountingAdministratorAsync(seed, fixture.Binding.LocalSubjectId);
            foreach (var (key, value) in new Dictionary<string, string>
            {
                [GovernanceSettingKeys.SupportAccess.Enabled] = "true",
                [GovernanceSettingKeys.SupportAccess.AllowWriteMode] = "true"
            })
            {
                var setting = await seed.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == key);
                if (setting is null) seed.SystemSettings.Add(new() { SettingKey = key, Value = value });
                else setting.Value = value;
            }
            var session = SupportAccessSession.Start(fixture.Binding.LocalSubjectId, PlatformDefaults.DefaultTenantId,
                SupportAccessModeEnum.Write, "maintenance", "Accounting repair", "accounting-fixture",
                fixture.Clock.GetUtcNow().AddMinutes(-1), expiry);
            seed.SupportAccessSessions.Add(session);
            await SeedAccountingAsync(seed);
            sessionId = session.Id;
        }
        await using var host = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureTestServices(RegisterAccounting));
        await using var request = await AccountingRequest.CreateAsync(host.Services, await fixture.SignInAsync(), null);
        request.Context.Request.Headers[SupportAccessHeaderNames.SessionId] = sessionId.ToString("D");
        await Assert.That((await request.Services.GetRequiredService<ISupportAccessSessionService>().GetCurrentAsync())
            .IsActive).IsTrue();
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        var services = selected.Services;
        gate.Owner = services.GetRequiredService<ExploreDbContext>();
        gate.AfterRepair = () =>
        {
            if (gate.Repairs == 2)
                services.GetRequiredService<IHumanActionCommitFinalizer>().Register(_ =>
                {
                    if (expire) fixture.Clock.Advance(expiry - fixture.Clock.GetUtcNow());
                    return Task.CompletedTask;
                }, _ => { });
        };
        var execution = services.GetRequiredService<IInstanceStorageSettingService>().RecalculateUsageAsync(
            new RecalculateInstanceStorageUsageCommand { UserId = fixture.Binding.LocalSubjectId });
        if (expire) await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution);
        else await execution;
        await Assert.That(gate.Starts).IsEqualTo(1);
        await Assert.That(gate.Repairs).IsEqualTo(2);
        await using var observer = fixture.Native.CreateDatabase();
        await AssertAccountingAsync(observer, !expire);
    }

    [Test]
    public async Task ActualHumanControllerCarriesResolvedActorIntoAccounting()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using (var seed = fixture.Native.CreateDatabase())
        {
            await SeedAccountingAdministratorAsync(seed, fixture.Binding.LocalSubjectId);
            await SeedAccountingAsync(seed);
        }
        await using var host = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureTestServices(RegisterAccounting));
        await using var request = await AccountingRequest.CreateAsync(host.Services, await fixture.SignInAsync(), null);
        var controller = ActivatorUtilities.CreateInstance<InstanceStorageSettingsController>(request.Services);
        request.Context.Request.Headers["X-Setup-Secret"] = Guid.CreateVersion7().ToString("N");
        controller.ControllerContext = new ControllerContext { HttpContext = request.Context };
        var response = await controller.RecalculateStorageUsage();
        await Assert.That(response.Result).IsTypeOf<OkObjectResult>();
        var usage = (InstanceStorageUsageDto)((OkObjectResult)response.Result!).Value!;
        await Assert.That(usage.UsedBytes).IsEqualTo(41L);
        await using var observer = fixture.Native.CreateDatabase();
        await AssertAccountingAsync(observer, true);
    }

    [Test]
    public async Task ActualAcceptedSetupControllerRetainsSetupRatherThanInventingAnActor()
    {
        await using var native = await LocalAdmissionWebApplicationFactory.CreateAsync(
            AuthenticationProviderKind.Keycloak, incompleteSetup: true);
        await using (var seed = native.CreateDatabase()) await SeedAccountingAsync(seed);
        await using var host = native.WithWebHostBuilder(builder => builder.ConfigureTestServices(RegisterAccounting));
        await using var request = await CreateSetupAccountingRequestAsync(native, host);
        var controller = ActivatorUtilities.CreateInstance<InstanceStorageSettingsController>(request.Services);
        controller.ControllerContext = new ControllerContext { HttpContext = request.Context };
        var response = await controller.RecalculateStorageUsage();
        await Assert.That(response.Result).IsTypeOf<OkObjectResult>();
        var usage = (InstanceStorageUsageDto)((OkObjectResult)response.Result!).Value!;
        await Assert.That(usage.UsedBytes).IsEqualTo(41L);
        await using var observer = native.CreateDatabase();
        await AssertAccountingAsync(observer, true);
    }

    [Test]
    public async Task AccountingRejectsARealButDifferentPhysicalOwner()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using (var seed = fixture.Native.CreateDatabase())
        {
            await SeedAccountingAdministratorAsync(seed, fixture.Binding.LocalSubjectId);
            await SeedAccountingAsync(seed);
        }
        await using var host = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            RegisterAccounting(services);
            services.AddScoped(_ => new OtherAccountingDatabase(fixture.Native.CreateDatabase()));
            services.AddScoped<ISerializableAttemptOwner>(provider =>
                new EfCoreUnitOfWork(provider.GetRequiredService<OtherAccountingDatabase>().Context));
        }));
        await using var request = await AccountingRequest.CreateAsync(host.Services, await fixture.SignInAsync(), null);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            request.Services.GetRequiredService<IInstanceStorageSettingService>().RecalculateUsageAsync(
                new RecalculateInstanceStorageUsageCommand { UserId = fixture.Binding.LocalSubjectId }));
        await using var observer = fixture.Native.CreateDatabase();
        await AssertAccountingAsync(observer, false);
    }

    private sealed class OtherAccountingDatabase(ExploreDbContext context) : IAsyncDisposable
    {
        internal ExploreDbContext Context => context;
        public ValueTask DisposeAsync() => context.DisposeAsync();
    }
}
