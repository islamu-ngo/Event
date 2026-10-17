using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using EventVisitorCapabilitySqliteFixture = Event.Api.IntegrationTests.Authentication.ResourceVisitorAuthenticatedFixture;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Users.Requests.Queries;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
public sealed class ResourceVisitorHumanCommitAuthorityTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task InternalWriterCannotReclassifyAnAuthenticatedSelectedContext(bool visitor)
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        await GrantTenantAsync(fixture);
        await SubjectAsync(fixture);
        var unit = new EfCoreUnitOfWork(fixture.Context);
        var locks = new RelationalSettingMutationLock(fixture.Context, unit);
        if (visitor)
        {
            var writer = new VisitorAccessSettingsWriter(fixture.Context, locks, unit,
                new EventParticipationConfigurationRepository(fixture.Context),
                new ConfigurationBuilder().Build());
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => writer.ApplyInternalAsync(
                [new(fixture.TenantId, GovernanceSettingKeys.PublicExperience.VisitorAccessMode,
                    VisitorAccessSettingMutationKind.SetValue, "\"DirectoryListingOnly\"")], null));
        }
        else
        {
            var writer = new EventResourceSettingsWriter(fixture.Context, locks, unit);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => writer.ApplyInternalAsync(
                [new(fixture.TenantId, GovernanceSettingKeys.EventResources.MaxActiveResources,
                    EventResourceSettingMutationKind.SetValue, "12")], null));
        }
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
        await Assert.That(await fixture.Context.TenantSettingOverrides.AsNoTracking()
            .CountAsync(row => row.TenantId == fixture.TenantId)).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task HumanWriterRejectsAnotherAuditActorBeforeEnteringItsOwner(bool visitor)
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        await GrantTenantAsync(fixture);
        var subject = await SubjectAsync(fixture);
        var authority = Authority(fixture, subject, HumanAdministratorCommitPurpose.RuntimeTenant);
        var unit = new EfCoreUnitOfWork(fixture.Context);
        var locks = new RelationalSettingMutationLock(fixture.Context, unit);
        Guid otherActor = Guid.CreateVersion7();
        if (visitor)
        {
            var writer = new VisitorAccessSettingsWriter(fixture.Context, locks, unit,
                new EventParticipationConfigurationRepository(fixture.Context),
                new ConfigurationBuilder().Build(), authority, authority,
                fixture.Services.GetRequiredService<IHumanActionCommitFinalizer>(), subject,
                fixture.Services.GetRequiredService<IHumanSettingsMutationPreparation>());
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => writer.ApplyAsync(
                [new(fixture.TenantId, GovernanceSettingKeys.PublicExperience.VisitorAccessMode,
                    VisitorAccessSettingMutationKind.SetValue, "\"DirectoryListingOnly\"")],
                subject, SettingsMutationOperation.TenantSettings, otherActor));
        }
        else
        {
            var writer = new EventResourceSettingsWriter(fixture.Context, locks, unit, authority, authority,
                fixture.Services.GetRequiredService<IHumanActionCommitFinalizer>(), subject,
                fixture.Services.GetRequiredService<IHumanSettingsMutationPreparation>(), TimeProvider.System);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => writer.ApplyAsync(
                [new(fixture.TenantId, GovernanceSettingKeys.EventResources.MaxActiveResources,
                    EventResourceSettingMutationKind.SetValue, "12")],
                subject, SettingsMutationOperation.TenantSettings, otherActor));
        }
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
        await Assert.That(await fixture.Context.TenantSettingOverrides.AsNoTracking()
            .CountAsync(row => row.TenantId == fixture.TenantId)).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RevokedTenantAdministrationRollsBackEarlierBorrowedWrites(bool visitor)
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var context = fixture.Context;
        var grant = await GrantTenantAsync(fixture);
        var subject = await SubjectAsync(fixture);
        var authority = Authority(fixture, subject, HumanAdministratorCommitPurpose.RuntimeTenant);
        await context.TenantUserRoleGrants.Where(row => row.Id == grant.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.RevokedAt, DateTime.UtcNow));
        var unit = new EfCoreUnitOfWork(context);
        var locks = new RelationalSettingMutationLock(context, unit);
        string[] keys = visitor
            ? VisitorAccessCapabilityResolver.AuthoritySettingKeys.ToArray()
            : EventResourceSettingMutationGuard.Keys.ToArray();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            locks.ExecuteOrderedGroupsAsync<bool>([keys], token =>
                ((ISerializableAttemptOwner)unit).ExecuteAsync<bool>(async inner =>
                {
                    await context.Tenants.Where(row => row.Id == fixture.TenantId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.FullName, "Must roll back"), inner);
                    await ApplyAsync(fixture, authority, subject, visitor,
                        SettingsMutationOperation.TenantSettings, fixture.TenantId, inner);
                    return true;
                }, token)));
        await Assert.That(await context.Tenants.AsNoTracking().Where(row => row.Id == fixture.TenantId)
            .Select(row => row.FullName).SingleAsync()).IsEqualTo("Visitor authority");
        await Assert.That(await context.TenantSettingOverrides.AsNoTracking()
            .CountAsync(row => row.TenantId == fixture.TenantId)).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task InstanceAdministratorCannotUseOrdinaryTenantSettingPredicate(bool visitor)
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        await GrantInstanceAsync(fixture);
        var subject = await SubjectAsync(fixture);
        var authority = Authority(fixture, subject, HumanAdministratorCommitPurpose.RuntimeTenant);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ApplyAsync(
            fixture, authority, subject, visitor, SettingsMutationOperation.TenantSettings,
            fixture.TenantId, CancellationToken.None));
        await Assert.That(await fixture.Context.TenantSettingOverrides.AsNoTracking().CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExplicitInstanceTenantAdministrationUsesItsSelectedExactTenantOwner(bool visitor)
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        await GrantInstanceAsync(fixture);
        var subject = await SubjectAsync(fixture);
        var authority = Authority(fixture, subject, HumanAdministratorCommitPurpose.RuntimeTenant);
        await ApplyAsync(fixture, authority, subject, visitor, SettingsMutationOperation.InstanceTenantSettings,
            fixture.TenantId, CancellationToken.None);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ApplyAsync(
            fixture, authority, subject, visitor, SettingsMutationOperation.InstanceTenantSettings,
            Guid.CreateVersion7(), CancellationToken.None));
        await Assert.That(await fixture.Context.TenantSettingOverrides.AsNoTracking()
            .CountAsync(row => row.TenantId == fixture.TenantId)).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CurrentTenantAdministratorRetainsPolicyWriteAndUnrelatedSetting(bool visitor)
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        await GrantTenantAsync(fixture);
        fixture.Context.SystemSettings.Add(new SystemSetting
        {
            Id = Guid.CreateVersion7(),
            SettingKey = "unrelated.setting",
            Value = "retained",
            CreatedAt = DateTime.UtcNow
        });
        await fixture.Context.SaveChangesAsync();
        var subject = await SubjectAsync(fixture);
        var authority = Authority(fixture, subject, HumanAdministratorCommitPurpose.RuntimeTenant);
        await ApplyAsync(fixture, authority, subject, visitor, SettingsMutationOperation.TenantSettings,
            fixture.TenantId, CancellationToken.None);
        await Assert.That(await fixture.Context.TenantSettingOverrides.AsNoTracking()
            .CountAsync(row => row.TenantId == fixture.TenantId)).IsEqualTo(1);
        await Assert.That(await fixture.Context.SystemSettings.AsNoTracking()
            .Where(row => row.SettingKey == "unrelated.setting").Select(row => row.Value).SingleAsync())
            .IsEqualTo("retained");
    }

    [Test]
    public async Task InstanceImportBorrowsOwnerAndDoesNotRequireTenantAdministratorGrant()
    {
        var rejection = new RejectAppliedImportReceipt();
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync(rejection);
        await GrantInstanceAsync(fixture);
        if (!await fixture.Context.PaidEventPolicyVersions.AnyAsync(row => row.IsActive && row.TenantId == null))
        {
            fixture.Context.PaidEventPolicyVersions.Add(PaidEventPolicyVersion.CreateDefaultInstance());
            await fixture.Context.SaveChangesAsync();
        }
        var executor = fixture.Services.GetRequiredService<IConfigurationInstanceImportExecutor>();
        var snapshot = await executor.ReadSnapshotAsync(ConfigurationManifestExportView.Overrides, default);
        var manifest = JsonNode.Parse(snapshot.Utf8Json.Span)!;
        string slug = await fixture.Context.Tenants.Where(row => row.Id == fixture.TenantId)
            .Select(row => row.Slug).SingleAsync();
        var target = manifest["spec"]!["tenants"]!.AsArray().Single(row =>
            row!["metadata"]!["name"]!.GetValue<string>() == slug)!;
        target["spec"]!["settings"]![GovernanceSettingKeys.EventResources.MaxActiveResources] = 12;
        var session = await executor.CreateAsync(JsonSerializer.SerializeToUtf8Bytes(manifest), default);
        var request = new ConfigurationImportPreviewRequest
        {
            SelectedSectionKeys = ["tenant.settings"],
            Mappings = new Dictionary<string, string>(),
            ApplyMode = ConfigurationImportApplyMode.ApplySelected,
            GrantedApprovalCodes = []
        };
        await Assert.That((await executor.PreviewAsync(session.SessionId, session.AccessToken, request, default))
            .IsApplyReady).IsTrue();
        await Assert.ThrowsAsync<RejectedImportReceiptException>(() =>
            executor.ApplyAsync(session.SessionId, session.AccessToken, request, null, null, default));
        await Assert.That(rejection.ObservedPersistedResource).IsTrue();
        await Assert.That(await fixture.Context.TenantSettingOverrides.AsNoTracking()
            .CountAsync(row => row.TenantId == fixture.TenantId)).IsEqualTo(0);
    }

    private sealed class RejectedImportReceiptException : InvalidOperationException;

    private sealed class RejectAppliedImportReceipt : SaveChangesInterceptor
    {
        private IDbContextTransaction? original;
        private Guid? writtenTenant;
        internal bool ObservedPersistedResource { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is ExploreDbContext context)
            {
                if (context.ChangeTracker.Entries<TenantSetting>().Any(entry =>
                    entry.Entity.SettingKey == GovernanceSettingKeys.EventResources.MaxActiveResources
                    && entry.State is EntityState.Added or EntityState.Modified))
                {
                    original ??= context.Database.CurrentTransaction;
                    writtenTenant ??= context.ChangeTracker.Entries<TenantSetting>()
                        .Single(entry => entry.Entity.SettingKey == GovernanceSettingKeys.EventResources.MaxActiveResources
                            && entry.State is EntityState.Added or EntityState.Modified).Entity.TenantId;
                }
                if (context.ChangeTracker.Entries<ConfigurationImportOperation>()
                    .Any(entry => entry.State == EntityState.Added
                        && entry.Entity.Status == ConfigurationImportOperationStatus.Applied))
                {
                    await Assert.That(original).IsNotNull();
                    await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(original);
                    // The original instance owner has no ambient tenant. Observe only the
                    // exact target already captured from its write, on that same transaction.
                    ObservedPersistedResource = await context.TenantSettingOverrides.IgnoreQueryFilters().AsNoTracking().AnyAsync(row =>
                        row.TenantId == writtenTenant
                        && row.SettingKey == GovernanceSettingKeys.EventResources.MaxActiveResources && row.Value == "12",
                        cancellationToken);
                    throw new RejectedImportReceiptException();
                }
            }
            return result;
        }
    }

    [Test]
    public async Task ResourceImportCannotSelectImportAuthorityOnOrdinaryOwner()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        await GrantInstanceAsync(fixture);
        var subject = await SubjectAsync(fixture);
        var authority = Authority(fixture, subject, HumanAdministratorCommitPurpose.RuntimeTenant);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ApplyAsync(fixture, authority, subject, false,
            SettingsMutationOperation.ConfigurationInstanceImport, fixture.TenantId, CancellationToken.None));
    }

    [Test]
    public async Task AuditActorCannotReplaceBoundHumanSubject()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        await GrantTenantAsync(fixture);
        var subject = await SubjectAsync(fixture);
        var http = fixture.Services.GetRequiredService<IHttpContextAccessor>();
        http.HttpContext!.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString())], "fixture"));
        var different = (await HumanCommitSubject.ResolveAsync(http.HttpContext.User,
            fixture.Services.GetRequiredService<IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>>(),
            CancellationToken.None))!;
        var authority = Authority(fixture, subject, HumanAdministratorCommitPurpose.RuntimeTenant);
        var unit = new EfCoreUnitOfWork(fixture.Context);
        var writer = new EventResourceSettingsWriter(fixture.Context,
            new RelationalSettingMutationLock(fixture.Context, unit), unit, authority, authority,
            fixture.Services.GetRequiredService<IHumanActionCommitFinalizer>(), subject,
            fixture.Services.GetRequiredService<IHumanSettingsMutationPreparation>(), TimeProvider.System);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => writer.ApplyAsync(
            [new(fixture.TenantId, GovernanceSettingKeys.EventResources.MaxActiveResources,
                EventResourceSettingMutationKind.SetValue, "12")],
            different, SettingsMutationOperation.TenantSettings, subject.PrincipalId));
    }

    [Test]
    public async Task EmptyHumanBatchStillRequiresCurrentAuthorityAndNullAuditDoesNotBypassIt()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var subject = await SubjectAsync(fixture);
        var authority = Authority(fixture, subject, HumanAdministratorCommitPurpose.RuntimeTenant);
        var unit = new EfCoreUnitOfWork(fixture.Context);
        var writer = new EventResourceSettingsWriter(fixture.Context,
            new RelationalSettingMutationLock(fixture.Context, unit), unit, authority, authority,
            fixture.Services.GetRequiredService<IHumanActionCommitFinalizer>(), subject,
            fixture.Services.GetRequiredService<IHumanSettingsMutationPreparation>(), TimeProvider.System);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => writer.ApplyAsync(
            [], subject, SettingsMutationOperation.TenantSettings, actorUserId: null));
        await Assert.That(await fixture.Context.TenantSettingOverrides.AsNoTracking().CountAsync()).IsEqualTo(0);
    }

    private static async Task ApplyAsync(EventVisitorCapabilitySqliteFixture fixture,
        ExternalApiKeyIssuanceAuthority authority, HumanCommitSubject subject, bool visitor,
        SettingsMutationOperation operation, Guid? tenantId, CancellationToken token)
    {
        var unit = new EfCoreUnitOfWork(fixture.Context);
        var locks = new RelationalSettingMutationLock(fixture.Context, unit);
        if (visitor)
        {
            var writer = new VisitorAccessSettingsWriter(fixture.Context, locks, unit,
                new EventParticipationConfigurationRepository(fixture.Context),
                new ConfigurationBuilder().Build(), authority, authority,
                fixture.Services.GetRequiredService<IHumanActionCommitFinalizer>(), subject,
                fixture.Services.GetRequiredService<IHumanSettingsMutationPreparation>());
            (await writer.ApplyAsync(
                [new(tenantId, GovernanceSettingKeys.PublicExperience.VisitorAccessMode,
                    VisitorAccessSettingMutationKind.SetValue, "\"DirectoryListingOnly\"")],
                subject, operation, subject.PrincipalId, token)).EnsureAccepted();
        }
        else
        {
            var writer = new EventResourceSettingsWriter(fixture.Context, locks, unit, authority, authority,
                fixture.Services.GetRequiredService<IHumanActionCommitFinalizer>(), subject,
                fixture.Services.GetRequiredService<IHumanSettingsMutationPreparation>(), TimeProvider.System);
            (await writer.ApplyAsync(
                [new(tenantId, GovernanceSettingKeys.EventResources.MaxActiveResources,
                    EventResourceSettingMutationKind.SetValue, "12")],
                subject, operation, subject.PrincipalId, token)).EnsureAccepted();
        }
    }

    private static ExternalApiKeyIssuanceAuthority Authority(EventVisitorCapabilitySqliteFixture fixture,
        HumanCommitSubject subject, HumanAdministratorCommitPurpose purpose) =>
        new(fixture.Context, fixture.Services.GetRequiredService<IHttpContextAccessor>(),
            fixture.Services.GetRequiredService<ExploreDbContextExecutionLease>().HumanScope!);

    private static async Task<HumanCommitSubject> SubjectAsync(EventVisitorCapabilitySqliteFixture fixture)
    {
        var preparation = fixture.Services.GetRequiredService<IHumanCommitScopePreparation>();
        await preparation.PrepareAsync(CancellationToken.None);
        return preparation.Subject!;
    }

    private static async Task<TenantUserRoleGrant> GrantTenantAsync(EventVisitorCapabilitySqliteFixture fixture)
    {
        var membership = await fixture.Context.TenantUsers.SingleAsync(row => row.UserId == fixture.UserId);
        var grant = new TenantUserRoleGrant
        {
            Id = Guid.CreateVersion7(),
            TenantId = fixture.TenantId,
            Tenant = null!,
            TenantUserId = membership.Id,
            TenantUser = null!,
            RoleId = (int)RoleEnum.TenantAdmin,
            Role = null!,
            RoleScopeId = (int)RoleScopeEnum.Tenant,
            GrantedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        fixture.Context.TenantUserRoleGrants.Add(grant);
        await fixture.Context.SaveChangesAsync();
        return grant;
    }

    private static async Task GrantInstanceAsync(EventVisitorCapabilitySqliteFixture fixture)
    {
        fixture.Context.PlatformUserRoles.Add(new PlatformUserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = fixture.UserId,
            User = null!,
            RoleId = (int)RoleEnum.Admin,
            Role = null!,
            GrantedAt = DateTime.UtcNow
        });
        await fixture.Context.SaveChangesAsync();
    }
}
