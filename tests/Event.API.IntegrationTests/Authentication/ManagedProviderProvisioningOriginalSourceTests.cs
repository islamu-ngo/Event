using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Constants;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.ManagedProviderProvisioning;
using Explore.Application.DTOs.Management;
using Explore.Application.DTOs.TenantSettings;
using Explore.Application.Features.ManagedProviderProvisioning.Requests.Commands;
using Explore.Application.Operations;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Explore.Persistence.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

/// <summary>
/// Retains original administrator source and routing controls while applying the
/// compiled provisioning DATA graph on its actual original physical owner.
/// </summary>
[NotInParallel]
public sealed class ManagedProviderProvisioningOriginalSourceTests
{
    [Test]
    [Arguments("unchanged")]
    [Arguments("binding")]
    [Arguments("grant")]
    [Arguments("stamp")]
    [Arguments("route")]
    [Arguments("request")]
    [Arguments("credential_after_reads")]
    [Arguments("support_unchanged")]
    [Arguments("support_revoke")]
    [Arguments("support_extend_after_reads")]
    public async Task OriginalAdministratorSourcesAndDeadlinesSurviveUntilPhysicalSettlement(string change)
    {
        await using var fixture = await Fixture();
        Guid grant = Guid.CreateVersion7();
        DateTimeOffset supportExpiry = fixture.Clock.GetUtcNow().AddMinutes(5);
        Guid? support = await Seed(fixture, grant, change.StartsWith("support_", StringComparison.Ordinal), supportExpiry);
        var identity = await fixture.ReadIdentityAsync();
        string credential = await fixture.SignInAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.Authenticate(credential);
        await request.SelectGlobalOwner();
        if (support is { } session)
        {
            request.Context.Request.Headers[SupportAccessHeaderNames.SessionId] = session.ToString("D");
            await Assert.That((await request.Services.GetRequiredService<ISupportAccessSessionService>()
                .GetCurrentAsync()).IsActive).IsTrue();
        }
        var command = Command(fixture);
        var prepared = await request.Services.GetRequiredService<IHumanManagedProviderProvisioningPreparation>()
            .PrepareAsync(command, fixture.Binding.LocalSubjectId);
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var owner = Owner(request.Services);
        bool observedOriginalAdministrator = false;
        HumanManagedProviderProvisioningRows? applied = null;
        bool observedLateRead = false;
        async Task Execute()
        {
            var settlement = await owner.ExecuteAsync(command, async token =>
            {
                var data = await prepared.AcquireAsync(command, token);
                await Assert.That(data.Refusal).IsNull();
                await Assert.That(data.AdministratorUserId).IsEqualTo(fixture.Binding.LocalSubjectId);
                var capacity = await data.ReadActivationCapacityAsync(token);
                await Assert.That(capacity.ActiveTenants >= 0 && capacity.ReservedTenants >= 0).IsTrue();
                var rows = await data.ApplyAsync(token);
                applied = rows;
                await Assert.That(rows.UserActorId).IsEqualTo(fixture.Binding.PersonalActorId);
                await Assert.That(rows.UserExternalLoginId).IsEqualTo(fixture.Binding.ExternalLoginId);
                await Assert.That(await context.Tenants.AsNoTracking()
                    .AnyAsync(row => row.Id == rows.TenantId, token)).IsTrue();
                observedOriginalAdministrator = await request.Services.GetRequiredService<IPlatformUserRoleRepository>()
                    .IsUserPlatformAdmin(fixture.Binding.LocalSubjectId);
                await Assert.That(observedOriginalAdministrator).IsTrue();
                switch (change)
                {
                    case "binding":
                        await context.UserExternalLogins.Where(row => row.Id == fixture.Binding.ExternalLoginId)
                            .ExecuteUpdateAsync(update => update.SetProperty(row => row.ProviderKey,
                                Guid.CreateVersion7().ToString("D")), token);
                        break;
                    case "grant":
                        await context.PlatformUserRoles.Where(row => row.Id == grant).ExecuteDeleteAsync(token);
                        break;
                    case "stamp":
                        await context.Set<LocalIdentityUser>().Where(row => row.Id == fixture.Binding.LocalSubjectId)
                            .ExecuteUpdateAsync(update => update.SetProperty(row => row.SecurityStamp,
                                Guid.CreateVersion7().ToString("N")), token);
                        break;
                    case "route":
                        await context.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Security.AuthorizationProvider)
                            .ExecuteUpdateAsync(update => update.SetProperty(row => row.Value, "\"cerbos\""), token);
                        break;
                    case "request":
                        request.Context.User = new System.Security.Claims.ClaimsPrincipal(request.Context.User);
                        break;
                    case "support_revoke":
                        await context.SupportAccessSessions.Where(row => row.Id == support)
                            .ExecuteUpdateAsync(update => update.SetProperty(row => row.StatusId,
                                (int)SupportAccessSessionStatusEnum.Revoked), token);
                        break;
                }
                if (change is "credential_after_reads" or "support_extend_after_reads")
                    request.Services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async finalToken =>
                    {
                        // This real source read finishes AFTER the preparation's terminal
                        // reader; every synchronous deadline must still follow it.
                        await context.PlatformUserRoles.AsNoTracking().SingleAsync(row => row.Id == grant, finalToken);
                        observedLateRead = true;
                        DateTimeOffset deadline = supportExpiry;
                        if (change == "credential_after_reads")
                        {
                            var lifetime = (await request.Services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
                                .ObserveAsync(request.Context.User, finalToken))!;
                            deadline = lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1);
                        }
                        else
                        {
                            await context.SupportAccessSessions.Where(row => row.Id == support)
                                .ExecuteUpdateAsync(update => update.SetProperty(row => row.ExpiresAtUtc,
                                    supportExpiry.AddMinutes(10)), finalToken);
                        }
                        fixture.Clock.Advance(deadline - fixture.Clock.GetUtcNow());
                    }, _ => { });
                return OriginalOwnerDecision<bool>.Commit(true);
            });
            await Assert.That(owner.Consume(command, settlement, OriginalOwnerDisposition.Committed)).IsTrue();
        }
        if (change is "unchanged" or "support_unchanged")
            await Execute();
        else if (change == "support_extend_after_reads")
        {
            // The compiled DATA seal is checked before clocks. This write occurs
            // after completion, so it is protocol tampering, not fresh authority
            // that could extend the original support horizon.
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(Execute);
            await Assert.That(failure!.Message)
                .IsEqualTo("provisioning_data_terminal_seal_missing_or_changed");
        }
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(Execute);
        await Assert.That(observedOriginalAdministrator).IsTrue();
        await Assert.That(applied).IsNotNull();
        await Assert.That(observedLateRead).IsEqualTo(change is "credential_after_reads" or "support_extend_after_reads");
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.PlatformUserRoles.AnyAsync(row => row.Id == grant)).IsTrue();
        await Assert.That(await observer.UserExternalLogins.Where(row => row.Id == fixture.Binding.ExternalLoginId)
            .Select(row => row.ProviderKey).SingleAsync()).IsEqualTo(fixture.Binding.LocalSubjectId.ToString("D"));
        await Assert.That(await observer.Set<LocalIdentityUser>().Where(row => row.Id == fixture.Binding.LocalSubjectId)
            .Select(row => row.SecurityStamp).SingleAsync()).IsEqualTo(identity.SecurityStamp);
        await Assert.That(await observer.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Security.AuthorizationProvider)
            .Select(row => row.Value).SingleAsync()).IsEqualTo("\"local\"");
        await Assert.That(await fixture.ReadIdentityAsync()).IsEqualTo(identity);
        bool committed = change is "unchanged" or "support_unchanged";
        await Assert.That(await observer.Tenants.AnyAsync(row => row.Id == applied!.TenantId)).IsEqualTo(committed);
        await Assert.That(await observer.TenantSettingsDocuments.CountAsync(row => row.TenantId == applied!.TenantId))
            .IsEqualTo(committed ? 2 : 0);
        await Assert.That(await observer.TenantUsers.AnyAsync(row => row.Id == applied!.TenantUserId)).IsEqualTo(committed);
        await Assert.That(await observer.TenantUserProfiles.AnyAsync(row => row.Id == applied!.TenantUserProfileId)).IsEqualTo(committed);
        await Assert.That(await observer.TenantUserRoleGrants.AnyAsync(row => row.Id == applied!.TenantUserRoleGrantId)).IsEqualTo(committed);
        await Assert.That(await observer.ExternalBindings.AnyAsync(row => row.ProviderKey == command.ProvisioningDto.ProviderKey
            && row.ExternalId == command.ProvisioningDto.ExternalCustomerId)).IsEqualTo(committed);
        await Assert.That(fixture.Smtp.Handoffs.Count).IsEqualTo(0);
        if (support is { } supportId)
        {
            var row = await observer.SupportAccessSessions.AsNoTracking().SingleAsync(row => row.Id == supportId);
            await Assert.That(row.StatusId).IsEqualTo((int)SupportAccessSessionStatusEnum.Active);
            await Assert.That(row.ExpiresAtUtc).IsEqualTo(supportExpiry);
        }
    }

    [Test]
    public async Task EqualRequestCannotReplaceOriginalOrReplayItsAcquisition()
    {
        await using var fixture = await Fixture();
        await Seed(fixture, Guid.CreateVersion7(), false, fixture.Clock.GetUtcNow().AddMinutes(5));
        string credential = await fixture.SignInAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.Authenticate(credential);
        await request.SelectGlobalOwner();
        var command = Command(fixture);
        var prepared = await request.Services.GetRequiredService<IHumanManagedProviderProvisioningPreparation>()
            .PrepareAsync(command, fixture.Binding.LocalSubjectId);
        var owner = Owner(request.Services);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => owner.ExecuteAsync(command, async token =>
        {
            await prepared.AcquireAsync(command with { }, token);
            return OriginalOwnerDecision<bool>.Commit(true);
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => owner.ExecuteAsync(command, async token =>
        {
            await prepared.AcquireAsync(command, token);
            return OriginalOwnerDecision<bool>.Commit(true);
        }));
        await Assert.That(request.Services.GetRequiredService<ExploreDbContext>().Database.CurrentTransaction).IsNull();
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.Tenants.AnyAsync(row => row.Slug == command.ProvisioningDto.TenantSlug)).IsFalse();
        await Assert.That(await observer.ExternalBindings.AnyAsync(row => row.ExternalId == command.ProvisioningDto.ExternalCustomerId)).IsFalse();
    }

    [Test]
    public async Task ExternalIdentityCannotBorrowApplicationOwnerForHumanSources()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            topology: IdentityDatabaseTopology.External,
            configureServices: Configure);
        await Seed(fixture, Guid.CreateVersion7(), false, fixture.Clock.GetUtcNow().AddMinutes(5));
        var identity = await fixture.ReadIdentityAsync();
        string credential = await fixture.SignInAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.Authenticate(credential);
        await request.SelectGlobalOwner();
        var command = Command(fixture);
        await Assert.ThrowsAsync<NotSupportedException>(() => request.Services
            .GetRequiredService<IHumanManagedProviderProvisioningPreparation>()
            .PrepareAsync(command, fixture.Binding.LocalSubjectId));
        await Assert.That(request.Services.GetRequiredService<ExploreDbContext>().Database.CurrentTransaction).IsNull();
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.Tenants.AnyAsync(row => row.Slug == command.ProvisioningDto.TenantSlug)).IsFalse();
        await Assert.That(await observer.ExternalBindings.AnyAsync(row => row.ExternalId == command.ProvisioningDto.ExternalCustomerId)).IsFalse();
        await Assert.That(await fixture.ReadIdentityAsync()).IsEqualTo(identity);
        await Assert.That(fixture.Smtp.Handoffs.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RuntimeCannotReplaceOriginalGlobalProvisioningAuthority()
    {
        await using var fixture = await Fixture();
        await Seed(fixture, Guid.CreateVersion7(), false, fixture.Clock.GetUtcNow().AddMinutes(5));
        string credential = await fixture.SignInAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.Authenticate(credential);
        var command = Command(fixture);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => request.Services
            .GetRequiredService<IHumanManagedProviderProvisioningPreparation>()
            .PrepareAsync(command, fixture.Binding.LocalSubjectId));
        await Assert.That(request.Services.GetRequiredService<ExploreDbContext>().Database.CurrentTransaction).IsNull();
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.Tenants.AnyAsync(row => row.Slug == command.ProvisioningDto.TenantSlug)).IsFalse();
        await Assert.That(fixture.Smtp.Handoffs.Count).IsEqualTo(0);
    }

    [Test]
    [Arguments("human")]
    [Arguments("management")]
    [Arguments("operation")]
    [Arguments("outbox")]
    public async Task HumanRoutingSelectsGlobalBeforeConstructionAndRejectsWorkerPayloads(string kind)
    {
        await using var fixture = await Fixture();
        await Seed(fixture, Guid.CreateVersion7(), false, fixture.Clock.GetUtcNow().AddMinutes(5));
        string credential = await fixture.SignInAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.Authenticate(credential);
        var command = Command(fixture);
        command = kind switch
        {
            "management" => command with
            {
                ManagementRequest = new()
                {
                    ExternalRequestId = Guid.CreateVersion7().ToString("D"),
                    ExternalCustomerReference = command.ProvisioningDto.ExternalCustomerId,
                    TenantName = command.ProvisioningDto.TenantFullName,
                    TenantSlug = command.ProvisioningDto.TenantSlug,
                    Administrator = new() { LocalIdentity = new() { LocalSubjectId = fixture.Binding.LocalSubjectId } },
                    Plan = new() { Key = "source-control" }
                }
            },
            "operation" => command with { OperationId = Guid.CreateVersion7() },
            "outbox" => command with { ExpectedOutboxMessageId = Guid.CreateVersion7() },
            _ => command
        };
        if (kind != "human")
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await PolicyMutationCommandScope.SelectAsync(command, request.Services, default));
            await Assert.That(request.Services.GetRequiredService<ExploreDbContext>().Database.CurrentTransaction).IsNull();
            await using var rejectedObserver = fixture.Native.CreateDatabase();
            await Assert.That(await rejectedObserver.Tenants.AnyAsync(row => row.Slug == command.ProvisioningDto.TenantSlug)).IsFalse();
            await Assert.That(await rejectedObserver.ExternalBindings.AnyAsync(
                row => row.ExternalId == command.ProvisioningDto.ExternalCustomerId)).IsFalse();
            await Assert.That(fixture.Smtp.Handoffs.Count).IsEqualTo(0);
            return;
        }
        var selection = await PolicyMutationCommandScope.SelectAsync(command, request.Services, default);
        await Assert.That(selection).IsEqualTo(PolicyMutationScope.Instance);
        await request.SelectGlobalOwner();
        var services = request.Services;
        var prepared = await services.GetRequiredService<IHumanManagedProviderProvisioningPreparation>()
            .PrepareAsync(command, fixture.Binding.LocalSubjectId);
        // A subsequent tenant selection cannot replace the already-bound physical owner.
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
                .CreateAsync(new(PlatformDefaults.DefaultTenantId), default));
        await Assert.That(services.GetRequiredService<ExploreDbContext>().Database.CurrentTransaction).IsNull();
        var owner = Owner(services);
        var settlement = await owner.ExecuteAsync(command, async token =>
        {
            var data = await prepared.AcquireAsync(command, token);
            await Assert.That(data.Refusal).IsNull();
            await Assert.That(data.AdministratorUserId).IsEqualTo(fixture.Binding.LocalSubjectId);
            var capacity = await data.ReadActivationCapacityAsync(token);
            await Assert.That(capacity.ActiveTenants >= 0 && capacity.ReservedTenants >= 0).IsTrue();
            return OriginalOwnerDecision<HumanManagedProviderProvisioningRows>.Commit(await data.ApplyAsync(token));
        });
        var rows = owner.Consume(command, settlement, OriginalOwnerDisposition.Committed);
        await Assert.That(services.GetRequiredService<ExploreDbContext>().Database.CurrentTransaction).IsNull();
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.Tenants.AnyAsync(row => row.Id == rows.TenantId
            && row.Slug == command.ProvisioningDto.TenantSlug)).IsTrue();
        await Assert.That(fixture.Smtp.Handoffs.Count).IsEqualTo(0);
    }

    private static Task<LocalIdentityLifecycleHttpFixture> Fixture() =>
        LocalIdentityLifecycleHttpFixture.CreateAsync(
            configureServices: Configure);

    private static void Configure(IServiceCollection services)
    {
        services.AddHumanManagedProviderProvisioningData();
        services.AddHumanManagedProviderProvisioningPreparation();
    }

    private static IOriginalOwnerSettlementBoundary Owner(IServiceProvider services) =>
        services.GetRequiredService<IPrivacyIdentityFenceAuthority>() as IOriginalOwnerSettlementBoundary
        ?? services.GetRequiredService<IOriginalOwnerSettlementBoundary>();

    private static EnsureManagedProviderClientProvisionedCommand Command(LocalIdentityLifecycleHttpFixture fixture) =>
        new()
        {
            ProvisioningDto = new ManagedProviderClientProvisioningDto
            {
                ProviderKey = "source-control",
                ExternalSystem = "source-control",
                ExternalCustomerId = Guid.CreateVersion7().ToString("D"),
                TenantFullName = "Original provisioning source",
                TenantSlug = "original-" + Guid.CreateVersion7().ToString("N"),
                DirectoryOperatorIdentity = DirectoryIdentity(),
                ActivateTenant = false,
                LocalIdentity = new ManagementTenantLocalIdentityDto
                {
                    LocalSubjectId = fixture.Binding.LocalSubjectId
                }
            }
        };

    private static TenantDirectoryOperatorIdentityInputDto DirectoryIdentity()
    {
        var input = new TenantDirectoryOperatorIdentityInputDto
        {
            PublicName = "Original Operator",
            LegalName = "Original Operator ASBL",
            OperatorKindCode = "registered_organization",
            JurisdictionCountryCode = "BE",
            RegistrationIdentifier = "BE 0123.456.789",
            PublicContactEmail = "contact@example.test",
            LegalNoticeUrl = "https://example.test/legal",
            TermsUrl = "https://example.test/terms",
            PrivacyUrl = "https://example.test/privacy"
        };
        var readiness = TenantDirectoryOperatorIdentity.Evaluate(input.ToPayload(),
            TenantDirectoryOperatorIdentityCapability.Activation);
        var normalized = readiness.Identity?.ToSettings()
            ?? throw new InvalidOperationException("The original operator identity must be activation-ready.");
        return input with
        {
            PublicName = normalized.PublicName,
            LegalName = normalized.LegalName,
            OperatorKindCode = normalized.OperatorKindCode,
            JurisdictionCountryCode = normalized.JurisdictionCountryCode,
            RegistrationIdentifier = normalized.RegistrationIdentifier,
            PublicContactEmail = normalized.PublicContactEmail,
            LegalNoticeUrl = normalized.LegalNoticeUrl,
            TermsUrl = normalized.TermsUrl,
            PrivacyUrl = normalized.PrivacyUrl
        };
    }

    private static async Task<Guid?> Seed(LocalIdentityLifecycleHttpFixture fixture, Guid grant, bool support,
        DateTimeOffset supportExpiry)
    {
        await using var seed = fixture.Native.CreateDatabase();
        seed.PlatformUserRoles.Add(new PlatformUserRole
        {
            Id = grant,
            UserId = fixture.Binding.LocalSubjectId,
            User = null!,
            RoleId = (int)RoleEnum.Admin,
            Role = null!,
            GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
        });
        Guid? sessionId = null;
        if (support)
        {
            var session = SupportAccessSession.Start(fixture.Binding.LocalSubjectId, PlatformDefaults.DefaultTenantId,
                SupportAccessModeEnum.Write, "maintenance", "Provisioning source control", "fixture-ticket",
                fixture.Clock.GetUtcNow().AddMinutes(-1), supportExpiry);
            seed.SupportAccessSessions.Add(session);
            sessionId = session.Id;
        }
        foreach (var (key, value) in new[]
        {
            (GovernanceSettingKeys.Deployment.Mode, "\"MultiTenant\""),
            (GovernanceSettingKeys.Security.AuthorizationProvider, "\"local\""),
            (GovernanceSettingKeys.SupportAccess.Enabled, "true"),
            (GovernanceSettingKeys.SupportAccess.AllowWriteMode, "true")
        })
        {
            var row = await seed.SystemSettings.SingleOrDefaultAsync(setting => setting.SettingKey == key);
            if (row is null) seed.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = value });
            else row.Value = value;
        }
        await seed.SaveChangesAsync();
        return sessionId;
    }

    private sealed class Request : IAsyncDisposable
    {
        private readonly AsyncServiceScope scope;
        private readonly IHttpContextAccessor accessor;
        private readonly HttpContext? previous;
        private IPolicyMutationExecutionScope? selected;
        internal IServiceProvider Services => selected?.Services ?? scope.ServiceProvider;
        internal DefaultHttpContext Context { get; }

        internal Request(IServiceProvider services)
        {
            scope = services.CreateAsyncScope();
            Context = new DefaultHttpContext { RequestServices = Services };
            accessor = Services.GetRequiredService<IHttpContextAccessor>();
            Services.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(PlatformDefaults.DefaultTenantId);
            previous = accessor.HttpContext;
            accessor.HttpContext = Context;
        }

        internal async Task Authenticate(string credential)
        {
            Context.Request.Headers.Authorization = "Bearer " + credential;
            var authentication = await Context.AuthenticateAsync();
            await Assert.That(authentication.Succeeded).IsTrue();
            Context.User = authentication.Principal!;
        }

        internal async Task SelectGlobalOwner()
        {
            selected = await scope.ServiceProvider.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
                .CreateAsync(PolicyMutationScope.Instance, default);
        }

        public async ValueTask DisposeAsync()
        {
            if (selected is not null) await selected.DisposeAsync();
            accessor.HttpContext = previous;
            await scope.DisposeAsync();
        }
    }
}
