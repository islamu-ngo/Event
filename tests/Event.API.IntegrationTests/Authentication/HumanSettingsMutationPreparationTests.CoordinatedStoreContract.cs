using System.Collections.Immutable;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Persistence;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanSettingsMutationPreparationTests
{
    private static readonly Guid ContractEarlier = Guid.Parse("018e4e5c-7f00-7000-8000-00000000a001");
    private static readonly Guid ContractRequested = Guid.Parse("018e4e5c-7f00-7000-8000-00000000a002");
    private static readonly Guid ContractLater = Guid.Parse("018e4e5c-7f00-7000-8000-00000000a003");
    private const string ContractProviderValue = "provider-row-must-not-change";

    [Test]
    public async Task CoordinatedInstanceReadRetainsAllTenantSourcesInPolicyOrderAndExcludesProviderRows()
    {
        await using var fixture = await PublicationFixture.CreateAsync();
        await SeedCoordinatedContractAsync(fixture);
        var snapshot = await ExecuteCoordinatedValuesAsync(fixture, null, PublicationValues((ApprovalKey, "true")),
            async (services, token) =>
            {
                var observed = await services.GetRequiredService<ICoordinatedSettingMutationStore>()
                    .ReadInstanceSnapshotAsync(token);
                await Assert.That((await ApplyCoordinatedAsync(fixture, services, null,
                    [new(ApprovalKey, PublicationPolicyMutationKind.Set, "true", null, false)], token)).Success).IsTrue();
                return observed;
            });
        await Assert.That(snapshot.SystemValues.SequenceEqual(new PublicationPolicySystemValueSnapshot[]
        {
            new(IntakeKey, " true \n", false), new(ApprovalKey, "false", false),
            new(UserSubmissionKey, "true", false), new(OrganizationSubmissionKey, "true", false),
            new(GroupSubmissionKey, "true", false)
        })).IsTrue();
        await Assert.That(snapshot.TenantValues.SequenceEqual(new PublicationPolicyTenantValueSnapshot[]
        {
            new(ContractEarlier, ApprovalKey, "false"),
            new(ContractRequested, IntakeKey, " false \n"),
            new(ContractRequested, UserSubmissionKey, "true"),
            new(ContractLater, IntakeKey, "true")
        })).IsTrue();
        await Assert.That(snapshot.SystemValues.Any(row => row.Key == ContractProviderKey)).IsFalse();
        await Assert.That(snapshot.TenantValues.Any(row => row.Key == ContractProviderKey)).IsFalse();
        await AssertCoordinatedProvidersAsync(fixture);
    }

    [Test]
    public async Task CoordinatedRequestedTenantWriteCannotBorrowAmbientOrThirdTenantEffects()
    {
        await using var fixture = await PublicationFixture.CreateAsync();
        await SeedCoordinatedContractAsync(fixture);
        var before = await ReadCoordinatedStateAsync(fixture);
        Guid ambient = fixture.Request.Services.GetRequiredService<ITenantContext>().TenantId;
        await Assert.That(ambient).IsNotEqualTo(ContractRequested);
        var result = await fixture.ExecuteContractAsync(new(ContractRequested),
            (services, token) => services.GetRequiredService<IHumanSettingsMutationPreparation>()
                .PrepareAsync(new Explore.Application.Features.InstanceAdmin.Requests.Commands.SetInstanceTenantSettingCommand(
                    ContractRequested, IntakeKey, "true"), "true", token),
            async (services, token) =>
            {
                var store = services.GetRequiredService<ICoordinatedSettingMutationStore>();
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.ReadTenantSnapshotAsync(ContractEarlier, token));
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.ReadTenantSnapshotAsync(ContractLater, token));
                var original = await store.ReadTenantSnapshotAsync(ContractRequested, token);
                await Assert.That(original.TenantValues.Select(row => row.TenantId).Distinct())
                    .IsEquivalentTo([(Guid?)ContractRequested]);
                return await ApplyCoordinatedAsync(fixture, services, ContractRequested,
                    [new(IntakeKey, PublicationPolicyMutationKind.Set, "true", ContractRequested, null)], token);
            });
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.DeferredNotifications).HasSingleItem();
        await AssertCoordinatedNotificationAsync(fixture, result, 0, ContractRequested, IntakeKey, " false \n", "true");
        var after = await ReadCoordinatedStateAsync(fixture);
        await Assert.That(after.System.SequenceEqual(before.System)).IsTrue();
        await Assert.That(after.Tenant.Where(row => row.Tenant != ContractRequested)
            .SequenceEqual(before.Tenant.Where(row => row.Tenant != ContractRequested))).IsTrue();
        await Assert.That(after.Tenant.Single(row => row.Tenant == ContractRequested && row.Key == IntakeKey).Value)
            .IsEqualTo("true");
        await Assert.That(fixture.Request.Services.GetRequiredService<ITenantContext>().TenantId).IsEqualTo(ambient);
        await AssertCoordinatedProvidersAsync(fixture);
    }

    [Test]
    public async Task CoordinatedTenantSetAndLegalLockedOverrideRemovalReturnExactOrderedNeutralEffects()
    {
        await using var fixture = await PublicationFixture.CreateAsync();
        await SeedCoordinatedContractAsync(fixture);
        await fixture.SeedSystemAsync(IntakeKey, "true", locked: true);
        var before = await ReadCoordinatedStateAsync(fixture);
        var result = await ExecuteCoordinatedValuesAsync(fixture, ContractRequested,
            PublicationValues((UserSubmissionKey, "false"), (ApprovalKey, "true"), (IntakeKey, "true")),
            (services, token) => ApplyCoordinatedAsync(fixture, services, ContractRequested,
                [
                    new(UserSubmissionKey, PublicationPolicyMutationKind.Set, "false", ContractRequested, null),
                    new(ApprovalKey, PublicationPolicyMutationKind.Set, "true", ContractRequested, null),
                    new(IntakeKey, PublicationPolicyMutationKind.Set, "true", ContractRequested, null)
                ], token, PublicationPolicyLockedSystemBehavior.RemoveOverride));
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.DeferredNotifications.Length).IsEqualTo(3);
        await AssertCoordinatedNotificationAsync(fixture, result, 0, ContractRequested, IntakeKey, " false \n", null);
        await AssertCoordinatedNotificationAsync(fixture, result, 1, ContractRequested, ApprovalKey, null, "true");
        await AssertCoordinatedNotificationAsync(fixture, result, 2, ContractRequested, UserSubmissionKey, "true", "false");
        var after = await ReadCoordinatedStateAsync(fixture);
        await Assert.That(after.System.SequenceEqual(before.System)).IsTrue();
        await Assert.That(after.Tenant.Where(row => row.Tenant != ContractRequested)
            .SequenceEqual(before.Tenant.Where(row => row.Tenant != ContractRequested))).IsTrue();
        await Assert.That(after.Tenant.Any(row => row.Tenant == ContractRequested && row.Key == IntakeKey)).IsFalse();
        var created = after.Tenant.Single(row => row.Tenant == ContractRequested && row.Key == ApprovalKey);
        var updated = after.Tenant.Single(row => row.Tenant == ContractRequested && row.Key == UserSubmissionKey);
        await Assert.That(created.Value).IsEqualTo("true");
        await Assert.That(created.Locked).IsFalse();
        await Assert.That(created.CreatedBy).IsEqualTo(fixture.Actor);
        await Assert.That(created.CreatedAt).IsEqualTo(fixture.Identity.Clock.GetUtcNow().UtcDateTime);
        await Assert.That(updated.Value).IsEqualTo("false");
        await Assert.That(updated.UpdatedBy).IsEqualTo(fixture.Actor);
        await Assert.That(updated.UpdatedAt).IsEqualTo(fixture.Identity.Clock.GetUtcNow().UtcDateTime);
        await AssertCoordinatedProvidersAsync(fixture);
        var neutral = await ExecuteCoordinatedValuesAsync(fixture, ContractRequested,
            PublicationValues((ApprovalKey, "true"), (UserSubmissionKey, "false")),
            (services, token) => ApplyCoordinatedAsync(fixture, services, ContractRequested,
                [
                    new(UserSubmissionKey, PublicationPolicyMutationKind.Set, "false", ContractRequested, null),
                    new(ApprovalKey, PublicationPolicyMutationKind.Set, "true", ContractRequested, null)
                ], token));
        await Assert.That(neutral.Success).IsTrue();
        await Assert.That(neutral.DeferredNotifications).IsEmpty();
        await AssertCoordinatedStateAsync(fixture, after);
    }

    [Test]
    public async Task CoordinatedInstanceExistingLockUsesRealLockProducerAndResetHasNoInstanceAuthority()
    {
        await using var fixture = await PublicationFixture.CreateAsync();
        await SeedCoordinatedContractAsync(fixture);
        await fixture.SeedSystemAsync(ApprovalKey, "true");
        var before = await ReadCoordinatedStateAsync(fixture);
        var result = await fixture.ExecuteContractAsync(PolicyMutationScope.Instance,
            (services, token) => services.GetRequiredService<IHumanSettingsMutationPreparation>()
                .PrepareAsync(new LockSettingCommand { Key = ApprovalKey, Scope = SettingScope.Instance }, token),
            (services, token) => ApplyCoordinatedAsync(fixture, services, null,
                [new(ApprovalKey, PublicationPolicyMutationKind.Set, "true", null, true)], token));
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.DeferredNotifications.Length).IsEqualTo(1);
        await AssertCoordinatedNotificationAsync(fixture, result, 0, null, ApprovalKey, "true", "true",
            SettingSource.SystemLocked);
        var after = await ReadCoordinatedStateAsync(fixture);
        var locked = after.System.Single(row => row.Key == ApprovalKey);
        var original = before.System.Single(row => row.Key == ApprovalKey);
        await Assert.That(locked.Locked).IsTrue();
        await Assert.That(locked.Id).IsEqualTo(original.Id);
        await Assert.That(locked.Value).IsEqualTo(original.Value);
        await Assert.That(locked.CreatedAt).IsEqualTo(original.CreatedAt);
        await Assert.That(locked.CreatedBy).IsEqualTo(original.CreatedBy);
        await Assert.That(locked.UpdatedBy).IsEqualTo(fixture.Actor);
        await Assert.That(locked.UpdatedAt).IsEqualTo(fixture.Identity.Clock.GetUtcNow().UtcDateTime);
        await Assert.That(after.System.Where(row => row.Key != ApprovalKey)
            .SequenceEqual(before.System.Where(row => row.Key != ApprovalKey))).IsTrue();
        await Assert.That(after.Tenant.SequenceEqual(before.Tenant)).IsTrue();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.ExecuteContractAsync(PolicyMutationScope.Instance,
                (services, token) => services.GetRequiredService<IHumanSettingsMutationPreparation>()
                    .PrepareAsync(new ResetSettingCommand { Key = UserSubmissionKey, Scope = SettingScope.Instance }, token),
                (_, _) => Task.FromResult(true)));
        await AssertCoordinatedStateAsync(fixture, after);
    }

    [Test]
    public async Task CoordinatedMissingRowValueCreationHasCanonicalMetadataButCannotBorrowLockedCreation()
    {
        await using var fixture = await PublicationFixture.CreateAsync();
        await SeedCoordinatedContractAsync(fixture);
        await fixture.SeedSystemAsync(ApprovalKey, "true");
        await using (var seed = fixture.Identity.Native.CreateDatabase())
            await seed.SystemSettings.Where(row => row.SettingKey == GroupSubmissionKey).ExecuteDeleteAsync();
        var absent = await ReadCoordinatedStateAsync(fixture);
        await fixture.ExecuteContractAsync(PolicyMutationScope.Instance,
            (services, token) => services.GetRequiredService<IHumanSettingsMutationPreparation>()
                .PrepareAsync(new LockSettingCommand { Key = GroupSubmissionKey, Scope = SettingScope.Instance }, token),
            async (services, token) =>
            {
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ApplyCoordinatedAsync(fixture, services, null,
                    [new(GroupSubmissionKey, PublicationPolicyMutationKind.Set, "true", null, true)], token));
                return true;
            });
        await AssertCoordinatedStateAsync(fixture, absent);
        var result = await ExecuteCoordinatedValuesAsync(fixture, null, PublicationValues((GroupSubmissionKey, "false")),
            async (services, token) =>
            {
                var preparation = services.GetRequiredService<IHumanSettingsMutationPreparation>();
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => preparation.RequireCurrentOwnedAsync(
                    SettingScope.Instance, null, null, GroupSubmissionKey, HumanSettingsMutationKind.SetLock,
                    isLocked: true, cancellationToken: token));
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    services.GetRequiredService<ICoordinatedSettingMutationStore>().WriteInstanceAsync(
                        [new(GroupSubmissionKey, PublicationPolicyMutationKind.Set, "false", null, true)],
                        fixture.Actor, fixture.Identity.Clock.GetUtcNow().UtcDateTime, token));
                return await ApplyCoordinatedAsync(fixture, services, null,
                    [new(GroupSubmissionKey, PublicationPolicyMutationKind.Set, "false", null, false)], token);
            });
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.DeferredNotifications).HasSingleItem();
        await AssertCoordinatedNotificationAsync(fixture, result, 0, null, GroupSubmissionKey, null, "false");
        var after = await ReadCoordinatedStateAsync(fixture);
        await Assert.That(after.System.Where(row => row.Key != GroupSubmissionKey).SequenceEqual(absent.System)).IsTrue();
        await Assert.That(after.Tenant.SequenceEqual(absent.Tenant)).IsTrue();
        await using var observer = fixture.Identity.Native.CreateDatabase();
        var row = await observer.SystemSettings.AsNoTracking().SingleAsync(item => item.SettingKey == GroupSubmissionKey);
        var definition = SettingRegistry.Get(GroupSubmissionKey)!;
        await Assert.That(row.Id).IsEqualTo(Explore.Persistence.Seed.SeedIds.SystemSettingGroupSubmissionEnabledId);
        await Assert.That(row.Value).IsEqualTo("false");
        await Assert.That(row.IsLocked).IsFalse();
        await Assert.That(row.ValueType).IsEqualTo(definition.ValueType);
        await Assert.That(row.Description).IsEqualTo(definition.Description);
        await Assert.That(row.Category).IsEqualTo(definition.Category);
        await Assert.That(row.AllowedValues).IsNull();
        await Assert.That(row.DisplayOrder).IsEqualTo(5);
        await Assert.That(row.CreatedBy).IsEqualTo(fixture.Actor);
        await Assert.That(row.CreatedAt).IsEqualTo(fixture.Identity.Clock.GetUtcNow().UtcDateTime);
        await AssertCoordinatedProvidersAsync(fixture);
    }

    [Test]
    public async Task CoordinatedAbsentTenantResetIsNeutralAndInstanceResetIsRejectedEvenWhenAbsent()
    {
        await using var fixture = await PublicationFixture.CreateAsync();
        await SeedCoordinatedContractAsync(fixture);
        await fixture.SeedSystemAsync(ApprovalKey, "true");
        var before = await ReadCoordinatedStateAsync(fixture);
        var result = await fixture.ExecuteContractAsync(new(ContractRequested),
            (services, token) => services.GetRequiredService<IHumanSettingsMutationPreparation>()
                .PrepareAsync(new ResetSettingCommand { Key = GroupSubmissionKey, Scope = SettingScope.Tenant }, token),
            (services, token) => ApplyCoordinatedAsync(fixture, services, ContractRequested,
                [new(GroupSubmissionKey, PublicationPolicyMutationKind.Remove, null, ContractRequested, null)], token));
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.DeferredNotifications).IsEmpty();
        await AssertCoordinatedStateAsync(fixture, before);
        await using (var seed = fixture.Identity.Native.CreateDatabase())
            await seed.SystemSettings.Where(row => row.SettingKey == GroupSubmissionKey).ExecuteDeleteAsync();
        var absent = await ReadCoordinatedStateAsync(fixture);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.ExecuteContractAsync(PolicyMutationScope.Instance,
                (services, token) => services.GetRequiredService<IHumanSettingsMutationPreparation>()
                    .PrepareAsync(new ResetSettingCommand { Key = GroupSubmissionKey, Scope = SettingScope.Instance }, token),
                (_, _) => Task.FromResult(true)));
        await AssertCoordinatedStateAsync(fixture, absent);
    }

    [Test]
    public async Task CoordinatedSuccessfulEffectsAreRolledBackByOriginalOwnerFailureWithoutChangingExceptionIdentity()
    {
        await using var fixture = await PublicationFixture.CreateAsync();
        await SeedCoordinatedContractAsync(fixture);
        var before = await ReadCoordinatedStateAsync(fixture);
        var failure = new CoordinatedOwnerFailure();
        PublicationPolicyMutationResult? escaped = null;
        var caught = await Assert.ThrowsAsync<CoordinatedOwnerFailure>(async () =>
            escaped = await ExecuteCoordinatedValuesAsync<PublicationPolicyMutationResult>(fixture, ContractRequested,
                PublicationValues((ApprovalKey, "true"), (GroupSubmissionKey, "false")),
                async (services, token) =>
                {
                    var result = await ApplyCoordinatedAsync(fixture, services, ContractRequested,
                        [
                            new(ApprovalKey, PublicationPolicyMutationKind.Set, "true", ContractRequested, null),
                            new(GroupSubmissionKey, PublicationPolicyMutationKind.Set, "false", ContractRequested, null)
                        ], token);
                    await Assert.That(result.Success).IsTrue();
                    await Assert.That(result.DeferredNotifications.Length).IsEqualTo(2);
                    var owner = services.GetRequiredService<ExploreDbContext>();
                    await Assert.That(await owner.TenantSettingOverrides.AsNoTracking()
                        .Where(row => row.TenantId == ContractRequested && row.SettingKey == ApprovalKey)
                        .Select(row => row.Value).SingleAsync(token)).IsEqualTo("true");
                    throw failure;
                }));
        await Assert.That(caught).IsSameReferenceAs(failure);
        await Assert.That(escaped).IsNull();
        await AssertCoordinatedStateAsync(fixture, before);
    }

    [Test]
    public async Task CoordinatedRealDatabaseFailureAfterFirstEffectRollsBackEveryRowAndRetainsOriginalException()
    {
        await using var fixture = await PublicationFixture.CreateAsync();
        await SeedCoordinatedContractAsync(fixture);
        await fixture.SeedTenantAsync(GroupSubmissionKey, "true", ContractRequested);
        await using (var seed = fixture.Identity.Native.CreateDatabase())
        {
            var sql = new HumanPrincipalSourceSql(seed);
            await seed.Database.ExecuteSqlRawAsync($"""
                CREATE TRIGGER coordinated_contract_failure BEFORE UPDATE ON {sql.Table(typeof(TenantSetting))}
                WHEN NEW.{sql.Column(typeof(TenantSetting), nameof(TenantSetting.SettingKey))} = '{GroupSubmissionKey}'
                BEGIN SELECT RAISE(ABORT, 'coordinated real second write failure'); END;
                """);
        }
        var before = await ReadCoordinatedStateAsync(fixture);
        DbUpdateException? original = null;
        var caught = await Assert.ThrowsAsync<DbUpdateException>(() => ExecuteCoordinatedValuesAsync(fixture,
            ContractRequested, PublicationValues((ApprovalKey, "true"), (GroupSubmissionKey, "false")),
            async (services, token) =>
            {
                var first = await ApplyCoordinatedAsync(fixture, services, ContractRequested,
                    [new(ApprovalKey, PublicationPolicyMutationKind.Set, "true", ContractRequested, null)], token);
                await Assert.That(first.Success).IsTrue();
                var owner = services.GetRequiredService<ExploreDbContext>();
                await Assert.That(await owner.TenantSettingOverrides.AsNoTracking()
                    .Where(row => row.TenantId == ContractRequested && row.SettingKey == ApprovalKey)
                    .Select(row => row.Value).SingleAsync(token)).IsEqualTo("true");
                try
                {
                    return await ApplyCoordinatedAsync(fixture, services, ContractRequested,
                        [new(GroupSubmissionKey, PublicationPolicyMutationKind.Set, "false", ContractRequested, null)], token);
                }
                catch (DbUpdateException failure)
                {
                    original = failure;
                    throw;
                }
            }));
        await Assert.That(original).IsNotNull();
        await Assert.That(caught).IsSameReferenceAs(original);
        await Assert.That(caught!.InnerException!.Message.Contains("coordinated real second write failure",
            StringComparison.Ordinal)).IsTrue();
        await AssertCoordinatedStateAsync(fixture, before);
    }

    [Test]
    public async Task CoordinatedCallerCancellationAtSubscribedAppliedEffectRollsBackBeforeAcknowledgment()
    {
        await using var fixture = await PublicationFixture.CreateAsync();
        await SeedCoordinatedContractAsync(fixture);
        var before = await ReadCoordinatedStateAsync(fixture);
        using var cancellation = new CancellationTokenSource();
        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = cancellation.Token.Register(() => cancelled.TrySetResult());
        var attempt = ExecuteCoordinatedValuesAsync(fixture, ContractRequested, PublicationValues((ApprovalKey, "true")),
            async (services, token) =>
            {
                await Assert.That(token).IsEqualTo(cancellation.Token);
                var result = await ApplyCoordinatedAsync(fixture, services, ContractRequested,
                    [new(ApprovalKey, PublicationPolicyMutationKind.Set, "true", ContractRequested, null)], token);
                await Assert.That(result.Success).IsTrue();
                await Assert.That(result.DeferredNotifications).HasSingleItem();
                var owner = services.GetRequiredService<ExploreDbContext>();
                await Assert.That(await owner.TenantSettingOverrides.AsNoTracking()
                    .Where(row => row.TenantId == ContractRequested && row.SettingKey == ApprovalKey)
                    .Select(row => row.Value).SingleAsync(token)).IsEqualTo("true");
                applied.TrySetResult();
                await released.Task.WaitAsync(TimeSpan.FromSeconds(30));
                return result;
            }, cancellation.Token);
        try
        {
            await Task.WhenAny(applied.Task, attempt).WaitAsync(TimeSpan.FromSeconds(30));
            if (!applied.Task.IsCompleted)
                await attempt;
            await applied.Task.WaitAsync(TimeSpan.FromSeconds(30));
            cancellation.Cancel();
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally { released.TrySetResult(); }
        var caught = await Assert.ThrowsAsync<OperationCanceledException>(() => attempt);
        await Assert.That(caught!.CancellationToken).IsEqualTo(cancellation.Token);
        await AssertCoordinatedStateAsync(fixture, before);
    }

    [Test]
    public async Task CoordinatedTenantLockProofCannotBecomePublicationLockAuthority()
    {
        await using var fixture = await PublicationFixture.CreateAsync();
        await SeedCoordinatedContractAsync(fixture);
        var before = await ReadCoordinatedStateAsync(fixture);
        await fixture.ExecuteContractAsync(new(ContractRequested),
            (services, token) => services.GetRequiredService<IHumanSettingsMutationPreparation>()
                .PrepareAsync(new Explore.Application.Features.InstanceAdmin.Requests.Commands.LockInstanceTenantSettingCommand(
                    ContractRequested, ApprovalKey), token),
            async (services, token) =>
            {
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ApplyCoordinatedAsync(fixture, services,
                    ContractRequested, [new(ApprovalKey, PublicationPolicyMutationKind.Set, "false", ContractRequested, true)], token));
                return true;
            });
        await AssertCoordinatedStateAsync(fixture, before);
    }

    private static string ContractProviderKey => GovernanceSettingKeys.Reporting.EnableTenantOspreyProvider;

    private static Task<T> ExecuteCoordinatedValuesAsync<T>(PublicationFixture fixture, Guid? tenant,
        ImmutableDictionary<string, string> values, Func<IServiceProvider, CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default) =>
        fixture.ExecuteContractAsync(tenant is { } target ? new(target) : PolicyMutationScope.Instance,
            (services, token) => services.GetRequiredService<IHumanSettingsMutationPreparation>().PrepareAsync(
                new UpdateSettingBatchCommand
                {
                    Category = "Events",
                    Scope = tenant.HasValue ? SettingScope.Tenant
                    : SettingScope.Instance,
                    Values = values
                }, values, token), action, cancellationToken);

    private static Task<PublicationPolicyMutationResult> ApplyCoordinatedAsync(PublicationFixture fixture,
        IServiceProvider services, Guid? tenant, ImmutableArray<PublicationPolicySettingMutation> mutations,
        CancellationToken token, PublicationPolicyLockedSystemBehavior behavior = PublicationPolicyLockedSystemBehavior.Reject)
    {
        var boundary = services.GetRequiredService<IPublicationPolicyMutationBoundary>();
        DateTime occurred = fixture.Identity.Clock.GetUtcNow().UtcDateTime;
        return tenant is { } target
            ? boundary.ApplyTenantAsync(new(target, fixture.Actor, occurred, mutations, behavior), token)
            : boundary.ApplyInstanceAsync(new(fixture.Actor, occurred, mutations), token);
    }

    private static async Task SeedCoordinatedContractAsync(PublicationFixture fixture)
    {
        await fixture.SeedSystemAsync(IntakeKey, " true \n");
        await using (var seed = fixture.Identity.Native.CreateDatabase())
        {
            foreach (var (id, slug) in new[] { (ContractEarlier, "contract-earlier"),
                (ContractRequested, "contract-requested"), (ContractLater, "contract-later") })
                seed.Tenants.Add(new Tenant
                {
                    Id = id,
                    Slug = slug,
                    FullName = slug,
                    TenantStatusId = (int)TenantStatusEnum.Active,
                    TenantStatus = null!
                });
            var member = new TenantUser
            {
                Id = Guid.CreateVersion7(),
                TenantId = ContractRequested,
                Tenant = null!,
                UserId = fixture.Actor,
                User = null!,
                StatusId = (int)TenantUserStatusEnum.Active,
                JoinedAt = fixture.Identity.Clock.GetUtcNow().UtcDateTime
            };
            seed.TenantUsers.Add(member);
            seed.TenantUserRoleGrants.Add(new TenantUserRoleGrant
            {
                Id = Guid.CreateVersion7(),
                TenantId = ContractRequested,
                Tenant = null!,
                TenantUserId = member.Id,
                TenantUser = null!,
                RoleId = (int)RoleEnum.TenantAdmin,
                Role = null!,
                RoleScopeId = (int)RoleScopeEnum.Tenant,
                GrantedAt = fixture.Identity.Clock.GetUtcNow().UtcDateTime
            });
            var provider = await seed.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == ContractProviderKey);
            if (provider is null)
                seed.SystemSettings.Add(new SystemSetting
                {
                    SettingKey = ContractProviderKey,
                    Value = ContractProviderValue,
                    ValueType = SettingValueType.String,
                    IsLocked = true
                });
            else { provider.Value = ContractProviderValue; provider.IsLocked = true; }
            await seed.SaveChangesAsync();
        }
        await fixture.SeedTenantAsync(ApprovalKey, "false", ContractEarlier);
        await fixture.SeedTenantAsync(IntakeKey, " false \n", ContractRequested);
        await fixture.SeedTenantAsync(UserSubmissionKey, "true", ContractRequested);
        await fixture.SeedTenantAsync(IntakeKey, "true", ContractLater);
        await fixture.SeedTenantAsync(ContractProviderKey, ContractProviderValue, ContractRequested);
        await fixture.SeedTenantAsync(ContractProviderKey, ContractProviderValue, ContractLater);
    }

    private static async Task AssertCoordinatedNotificationAsync(PublicationFixture fixture,
        PublicationPolicyMutationResult result, int index, Guid? tenant, string key, string? oldValue, string? newValue,
        SettingSource? source = null)
    {
        var notification = result.DeferredNotifications[index];
        await Assert.That(notification.Key).IsEqualTo(key);
        await Assert.That(notification.OldValue).IsEqualTo(oldValue);
        await Assert.That(notification.NewValue).IsEqualTo(newValue);
        await Assert.That(notification.TenantId).IsEqualTo(tenant);
        await Assert.That(notification.Scope).IsEqualTo(source
            ?? (tenant.HasValue ? SettingSource.TenantOverride : SettingSource.SystemDefault));
        await Assert.That(notification.ActorUserId).IsEqualTo(fixture.Actor);
        await Assert.That(notification.ChangedAt).IsEqualTo(fixture.Identity.Clock.GetUtcNow().UtcDateTime);
    }

    private static async Task<CoordinatedState> ReadCoordinatedStateAsync(PublicationFixture fixture)
    {
        await using var observer = fixture.Identity.Native.CreateDatabase();
        var systems = await observer.SystemSettings.AsNoTracking().OrderBy(row => row.SettingKey)
            .Select(row => new CoordinatedSystemRow(row.Id, row.SettingKey, row.Value, row.IsLocked, row.SettingValueTypeId,
                row.AllowedValues, row.Description, row.Category, row.DisplayOrder, row.CreatedAt, row.CreatedBy,
                row.UpdatedAt, row.UpdatedBy)).ToArrayAsync();
        var tenants = await observer.TenantSettingOverrides.IgnoreQueryFilters().AsNoTracking()
            .OrderBy(row => row.TenantId).ThenBy(row => row.SettingKey)
            .Select(row => new CoordinatedTenantRow(row.Id, row.TenantId, row.SettingKey, row.Value, row.IsLocked,
                row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy)).ToArrayAsync();
        return new(systems, tenants);
    }

    private static async Task AssertCoordinatedStateAsync(PublicationFixture fixture, CoordinatedState before)
    {
        var after = await ReadCoordinatedStateAsync(fixture);
        await Assert.That(after.System.SequenceEqual(before.System)).IsTrue();
        await Assert.That(after.Tenant.SequenceEqual(before.Tenant)).IsTrue();
    }

    private static async Task AssertCoordinatedProvidersAsync(PublicationFixture fixture)
    {
        var state = await ReadCoordinatedStateAsync(fixture);
        await Assert.That(state.System.Single(row => row.Key == ContractProviderKey).Value).IsEqualTo(ContractProviderValue);
        await Assert.That(state.Tenant.Where(row => row.Key == ContractProviderKey).Select(row => row.Value))
            .IsEquivalentTo([ContractProviderValue, ContractProviderValue]);
    }

    private sealed record CoordinatedState(CoordinatedSystemRow[] System, CoordinatedTenantRow[] Tenant);
    private sealed record CoordinatedSystemRow(Guid Id, string Key, string Value, bool Locked, int ValueType,
        string? AllowedValues, string? Description, string? Category, int DisplayOrder, DateTime CreatedAt,
        Guid? CreatedBy, DateTime? UpdatedAt, Guid? UpdatedBy);
    private sealed record CoordinatedTenantRow(Guid Id, Guid Tenant, string Key, string Value, bool Locked,
        DateTime CreatedAt, Guid? CreatedBy, DateTime? UpdatedAt, Guid? UpdatedBy);
    private sealed class CoordinatedOwnerFailure : Exception;
}
