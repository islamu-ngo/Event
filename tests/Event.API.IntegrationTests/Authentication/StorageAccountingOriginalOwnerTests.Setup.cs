using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Constants;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Handlers.Commands;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class StorageAccountingOriginalOwnerTests
{
    [Test]
    [Arguments("unchanged")]
    [Arguments("pending-replaced")]
    [Arguments("completed-before-begin")]
    [Arguments("header-replaced")]
    [Arguments("principal-replaced")]
    [Arguments("secret-locked-at-settlement")]
    [Arguments("credential-expired-at-settlement")]
    [Arguments("completed-in-owner")]
    public async Task ActualSetupAccountingRetainsTicketSecretAndPendingReceipt(string change)
    {
        var gate = new AccountingGate();
        var clock = new LocalIdentityLifecycleHttpFixture.LifecycleClock();
        await using var native = await LocalAdmissionWebApplicationFactory.CreateAsync(
            AuthenticationProviderKind.Keycloak, incompleteSetup: true, persistenceInterceptor: gate);
        await using (var seed = native.CreateDatabase())
        {
            seed.InstanceBootstrapStates.Add(InstanceBootstrapState.CreateInteractivePending(
                Guid.CreateVersion7(), DeploymentMode.SingleTenant, DateTime.UtcNow));
            await SeedAccountingAsync(seed);
        }
        await using var host = native.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                RegisterAccounting(services);
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
            }));
        await using var request = await CreateSetupAccountingRequestAsync(native, host);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        var services = selected.Services;
        var context = services.GetRequiredService<ExploreDbContext>();
        gate.Owner = context;
        gate.BeforeStart = async token =>
        {
            if (change == "header-replaced")
                request.Context.Request.Headers["X-Setup-Secret"] = Guid.CreateVersion7().ToString("N");
            if (change == "principal-replaced")
                request.Context.User = new System.Security.Claims.ClaimsPrincipal(request.Context.User);
            if (change is "pending-replaced" or "completed-before-begin")
            {
                await using var independent = native.CreateDatabase();
                var pending = await independent.InstanceBootstrapStates.SingleAsync(token);
                if (change == "pending-replaced")
                {
                    independent.InstanceBootstrapStates.Remove(pending);
                    await independent.SaveChangesAsync(token);
                    independent.InstanceBootstrapStates.Add(InstanceBootstrapState.CreateInteractivePending(
                        Guid.CreateVersion7(), DeploymentMode.SingleTenant, DateTime.UtcNow));
                }
                else
                    pending.CompleteInteractive(await independent.Users.Select(row => row.Id).FirstAsync(token),
                        DateTime.UtcNow);
                await independent.SaveChangesAsync(token);
            }
        };
        gate.AfterRepair = () =>
        {
            if (change == "secret-locked-at-settlement")
                services.GetRequiredService<ISetupSecretProvider>().Lock();
            if (change == "credential-expired-at-settlement" && gate.Repairs == 2)
            {
                var lifetime = services.GetRequiredService<Explore.Persistence.Database.ExploreDbContextExecutionLease>()
                    .HumanLifetime!;
                services.GetRequiredService<IHumanActionCommitFinalizer>().Register(_ =>
                {
                    clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1) - clock.GetUtcNow());
                    return Task.CompletedTask;
                }, _ => { });
            }
        };
        gate.AfterRepairAsync = async token =>
        {
            if (change != "completed-in-owner" || gate.Repairs != 2) return;
            Guid actor = await context.Users.Select(row => row.Id).FirstAsync(token);
            await context.InstanceBootstrapStates.ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.Status, InstanceBootstrapStatus.Completed)
                .SetProperty(row => row.CompletedAt, DateTime.UtcNow)
                .SetProperty(row => row.CompletedByUserId, actor), token);
        };
        var execution = ActivatorUtilities.CreateInstance<RecalculateInstanceStorageUsageCommandHandler>(services)
            .ExecuteAsync(new RecalculateInstanceStorageUsageDuringSetupCommand(), CancellationToken.None);
        if (change == "unchanged")
            await Assert.That((await execution).UsedBytes).IsEqualTo(41L);
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution);
        await Assert.That(gate.Starts).IsEqualTo(1);
        await Assert.That(gate.Repairs).IsEqualTo(change is "unchanged" or "secret-locked-at-settlement"
            or "credential-expired-at-settlement" or "completed-in-owner" ? 2 : 0);
        await Assert.That(services.GetRequiredService<IHumanCommitScopePreparation>().Subject).IsNotNull();
        await Assert.That(context.HasHumanCommitContextLease).IsTrue();
        await using var observer = native.CreateDatabase();
        await AssertAccountingAsync(observer, change == "unchanged");
        if (change == "completed-in-owner")
            await Assert.That((await observer.InstanceBootstrapStates.SingleAsync()).Status)
                .IsEqualTo(InstanceBootstrapStatus.Pending);
    }

    [Test]
    [Arguments("invented-actor")]
    [Arguments("empty-actor")]
    [Arguments("original-actor")]
    public async Task AcceptedSetupCannotBecomeHumanAdministratorAccounting(string actor)
    {
        await using var native = await LocalAdmissionWebApplicationFactory.CreateAsync(
            AuthenticationProviderKind.Keycloak, incompleteSetup: true);
        await using (var seed = native.CreateDatabase()) await SeedAccountingAsync(seed);
        await using var host = native.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(RegisterAccounting));
        await using var request = await CreateSetupAccountingRequestAsync(native, host);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        Guid original = selected.Services.GetRequiredService<IHumanCommitScopePreparation>().Subject!.PrincipalId;
        var execution = selected.Services.GetRequiredService<IInstanceStorageSettingService>().RecalculateUsageAsync(
            new RecalculateInstanceStorageUsageCommand
            {
                UserId = actor == "original-actor" ? original : actor == "empty-actor" ? Guid.Empty : Guid.CreateVersion7()
            });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution);
        await using var observer = native.CreateDatabase();
        await AssertAccountingAsync(observer, false);
    }

    [Test]
    public async Task AClaimedSetupIdentityIsNotAnActualSetupTicket()
    {
        await using var native = await LocalAdmissionWebApplicationFactory.CreateAsync(
            AuthenticationProviderKind.Keycloak, incompleteSetup: true);
        await using (var seed = native.CreateDatabase()) await SeedAccountingAsync(seed);
        await using var host = native.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(RegisterAccounting));
        await using var request = await CreateSetupAccountingRequestAsync(native, host);
        // An invented setup identity cannot substitute the actual provider ticket.
        request.Context.User = new(new System.Security.Claims.ClaimsIdentity(
            Array.Empty<System.Security.Claims.Claim>(), ApiAuthenticationSchemeNames.SetupSecret));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            request.Services.GetRequiredService<IInstanceStorageSettingService>()
                .RecalculateUsageAsync(new RecalculateInstanceStorageUsageDuringSetupCommand()));
        await using var observer = native.CreateDatabase();
        await AssertAccountingAsync(observer, false);
    }

    [Test]
    public async Task SetupAccountingAdmissionCannotChangeActionReplayOrOutliveItsOwner()
    {
        await using var native = await LocalAdmissionWebApplicationFactory.CreateAsync(
            AuthenticationProviderKind.Keycloak, incompleteSetup: true);
        await using var host = native.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(RegisterAccounting));
        await using var request = await CreateSetupAccountingRequestAsync(native, host);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        var authority = selected.Services.GetRequiredService<IStorageAccountingOriginalAuthority>();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            authority.PrepareAsync(new UnrelatedAccountingResultCommand(), CancellationToken.None));
        var command = new RecalculateInstanceStorageUsageDuringSetupCommand();
        using var admission = await authority.PrepareAsync(command, CancellationToken.None);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => admission.ValidateSourcesAsync(CancellationToken.None));
        await selected.Services.GetRequiredService<ISerializableAttemptOwner>().ExecuteAsync(async token =>
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => admission.AcquireAsync(command with { }, token));
            await admission.AcquireAsync(command, token);
            await admission.ValidateSourcesAsync(token);
            admission.ValidateTime();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => admission.AcquireAsync(command, token));
            return true;
        });
        await selected.Services.GetRequiredService<ISerializableAttemptOwner>().ExecuteAsync(async token =>
        {
            // Even before disposal, a new transaction cannot inherit this source receipt.
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => admission.ValidateSourcesAsync(token));
            return true;
        });
        admission.Dispose();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => admission.ValidateSourcesAsync(CancellationToken.None));
    }

    private sealed record UnrelatedAccountingResultCommand : ICommand<InstanceStorageUsageDto>;

    [Test]
    public async Task AcceptedSecretOnAnotherEntrypointCannotIssueAccountingSetupAuthority()
    {
        await using var native = await LocalAdmissionWebApplicationFactory.CreateAsync(
            AuthenticationProviderKind.Keycloak, incompleteSetup: true);
        await using (var seed = native.CreateDatabase()) await SeedAccountingAsync(seed);
        await using var host = native.WithWebHostBuilder(builder => builder.ConfigureTestServices(RegisterAccounting));
        await using var request = await CreateSetupAccountingRequestAsync(native, host);
        request.Context.Request.Path = "/api/instance/settings/auth-provider";
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            request.Services.GetRequiredService<IInstanceStorageSettingService>()
                .RecalculateUsageAsync(new RecalculateInstanceStorageUsageDuringSetupCommand()));
        await using var observer = native.CreateDatabase();
        await AssertAccountingAsync(observer, false);
    }
}
