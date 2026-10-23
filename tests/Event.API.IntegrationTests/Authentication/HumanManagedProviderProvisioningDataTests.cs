using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.ManagedProviderProvisioning;
using Explore.Application.DTOs.Management;
using Explore.Application.Exceptions;
using Explore.Application.Features.ManagedProviderProvisioning.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

/// <summary>
/// DATA protocol controls on an authentic original Global owner. These do not
/// replace handler/HTTP authorization acceptance. All execution is deferred.
/// </summary>
[NotInParallel]
public sealed class HumanManagedProviderProvisioningDataTests
{
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task GlobalLocalLinkCreatesWholeGraphAndReplaysWithoutCredentialMutation(int organizer)
    {
        await using var fixture = await Fixture();
        var identityBefore = await fixture.ReadIdentityAsync();
        var command = Command(fixture, organizer);
        HumanManagedProviderProvisioningRows created;
        await using (var request = new Request(fixture.Host.Services))
        {
            await request.InitializeAsync(fixture);
            created = await Apply(request.Services, command);
        }
        await Assert.That(created.AlreadyProvisioned).IsFalse();
        await Assert.That(created.UserId).IsEqualTo(fixture.Binding.LocalSubjectId);
        await Assert.That(created.UserActorId).IsEqualTo(fixture.Binding.PersonalActorId);
        await Assert.That(created.UserExternalLoginId).IsEqualTo(fixture.Binding.ExternalLoginId);
        await using (var reader = fixture.Native.CreateDatabase())
        {
            await Assert.That(await reader.TenantSettingsDocuments.CountAsync(row => row.TenantId == created.TenantId)).IsEqualTo(2);
            await Assert.That(await reader.TenantUsers.AnyAsync(row => row.Id == created.TenantUserId
                && row.TenantId == created.TenantId && row.UserId == created.UserId)).IsTrue();
            await Assert.That(await reader.TenantUserProfiles.AnyAsync(row => row.Id == created.TenantUserProfileId
                && row.TenantUserId == created.TenantUserId)).IsTrue();
            await Assert.That(await reader.TenantUserRoleGrants.AnyAsync(row => row.Id == created.TenantUserRoleGrantId
                && row.TenantUserId == created.TenantUserId && row.RoleId == (int)RoleEnum.TenantAdmin)).IsTrue();
            var revision = await reader.Set<EventDiscoveryRevision>().SingleAsync(row => row.TenantId == created.TenantId);
            await Assert.That(revision.DisclosureEpoch).IsEqualTo(1L);
            await Assert.That(revision.IdentityEpoch).IsEqualTo(0L);
            await Assert.That(await reader.ExternalBindings.CountAsync(row => row.ScopeTenantId == created.TenantId)).IsEqualTo(organizer == 0 ? 5 : 7);
            await Assert.That(await reader.ManagedTenantProvisioningOperations.AnyAsync(row => row.TenantId == created.TenantId)).IsFalse();
        }
        HumanManagedProviderProvisioningRows replay;
        await using (var request = new Request(fixture.Host.Services))
        {
            await request.InitializeAsync(fixture);
            replay = await Apply(request.Services, command);
        }
        await Assert.That(replay.AlreadyProvisioned).IsTrue();
        await Assert.That(replay.TenantId).IsEqualTo(created.TenantId);
        await Assert.That(replay.TenantUserRoleGrantId).IsEqualTo(created.TenantUserRoleGrantId);
        await Assert.That(await fixture.ReadIdentityAsync()).IsEqualTo(identityBefore);
        await Assert.That(fixture.Smtp.Handoffs.Count).IsEqualTo(0);
    }

    [Test]
    [Arguments("document")]
    [Arguments("binding")]
    [Arguments("membership")]
    [Arguments("outside_graph")]
    [Arguments("missing_terminal")]
    [Arguments("after_terminal")]
    public async Task BorrowedTransactionCannotChangeOrOmitTheCompiledSuccessImage(string change)
    {
        await using var fixture = await Fixture();
        await using var request = new Request(fixture.Host.Services);
        await request.InitializeAsync(fixture);
        var command = Command(fixture, 0);
        var observation = await request.Services.GetRequiredService<IHumanManagedProviderProvisioningDataSource>().ObserveAsync(command);
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        Guid? tenant = null;
        var failure = await Assert.ThrowsAsync<Exception>(() => request.Services.GetRequiredService<ISerializableAttemptOwner>().ExecuteAsync(async token =>
        {
            var sourceSet = await request.Services.GetRequiredService<IPolicySourceSetFence>().AcquireAsync(token);
            var data = await observation.AcquireAsync(command, sourceSet, token);
            var rows = await data.ApplyAsync(token);
            tenant = rows.TenantId;
            if (change == "document")
                await context.TenantSettingsDocuments.Where(row => row.TenantId == rows.TenantId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.PayloadJson, "{}"), token);
            if (change == "binding")
                await context.ExternalBindings.Where(row => row.ScopeTenantId == rows.TenantId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.InternalId, Guid.CreateVersion7()), token);
            if (change == "membership")
                await context.TenantUsers.Where(row => row.Id == rows.TenantUserId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.StatusId, (int)TenantUserStatusEnum.Active + 1), token);
            if (change == "outside_graph")
                await context.Tenants.Where(row => row.Id == PlatformDefaults.DefaultTenantId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.Description, "Unrelated tenant alteration"), token);
            if (change != "missing_terminal")
                request.Services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async finalToken =>
                {
                    await data.ValidateAsync(finalToken);
                    if (change == "after_terminal")
                        await context.Tenants.Where(row => row.Id == rows.TenantId)
                            .ExecuteUpdateAsync(update => update.SetProperty(row => row.Description, "Late alteration"), finalToken);
                }, _ => { });
            return rows;
        }));
        await Assert.That(change is "missing_terminal" or "after_terminal"
            ? failure is InvalidOperationException
            : failure is ConcurrencyConflictException).IsTrue();
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(tenant.HasValue).IsTrue();
        await Assert.That(await observer.Tenants.AnyAsync(row => row.Id == tenant)).IsFalse();
        await Assert.That(await observer.ExternalBindings.AnyAsync(row => row.ScopeTenantId == tenant)).IsFalse();
    }

    [Test]
    public async Task EqualCommandCannotSubstituteForTheOriginalObservation()
    {
        await using var fixture = await Fixture();
        await using var request = new Request(fixture.Host.Services);
        await request.InitializeAsync(fixture);
        var command = Command(fixture, 0);
        var observation = await request.Services.GetRequiredService<IHumanManagedProviderProvisioningDataSource>().ObserveAsync(command);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            request.Services.GetRequiredService<ISerializableAttemptOwner>().ExecuteAsync(async token =>
            {
                var set = await request.Services.GetRequiredService<IPolicySourceSetFence>().AcquireAsync(token);
                return await observation.AcquireAsync(command with { }, set, token);
            }));
    }

    [Test]
    public async Task SlugRefusalHasNoBusinessWritesAndRequiresWholeOwnerRollback()
    {
        await using var fixture = await Fixture();
        string slug;
        await using (var existing = fixture.Native.CreateDatabase())
            slug = await existing.Tenants.Where(row => row.Id == PlatformDefaults.DefaultTenantId).Select(row => row.Slug).SingleAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.InitializeAsync(fixture);
        var command = Command(fixture, 0);
        command = command with { ProvisioningDto = command.ProvisioningDto with { TenantSlug = slug } };
        var observation = await request.Services.GetRequiredService<IHumanManagedProviderProvisioningDataSource>().ObserveAsync(command);
        var owner = request.Services.GetRequiredService<IOriginalOwnerSettlementBoundary>();
        var settlement = await owner.ExecuteAsync(command, async token =>
        {
            var set = await request.Services.GetRequiredService<IPolicySourceSetFence>().AcquireAsync(token);
            var data = await observation.AcquireAsync(command, set, token);
            await Assert.That(data.Refusal).IsNotNull();
            await Assert.That(data.Refusal!.FailureCode).IsNull();
            await Assert.ThrowsAsync<InvalidOperationException>(() => data.ApplyAsync(token));
            return OriginalOwnerDecision<HumanManagedProviderProvisioningRefusal>.RollBack(data.Refusal);
        });
        await Assert.That(owner.Consume(command, settlement, OriginalOwnerDisposition.WholeTransactionRolledBack)).IsNotNull();
        await using var reader = fixture.Native.CreateDatabase();
        await Assert.That(await reader.ExternalBindings.AnyAsync(row => row.ExternalId == command.ProvisioningDto.ExternalCustomerId)).IsFalse();
    }

    [Test]
    public async Task OrdinaryRuntimeCannotAcquireTheGlobalDataPort()
    {
        await using var fixture = await Fixture();
        await using var request = new Request(fixture.Host.Services);
        await request.InitializeAsync(fixture);
        await request.Root.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(default);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Task.FromResult(
            request.Root.GetRequiredService<IHumanManagedProviderProvisioningDataSource>()));
    }

    private static async Task<HumanManagedProviderProvisioningRows> Apply(IServiceProvider services,
        EnsureManagedProviderClientProvisionedCommand command)
    {
        var observation = await services.GetRequiredService<IHumanManagedProviderProvisioningDataSource>().ObserveAsync(command);
        return await services.GetRequiredService<ISerializableAttemptOwner>().ExecuteAsync(async token =>
        {
            var set = await services.GetRequiredService<IPolicySourceSetFence>().AcquireAsync(token);
            var data = await observation.AcquireAsync(command, set, token);
            await Assert.That(data.Refusal).IsNull();
            var capacity = await data.ReadActivationCapacityAsync(token);
            await Assert.That(capacity.ActiveTenants >= 0 && capacity.ReservedTenants >= 0).IsTrue();
            var rows = await data.ApplyAsync(token);
            services.GetRequiredService<IHumanActionCommitFinalizer>().Register(data.ValidateAsync, _ => { });
            return rows;
        });
    }

    private static Task<LocalIdentityLifecycleHttpFixture> Fixture() =>
        LocalIdentityLifecycleHttpFixture.CreateAsync(configureServices: services => services.AddHumanManagedProviderProvisioningData());

    private static EnsureManagedProviderClientProvisionedCommand Command(LocalIdentityLifecycleHttpFixture fixture, int organizer) => new()
    {
        ProvisioningDto = new()
        {
            ProviderKey = "data-protocol",
            ExternalSystem = "source-control",
            ExternalCustomerId = Guid.CreateVersion7().ToString("D"),
            TenantFullName = "Provisioning DATA",
            TenantSlug = "data-" + Guid.CreateVersion7().ToString("N"),
            ActivateTenant = false,
            LocalIdentity = new ManagementTenantLocalIdentityDto { LocalSubjectId = fixture.Binding.LocalSubjectId },
            Organizer = organizer == 0 ? null : new ManagedProviderOrganizerDto
            {
                Kind = (ManagedProviderOrganizerKindDto)organizer,
                FullName = "DATA organizer",
                Description = "Exact organizer"
            }
        }
    };

    private sealed class Request : IAsyncDisposable
    {
        private readonly AsyncServiceScope root;
        private readonly IHttpContextAccessor accessor;
        private readonly HttpContext? previous;
        private readonly DefaultHttpContext context;
        private IPolicyMutationExecutionScope? selected;
        internal IServiceProvider Root => root.ServiceProvider;
        internal IServiceProvider Services => selected!.Services;

        internal Request(IServiceProvider provider)
        {
            root = provider.CreateAsyncScope();
            accessor = Root.GetRequiredService<IHttpContextAccessor>();
            previous = accessor.HttpContext;
            Root.GetRequiredService<Explore.Application.Contracts.Services.ITenantContextAccessor>()
                .BindExecutionTenant(PlatformDefaults.DefaultTenantId);
            context = new DefaultHttpContext { RequestServices = Root };
            accessor.HttpContext = context;
        }

        internal async Task InitializeAsync(LocalIdentityLifecycleHttpFixture fixture)
        {
            await Assert.That(ReferenceEquals(accessor.HttpContext, context)).IsTrue();
            context.Request.Headers.Authorization = "Bearer " + await fixture.SignInAsync();
            var authentication = await context.AuthenticateAsync();
            await Assert.That(authentication.Succeeded).IsTrue();
            context.User = authentication.Principal!;
            selected = await Root.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
                .CreateAsync(PolicyMutationScope.Instance, default);
            await Assert.That(ReferenceEquals(accessor.HttpContext, context)).IsTrue();
        }

        public async ValueTask DisposeAsync()
        {
            if (selected is not null) await selected.DisposeAsync();
            accessor.HttpContext = previous;
            await root.DisposeAsync();
        }
    }
}
