using System.Collections.Immutable;
using System.Data.Common;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.InstanceAdmin.Handlers.Commands;
using Explore.Application.Features.Settings.Handlers.Commands;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Notifications;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanSettingsMutationPreparationTests
{
    [Test]
    [Arguments("publication-lock", "unchanged")]
    [Arguments("publication-lock", "binding-revoked")]
    [Arguments("publication-lock", "terminal-expiry")]
    [Arguments("publication-unlock", "unchanged")]
    [Arguments("publication-unlock", "binding-revoked")]
    [Arguments("publication-unlock", "terminal-expiry")]
    [Arguments("publication-update", "unchanged")]
    [Arguments("publication-update", "binding-revoked")]
    [Arguments("publication-update", "terminal-expiry")]
    [Arguments("publication-batch", "unchanged")]
    [Arguments("publication-batch", "binding-revoked")]
    [Arguments("publication-batch", "terminal-expiry")]
    [Arguments("publication-reset", "unchanged")]
    [Arguments("publication-reset", "binding-revoked")]
    [Arguments("publication-reset", "terminal-expiry")]
    [Arguments("publication-reset", "locked-policy")]
    [Arguments("location-update", "unchanged")]
    [Arguments("location-update", "binding-revoked")]
    [Arguments("location-update", "terminal-expiry")]
    [Arguments("location-tenant-set", "unchanged")]
    [Arguments("location-tenant-set", "binding-revoked")]
    [Arguments("location-tenant-set", "terminal-expiry")]
    [Arguments("location-reset", "unchanged")]
    [Arguments("location-reset", "binding-revoked")]
    [Arguments("location-reset", "terminal-expiry")]
    public async Task SpecializedConsumersSettleOriginalEffectsAndProjectionCorrections(string operation, string change)
    {
        bool location = operation.StartsWith("location-", StringComparison.Ordinal);
        bool reset = operation.EndsWith("-reset", StringComparison.Ordinal);
        bool tenant = reset || operation == "location-tenant-set";
        string key = location ? GovernanceSettingKeys.LocationPrivacy.AllowPublicExactAddress
            : EventSettingDefinitions.RequireApproval.Key;
        var before = new ConsumerBeforeOwnerTransaction();
        var terminal = new SpecializedBeforeSettlement(key);
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            interceptor: change == "terminal-expiry" ? terminal : before);
        await SeedPlatformAdministratorAsync(fixture);
        Guid? placementId = null;
        int originalVersion = 0;
        await using (var seed = fixture.Native.CreateDatabase())
        {
            foreach (string policyKey in PublicationPolicySettingKeys.All)
                await SeedSpecializedSettingAsync(seed, policyKey, "true");
            await SeedSpecializedSettingAsync(seed, key, "true",
                operation == "publication-unlock" || change == "locked-policy");
            await SeedSpecializedSettingAsync(seed, EventSettingDefinitions.MaxSessionsPerEvent.Key, "100");
            if (tenant)
            {
                var member = await seed.TenantUsers.SingleOrDefaultAsync(row =>
                    row.TenantId == PlatformDefaults.DefaultTenantId && row.UserId == fixture.Binding.LocalSubjectId);
                if (member is null)
                {
                    member = new TenantUser
                    {
                        TenantId = PlatformDefaults.DefaultTenantId,
                        Tenant = null!,
                        UserId = fixture.Binding.LocalSubjectId,
                        User = null!,
                        StatusId = (int)TenantUserStatusEnum.Active,
                        JoinedAt = fixture.Clock.GetUtcNow().UtcDateTime
                    };
                    seed.TenantUsers.Add(member);
                }
                seed.TenantUserRoleGrants.Add(new TenantUserRoleGrant
                {
                    TenantId = PlatformDefaults.DefaultTenantId,
                    Tenant = null!,
                    TenantUserId = member.Id,
                    TenantUser = null!,
                    RoleId = (int)RoleEnum.TenantAdmin,
                    Role = null!,
                    RoleScopeId = (int)RoleScopeEnum.Tenant,
                    GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
                });
                seed.TenantSettingOverrides.Add(new TenantSetting
                {
                    TenantId = PlatformDefaults.DefaultTenantId,
                    Tenant = null!,
                    SettingKey = key,
                    Value = reset ? "false" : "true",
                    CreatedBy = fixture.Binding.LocalSubjectId,
                    CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime
                });
            }
            if (location && !reset)
            {
                var actor = await seed.Actors.SingleAsync(row => row.UserId == fixture.Binding.LocalSubjectId);
                var source = new Explore.Domain.Event(EventStatusEnum.Draft)
                {
                    TenantId = PlatformDefaults.DefaultTenantId,
                    Tenant = null!,
                    ActorId = actor.Id,
                    Actor = null!,
                    Title = "Settings governance correction",
                    EventStatus = null!,
                    EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
                    VisibilityTypeId = (int)VisibilityTypeEnum.Public,
                    VisibilityType = null!,
                    EventFormatId = (int)EventFormatEnum.Local,
                    EventFormat = null!,
                    CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime,
                    ConcurrencyStamp = Guid.CreateVersion7()
                };
                var venue = new Location
                {
                    TenantId = PlatformDefaults.DefaultTenantId,
                    Tenant = null!,
                    FullName = "Governance venue",
                    Country = "BE",
                    City = "Brussels",
                    CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime,
                    ConcurrencyStamp = Guid.CreateVersion7()
                };
                venue.SetManualAddress("Governance fixture street", "1000");
                seed.AddRange(source, venue);
                await seed.SaveChangesAsync();
                var placement = EventLocation.CreatePhysical(PlatformDefaults.DefaultTenantId, source.Id, venue.Id,
                    fixture.Binding.LocalSubjectId, fixture.Clock.GetUtcNow().UtcDateTime);
                await new EventLocationRepository(seed).AddAsync(placement, CancellationToken.None);
                seed.EventLocationDisclosureAudits.Add(placement.ChangeDisclosurePolicy(
                    EventLocationDisclosureFields.StreetAddress, LocationDisclosureAudienceEnum.AnyCurrentRegistrant,
                    null, placement.PolicyVersion, fixture.Binding.LocalSubjectId,
                    EventLocationDisclosureAuditReasonEnum.OrganizerPolicyChange,
                    fixture.Clock.GetUtcNow().UtcDateTime, needsPrivacyReview: false));
                placementId = placement.Id;
                originalVersion = placement.PolicyVersion;
            }
            await seed.SaveChangesAsync();
        }
        await using var request = await Request.CreateAsync(fixture);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(tenant ? new(PlatformDefaults.DefaultTenantId) : PolicyMutationScope.Instance, CancellationToken.None);
        var services = selected.Services;
        await services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(CancellationToken.None);
        var owner = services.GetRequiredService<ExploreDbContext>();
        var notifications = new ConsumerNotifications(owner);
        object sink = (IEnumerable<INotificationHandler<SettingChangedNotification>>)[notifications];
        before.Owner = owner;
        before.BeforeStart = async token =>
        {
            if (change != "binding-revoked") return;
            await using var revoker = fixture.Native.CreateDatabase();
            await revoker.UserExternalLogins.Where(row => row.Id == fixture.Binding.ExternalLoginId).ExecuteDeleteAsync(token);
        };
        terminal.Owner = owner;
        terminal.BeforeSettlement = () =>
        {
            var lifetime = services.GetRequiredService<Explore.Persistence.Database.ExploreDbContextExecutionLease>().HumanLifetime!;
            services.GetRequiredService<IHumanActionCommitFinalizer>().Register(_ =>
            {
                fixture.Clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1) - fixture.Clock.GetUtcNow());
                return Task.CompletedTask;
            }, _ => { });
        };
        var execution = ExecuteAsync();
        bool committed = change is "unchanged" or "locked-policy";
        if (committed) await Assert.That(await execution).IsTrue();
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution);
        await Assert.That(change == "terminal-expiry" ? terminal.Observed : before.Observed).IsTrue();

        await using var observer = fixture.Native.CreateDatabase();
        string? stored = tenant ? await observer.TenantSettingOverrides.Where(row =>
            row.TenantId == PlatformDefaults.DefaultTenantId && row.SettingKey == key).Select(row => row.Value).SingleOrDefaultAsync()
            : await observer.SystemSettings.Where(row => row.SettingKey == key).Select(row => row.Value).SingleOrDefaultAsync();
        await Assert.That(stored).IsEqualTo(reset ? committed ? null : "false"
            : committed && operation is not ("publication-lock" or "publication-unlock") ? "false" : "true");
        if (!tenant && !location)
        {
            bool locked = await observer.SystemSettings.Where(row => row.SettingKey == key).Select(row => row.IsLocked).SingleAsync();
            await Assert.That(locked).IsEqualTo(operation == "publication-lock" ? committed
                : operation == "publication-unlock" && !committed);
        }
        if (operation == "publication-batch")
            await Assert.That(await observer.SystemSettings.Where(row => row.SettingKey == EventSettingDefinitions.MaxSessionsPerEvent.Key)
                .Select(row => row.Value).SingleAsync()).IsEqualTo(committed ? "55" : "100");
        if (placementId.HasValue)
        {
            var placement = await observer.EventLocations.AsNoTracking().SingleAsync(row => row.Id == placementId);
            await Assert.That(placement.PolicyVersion).IsEqualTo(originalVersion + (committed ? 1 : 0));
            await Assert.That(placement.NeedsPrivacyReview).IsEqualTo(committed);
            await Assert.That(await observer.EventLocationDisclosureAudits.CountAsync(row =>
                row.EventLocationId == placementId && row.Reason == EventLocationDisclosureAuditReasonEnum.GovernanceTightening))
                .IsEqualTo(committed ? 1 : 0);
            await Assert.That(await observer.Set<LocationPrivacyCorrectionReceipt>().CountAsync()).IsEqualTo(committed ? 1 : 0);
        }
        await Assert.That(notifications.Published.Count).IsEqualTo(committed ? operation == "publication-batch" ? 2 : 1 : 0);

        async Task<bool> ExecuteAsync() => operation switch
        {
            "publication-lock" => (await ActivatorUtilities.CreateInstance<LockSettingCommandHandler>(services, sink)
                .ExecuteAsync(new() { Key = key, Scope = SettingScope.Instance }, CancellationToken.None)).IsSuccess,
            "publication-unlock" => (await ActivatorUtilities.CreateInstance<UnlockSettingCommandHandler>(services, sink)
                .ExecuteAsync(new() { Key = key, Scope = SettingScope.Instance }, CancellationToken.None)).IsSuccess,
            "publication-batch" => (await ActivatorUtilities.CreateInstance<UpdateSettingBatchCommandHandler>(services, sink)
                .ExecuteAsync(new()
                {
                    Category = "Events",
                    Scope = SettingScope.Instance,
                    Values = new Dictionary<string, string> { [key] = "false", [EventSettingDefinitions.MaxSessionsPerEvent.Key] = "55" }
                }, CancellationToken.None)).Success,
            "location-tenant-set" => (await ActivatorUtilities.CreateInstance<SetInstanceTenantSettingCommandHandler>(services, sink)
                .ExecuteAsync(new(PlatformDefaults.DefaultTenantId, key, "false"), CancellationToken.None)).IsSuccess,
            "publication-reset" or "location-reset" => (await ActivatorUtilities.CreateInstance<ResetSettingCommandHandler>(services, sink)
                .ExecuteAsync(new() { Key = key, Scope = SettingScope.Tenant }, CancellationToken.None)).IsSuccess,
            _ => (await ActivatorUtilities.CreateInstance<UpdateSettingCommandHandler>(services, sink)
                .ExecuteAsync(new() { Key = key, Scope = SettingScope.Instance, Value = "false" }, CancellationToken.None)).IsSuccess
        };
    }

    [Test]
    public async Task InstancePrivacyResetIsRejectedWithoutStartingAnOwnerOrChangingRootSettings()
    {
        var before = new ConsumerBeforeOwnerTransaction();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: before);
        await SeedPlatformAdministratorAsync(fixture);
        string key = GovernanceSettingKeys.LocationPrivacy.AllowPublicExactAddress;
        await using (var seed = fixture.Native.CreateDatabase())
        {
            await SeedSpecializedSettingAsync(seed, key, "true");
            await seed.SaveChangesAsync();
        }
        await using var request = await Request.CreateAsync(fixture);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        var owner = selected.Services.GetRequiredService<ExploreDbContext>();
        before.Owner = owner;
        var notifications = new ConsumerNotifications(owner);
        var handler = ActivatorUtilities.CreateInstance<ResetSettingCommandHandler>(selected.Services,
            (IEnumerable<INotificationHandler<SettingChangedNotification>>)[notifications]);
        var response = await handler.ExecuteAsync(
            new() { Key = key, Scope = SettingScope.Instance }, CancellationToken.None);
        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(before.Observed).IsFalse();
        await Assert.That(owner.Database.CurrentTransaction).IsNull();
        await Assert.That(notifications.Published.Count).IsEqualTo(0);
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.SystemSettings.Where(row => row.SettingKey == key)
            .Select(row => row.Value).SingleAsync()).IsEqualTo("true");
        await Assert.That(await observer.Set<LocationPrivacyCorrectionReceipt>().CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task PublicSpecializedStoresRejectOwnerOnlyAndCallerDerivedEffects()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await SeedPlatformAdministratorAsync(fixture);
        await using var request = await Request.CreateAsync(fixture);
        await using var selection = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        var services = selection.Services;
        var preparation = services.GetRequiredService<IHumanSettingsMutationPreparation>();
        string key = EventSettingDefinitions.RequireApproval.Key;
        var proof = await preparation.PrepareAsync(new LockSettingCommand { Key = key, Scope = SettingScope.Instance });
        var store = services.GetRequiredService<ICoordinatedSettingMutationStore>();
        var location = services.GetRequiredService<ILocationPrivacyGovernanceStore>();
        await preparation.ExecutePreparedAsync(proof, async (_, token) =>
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => location.ReadActiveProjectionsAsync(null, token));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.ReadTenantSnapshotAsync(PlatformDefaults.DefaultTenantId, token));
            await preparation.ExecutePublicationJoinedAsync([key], null, fixture.Binding.LocalSubjectId,
                PublicationPolicyLockedSystemBehavior.Reject,
                async (mutations, childToken) =>
                {
                    await Assert.That(mutations.Single().IsLocked).IsTrue();
                    await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.WriteInstanceAsync(
                        mutations, fixture.Binding.LocalSubjectId, fixture.Clock.GetUtcNow().UtcDateTime, childToken));
                    await Assert.ThrowsAsync<UnauthorizedAccessException>(() => preparation.RequireCurrentOwnedAsync(
                        SettingScope.Instance, null, null, key, HumanSettingsMutationKind.SetValue, "substituted", cancellationToken: childToken));
                    return true;
                }, token);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => preparation.RequireCurrentOwnedAsync(
                SettingScope.Instance, null, null, key, HumanSettingsMutationKind.SetValue, "false", cancellationToken: token));
            return true;
        });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.ReadInstanceSnapshotAsync(CancellationToken.None));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NonEmailJoinedFamiliesUseOneOriginalOwnerAndRollbackTogether(bool expireAtSettlement)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await SeedPlatformAdministratorAsync(fixture);
        string publication = EventSettingDefinitions.RequireApproval.Key;
        string location = GovernanceSettingKeys.LocationPrivacy.AllowPublicExactAddress;
        string resource = EventResourceSettingDefinitions.MaxActiveResources.Key;
        string visitor = GovernanceSettingKeys.PublicExperience.VisitorAccessMode;
        string ordinary = EventSettingDefinitions.MaxSessionsPerEvent.Key;
        var values = ImmutableDictionary<string, string>.Empty
            .Add(publication, "false").Add(location, "false").Add(resource, "400")
            .Add(visitor, "\"DirectoryListingOnly\"").Add(ordinary, "55");
        var baseline = new Dictionary<string, string>();
        await using (var seed = fixture.Native.CreateDatabase())
        {
            foreach (string key in PublicationPolicySettingKeys.All)
                await SeedSpecializedSettingAsync(seed, key, "true");
            await SeedSpecializedSettingAsync(seed, location, "true");
            await SeedSpecializedSettingAsync(seed, resource, "500");
            await SeedSpecializedSettingAsync(seed, visitor, "\"FullRegistrationAndAuth\"");
            await SeedSpecializedSettingAsync(seed, ordinary, "100");
            await seed.SaveChangesAsync();
            baseline = await seed.SystemSettings.Where(row => values.Keys.Contains(row.SettingKey))
                .ToDictionaryAsync(row => row.SettingKey, row => row.Value);
        }
        await using var request = await Request.CreateAsync(fixture);
        await using var selection = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        var services = selection.Services;
        var preparation = services.GetRequiredService<IHumanSettingsMutationPreparation>();
        // This tests the original owner's composition seam, not a relaxation of the
        // public handler's category validation. The handler's Events mixed case is above.
        var proof = await preparation.PrepareAsync(new UpdateSettingBatchCommand
        {
            Category = "Events",
            Scope = SettingScope.Instance,
            Values = values
        }, values);
        var subject = services.GetRequiredService<IHumanCommitScopePreparation>().Subject!;
        var context = services.GetRequiredService<ExploreDbContext>();
        var resolver = services.GetRequiredService<IHierarchicalSettingsResolver>();
        var execution = services.GetRequiredService<ISettingMutationLock>().ExecuteOrderedGroupsAsync(
            [Explore.Application.Services.VisitorAccessCapabilityResolver.AuthoritySettingKeys,
             EventResourceSettingMutationGuard.Keys, PublicationPolicySettingKeys.All, values.Keys],
            token => preparation.ExecutePreparedAsync(proof, async (active, ownerToken) =>
            {
                var transaction = context.Database.CurrentTransaction;
                var resourceResult = await services.GetRequiredService<IEventResourceSettingsWriter>().ApplyAsync(
                    [new(null, resource, EventResourceSettingMutationKind.SetValue, values[resource])],
                    subject, SettingsMutationOperation.InstanceSettings, subject.PrincipalId, ownerToken);
                resourceResult.EnsureAccepted();
                await preparation.RequireOwnedAsync(active, new(HumanSettingsOperation.InstanceSettings,
                    SettingScope.Instance, null, null, ordinary, HumanSettingsMutationKind.SetValue, "55"), ownerToken);
                var visitorResult = await services.GetRequiredService<IVisitorAccessSettingsWriter>().ApplyAsync(
                    [new(null, visitor, VisitorAccessSettingMutationKind.SetValue, values[visitor])],
                    subject, SettingsMutationOperation.InstanceSettings, subject.PrincipalId, ownerToken);
                visitorResult.EnsureAccepted();
                var publicationResult = await services.GetRequiredService<IPublicationPolicyMutationBoundary>().ApplyInstanceAsync(
                    new(subject.PrincipalId, fixture.Clock.GetUtcNow().UtcDateTime,
                        [new(publication, PublicationPolicyMutationKind.Set, values[publication], null, false)]), ownerToken);
                await Assert.That(publicationResult.Success).IsTrue();
                var locationResult = await services.GetRequiredService<ILocationPrivacyGovernanceMutationService>().ExecuteAsync(
                    location, values[location], SettingScope.Instance, null, subject.PrincipalId,
                    async persistToken =>
                    {
                        await resolver.SetValueAsync(location, values[location], SettingScope.Instance, Guid.Empty,
                            subject.PrincipalId, persistToken);
                        return baseline[location];
                    }, ownerToken);
                await Assert.That(locationResult.Accepted).IsTrue();
                await resolver.SetValueAsync(ordinary, values[ordinary], SettingScope.Instance, Guid.Empty,
                    subject.PrincipalId, ownerToken);
                await Assert.That(ReferenceEquals(transaction, context.Database.CurrentTransaction)).IsTrue();
                if (expireAtSettlement)
                {
                    var lifetime = services.GetRequiredService<Explore.Persistence.Database.ExploreDbContextExecutionLease>().HumanLifetime!;
                    services.GetRequiredService<IHumanActionCommitFinalizer>().Register(_ =>
                    {
                        fixture.Clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1) - fixture.Clock.GetUtcNow());
                        return Task.CompletedTask;
                    }, _ => { });
                }
                return true;
            }, token), CancellationToken.None);
        if (expireAtSettlement) await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution);
        else await Assert.That(await execution).IsTrue();
        await using var observer = fixture.Native.CreateDatabase();
        foreach (var (key, value) in values)
            await Assert.That(await observer.SystemSettings.Where(row => row.SettingKey == key).Select(row => row.Value).SingleAsync())
                .IsEqualTo(expireAtSettlement ? baseline[key] : value);
    }

    [Test]
    public async Task LocationCannotCorrectProjectionsWithoutItsOriginalPersistedSettingEffect()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await SeedPlatformAdministratorAsync(fixture);
        string key = GovernanceSettingKeys.LocationPrivacy.AllowPublicExactAddress;
        await using (var seed = fixture.Native.CreateDatabase())
        {
            await SeedSpecializedSettingAsync(seed, key, "true");
            await seed.SaveChangesAsync();
        }
        await using var request = await Request.CreateAsync(fixture);
        await using var selection = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        var services = selection.Services;
        var preparation = services.GetRequiredService<IHumanSettingsMutationPreparation>();
        var proof = await preparation.PrepareAsync(new UpdateSettingCommand
        {
            Key = key,
            Scope = SettingScope.Instance,
            Value = "false"
        }, "false");
        await Assert.ThrowsAsync<InvalidOperationException>(() => preparation.ExecutePreparedAsync(proof,
            (_, token) => services.GetRequiredService<ILocationPrivacyGovernanceMutationService>().ExecuteAsync(
                key, "false", SettingScope.Instance, null, fixture.Binding.LocalSubjectId,
                _ => Task.FromResult<string?>("true"), token)));
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.SystemSettings.Where(row => row.SettingKey == key).Select(row => row.Value).SingleAsync())
            .IsEqualTo("true");
        await Assert.That(await observer.Set<LocationPrivacyCorrectionReceipt>().AnyAsync()).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SpecializedOriginalOwnerRetainsWholeStateSafetyAndTenantCeilings(bool tenantCeiling)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await SeedPlatformAdministratorAsync(fixture);
        string key = tenantCeiling ? GovernanceSettingKeys.LocationPrivacy.AllowPublicExactAddress
            : EventSettingDefinitions.RequireApproval.Key;
        await using (var seed = fixture.Native.CreateDatabase())
        {
            foreach (string publicationKey in PublicationPolicySettingKeys.All)
                await SeedSpecializedSettingAsync(seed, publicationKey, "true");
            await SeedSpecializedSettingAsync(seed, GovernanceSettingKeys.EventReporting.IntakeEnabled, "false");
            await SeedSpecializedSettingAsync(seed, key, tenantCeiling ? "false" : "true");
            await SeedSpecializedSettingAsync(seed, EventSettingDefinitions.MaxSessionsPerEvent.Key, "100");
            await seed.SaveChangesAsync();
        }
        await using var request = await Request.CreateAsync(fixture);
        await using var selection = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(tenantCeiling ? new(PlatformDefaults.DefaultTenantId) : PolicyMutationScope.Instance, CancellationToken.None);
        var services = selection.Services;
        await services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(CancellationToken.None);
        var notifications = new ConsumerNotifications(services.GetRequiredService<ExploreDbContext>());
        object sink = (IEnumerable<INotificationHandler<SettingChangedNotification>>)[notifications];
        bool accepted = tenantCeiling
            ? (await ActivatorUtilities.CreateInstance<SetInstanceTenantSettingCommandHandler>(services, sink)
                .ExecuteAsync(new(PlatformDefaults.DefaultTenantId, key, "true"), CancellationToken.None)).IsSuccess
            : (await ActivatorUtilities.CreateInstance<UpdateSettingBatchCommandHandler>(services, sink)
                .ExecuteAsync(new()
                {
                    Category = "Events",
                    Scope = SettingScope.Instance,
                    Values = new Dictionary<string, string> { [key] = "false", [EventSettingDefinitions.MaxSessionsPerEvent.Key] = "55" }
                }, CancellationToken.None)).Success;
        await Assert.That(accepted).IsFalse();
        await Assert.That(notifications.Published.Count).IsEqualTo(0);
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.SystemSettings.Where(row => row.SettingKey == key).Select(row => row.Value).SingleAsync())
            .IsEqualTo(tenantCeiling ? "false" : "true");
        await Assert.That(await observer.TenantSettingOverrides.AnyAsync(row =>
            row.TenantId == PlatformDefaults.DefaultTenantId && row.SettingKey == key)).IsFalse();
        await Assert.That(await observer.SystemSettings.Where(row => row.SettingKey == EventSettingDefinitions.MaxSessionsPerEvent.Key)
            .Select(row => row.Value).SingleAsync()).IsEqualTo("100");
    }

    private static async Task SeedSpecializedSettingAsync(ExploreDbContext context, string key, string value, bool locked = false)
    {
        var setting = context.SystemSettings.Local.SingleOrDefault(row => row.SettingKey == key)
            ?? await context.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == key);
        if (setting is null)
        {
            setting = new SystemSetting { SettingKey = key, Value = value, ValueType = SettingRegistry.Get(key)!.ValueType };
            context.SystemSettings.Add(setting);
        }
        setting.Value = value;
        setting.IsLocked = locked;
    }

    private sealed class SpecializedBeforeSettlement(string key) : DbCommandInterceptor
    {
        internal ExploreDbContext? Owner { get; set; }
        internal Action? BeforeSettlement { get; set; }
        internal bool Observed { get; private set; }
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (ReferenceEquals(eventData.Context, Owner) && Owner?.Database.CurrentTransaction is not null
                && command.Parameters.Cast<DbParameter>().Any(parameter => Equals(parameter.Value, key))
                && BeforeSettlement is { } callback)
            {
                BeforeSettlement = null;
                Observed = true;
                callback();
            }
            return ValueTask.FromResult(result);
        }
    }
}
