
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EmailDispatch;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel("SqliteEmailDispatchEligibility")]
public sealed class EmailDeliveryDisableImpactTests
{
    [Test]
    public async Task InstanceDisable_IncludesInheritedAndMisconfiguredScopesButPreservesIndependentEnablement()
    {
        await using var fixture = await SmtpAuthenticatedFixture.CreateAsync();
        {
            await using var context = fixture.CreateIndependentContext();
            await UnlockDelegationAsync(context);
            await SetEmailSettingAsync(context, GovernanceSettingKeys.Email.SmtpHost, "\"smtp.impact.test\"");
            await SetTenantSettingsAsync(context, fixture.TenantId,
                (GovernanceSettingKeys.Email.DeliveryEnabled, "false"));
            Guid inherited = await AddTenantAsync(context);
            Guid sharedExplicit = await AddTenantAsync(context);
            Guid independent = await AddTenantAsync(context);
            Guid inheritedByo = await AddTenantAsync(context);
            Guid misconfigured = await AddTenantAsync(context);
            Guid disabled = await AddTenantAsync(context);
            await SetTenantSettingsAsync(context, sharedExplicit,
                (GovernanceSettingKeys.Email.DeliveryEnabled, "true"));
            await SetTenantSettingsAsync(context, independent,
                (GovernanceSettingKeys.Email.DeliveryEnabled, "true"),
                (GovernanceSettingKeys.Email.SmtpHost, "\"smtp.independent.test\""),
                (GovernanceSettingKeys.Email.FromAddress, "\"events@independent.test\""));
            await SetTenantSettingsAsync(context, inheritedByo,
                (GovernanceSettingKeys.Email.SmtpHost, "\"smtp.inherited.test\""),
                (GovernanceSettingKeys.Email.FromAddress, "\"events@inherited.test\""));
            await SetTenantSettingsAsync(context, misconfigured,
                (GovernanceSettingKeys.Email.SmtpHost, "\"smtp.incomplete.test\""));
            await SetTenantSettingsAsync(context, disabled,
                (GovernanceSettingKeys.Email.DeliveryEnabled, "false"));
            context.ChangeTracker.Clear();

            var processorBefore = await context.EmailDispatchProcessorStates.AsNoTracking().SingleAsync();
            var controlsBefore = await context.EmailDispatchTenantControls
                .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDeliveryPolicyReconciliation)
                .AsNoTracking().ToDictionaryAsync(row => row.TenantId, row => row.DeliveryPolicyRevision);
            var overridesBefore = await context.TenantSettingOverrides
                .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDeliveryPolicyReconciliation)
                .AsNoTracking().OrderBy(row => row.Id)
                .Select(row => new { row.Id, row.Value, row.IsLocked }).ToListAsync();
            await fixture.ExecuteAsync(tenantOwner: false, async services =>
            {
                var snapshot = await ReadImpactAsync(services, null);

                await Assert.That(snapshot).IsNotNull();
                await Assert.That(snapshot!.CanDisable).IsTrue();
                await Assert.That(snapshot.Revision).IsEqualTo(processorBefore.DeliveryPolicyRevision);
                EmailDeliveryAffectedScope[] expected = [new(TenantId: null, Revision: processorBefore.DeliveryPolicyRevision),
                    .. new[] { inherited, sharedExplicit, inheritedByo, misconfigured }.Order()
                        .Select(id => new EmailDeliveryAffectedScope(TenantId: id, Revision: controlsBefore.GetValueOrDefault(id)))];
                await Assert.That(snapshot.AffectedScopes.SequenceEqual(expected)).IsTrue();
                await Assert.That(services.GetRequiredService<ExploreDbContext>().ChangeTracker.Entries().Any()).IsFalse();
            });

            await using var observer = fixture.CreateIndependentContext();
            await Assert.That((await observer.SystemSettings.AsNoTracking().SingleAsync(
                setting => setting.SettingKey == GovernanceSettingKeys.Email.DeliveryEnabled)).Value).IsEqualTo("true");
            await Assert.That((await observer.EmailDispatchProcessorStates.AsNoTracking().SingleAsync()).DeliveryPolicyRevision)
                .IsEqualTo(processorBefore.DeliveryPolicyRevision);
            var controlsAfter = await observer.EmailDispatchTenantControls
                .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDeliveryPolicyReconciliation)
                .AsNoTracking().ToDictionaryAsync(row => row.TenantId, row => row.DeliveryPolicyRevision);
            await Assert.That(controlsAfter.OrderBy(row => row.Key).SequenceEqual(controlsBefore.OrderBy(row => row.Key))).IsTrue();
            var overridesAfter = await observer.TenantSettingOverrides
                .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDeliveryPolicyReconciliation)
                .AsNoTracking().OrderBy(row => row.Id)
                .Select(row => new { row.Id, row.Value, row.IsLocked }).ToListAsync();
            await Assert.That(overridesAfter.SequenceEqual(overridesBefore)).IsTrue();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task TenantDisable_RespectsInstanceAndDelegationLocks(bool delegationLock)
    {
        await using var fixture = await SmtpAuthenticatedFixture.CreateAsync();
        {
            await using var context = fixture.CreateIndependentContext();
            await UnlockDelegationAsync(context);
            Guid tenantId = await AddTenantAsync(context);
            await SetTenantSettingsAsync(context, tenantId, (GovernanceSettingKeys.Email.DeliveryEnabled, "true"));
            await ApplyEmailSettingsAsync(context,
                [new(TenantId: null,
                    Key: delegationLock ? GovernanceSettingKeys.TenantDelegation.LockSmtp : GovernanceSettingKeys.Email.DeliveryEnabled,
                    Kind: EmailDeliverySettingMutationKind.SetValue, Value: "true", IsLocked: !delegationLock)]);
            await fixture.ExecuteAsync(tenantOwner: true, async services =>
            {
                var snapshot = await ReadImpactAsync(services, tenantId);
                await Assert.That(snapshot!.IsLocked).IsTrue();
                await Assert.That(snapshot.CanDisable).IsFalse();
                await Assert.That(snapshot.AffectedScopes.IsEmpty).IsTrue();
            }, selectedTenantId: tenantId);
        }
    }

    [Test]
    public async Task TenantDisable_OnlyAffectsSelectedTenantAndUsesZeroForAbsentControl()
    {
        await using var fixture = await SmtpAuthenticatedFixture.CreateAsync();
        {
            await using var context = fixture.CreateIndependentContext();
            await UnlockDelegationAsync(context);
            Guid selected = await AddTenantAsync(context);
            await AddTenantAsync(context);
            await fixture.ExecuteAsync(tenantOwner: true, async services =>
            {
                var snapshot = await ReadImpactAsync(services, selected);
                await Assert.That(snapshot!.Revision).IsEqualTo(0L);
                await Assert.That(snapshot.CanDisable).IsTrue();
                await Assert.That(snapshot.AffectedScopes.SequenceEqual(
                    [new EmailDeliveryAffectedScope(TenantId: selected, Revision: 0)])).IsTrue();
                var reader = services.GetRequiredService<IEmailDeliveryDisableImpactReader>();
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    ExecutePreviewAsync(services, selected, _ => reader.ReadAsync(Guid.CreateVersion7())));
                await Assert.That(async () => await reader.ReadAsync(Guid.Empty)).Throws<ArgumentException>();
            }, selectedTenantId: selected);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ScopeOwnLock_OnlyLocksDescendantsAndStillAllowsDisable(bool tenantScope)
    {
        await using var fixture = await SmtpAuthenticatedFixture.CreateAsync();
        {
            await using var context = fixture.CreateIndependentContext();
            await UnlockDelegationAsync(context);
            Guid tenantId = await AddTenantAsync(context);
            if (tenantScope)
                await ApplyEmailSettingsAsync(context,
                    [new(TenantId: tenantId, Key: GovernanceSettingKeys.Email.DeliveryEnabled,
                        Kind: EmailDeliverySettingMutationKind.SetValue, Value: "true", IsLocked: true)]);
            else
                await ApplyEmailSettingsAsync(context,
                    [new(TenantId: null, Key: GovernanceSettingKeys.Email.DeliveryEnabled,
                        Kind: EmailDeliverySettingMutationKind.SetValue, Value: "true", IsLocked: true)]);
            await fixture.ExecuteAsync(tenantOwner: tenantScope, async services =>
            {
                var snapshot = await ReadImpactAsync(services, tenantScope ? tenantId : null);
                await Assert.That(snapshot!.IsLocked).IsFalse();
                await Assert.That(snapshot.CanDisable).IsTrue();
                await Assert.That(snapshot.AffectedScopes.Any(scope => scope.TenantId == tenantId)).IsTrue();
            }, selectedTenantId: tenantScope ? tenantId : null);
        }
    }

    [Test]
    public async Task InstancePreview_ReflectsCommittedMembershipChangesWithoutAnInstanceRevisionChange()
    {
        await using var fixture = await SmtpAuthenticatedFixture.CreateAsync();
        {
            EmailDeliveryDisableImpactSnapshot before;
            before = (await ReadImpactAsync(fixture, null))!;
            await using var writer = fixture.CreateIndependentContext();
            Guid createdTenant = await AddTenantAsync(writer);
            {
                var afterCreation = (await ReadImpactAsync(fixture, null))!;
                await Assert.That(afterCreation.Revision).IsEqualTo(before.Revision);
                await Assert.That(afterCreation.AffectedScopes.Length).IsEqualTo(before.AffectedScopes.Length + 1);
                await Assert.That(afterCreation.AffectedScopes.Contains(
                    new EmailDeliveryAffectedScope(TenantId: createdTenant, Revision: 0))).IsTrue();
            }

            await writer.Tenants.Where(tenant => tenant.Id == createdTenant).ExecuteDeleteAsync();
            {
                var afterRemoval = (await ReadImpactAsync(fixture, null))!;
                await Assert.That(afterRemoval.Revision).IsEqualTo(before.Revision);
                await Assert.That(afterRemoval.AffectedScopes.SequenceEqual(before.AffectedScopes)).IsTrue();
            }
        }
    }

    [Test]
    public async Task Preview_RequiresOriginalAcquiredProofNotOnlyCallerTransaction()
    {
        await using var fixture = await SmtpAuthenticatedFixture.CreateAsync();
        await fixture.ExecuteAsync(tenantOwner: false, async services =>
        {
            var reader = services.GetRequiredService<IEmailDeliveryDisableImpactReader>();
            await Assert.That(async () => await reader.ReadAsync(null)).Throws<InvalidOperationException>();
            await using var transaction = await services.GetRequiredService<ExploreDbContext>()
                .Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.ReadAsync(null));
        });
    }

    private static async Task<Guid> AddTenantAsync(ExploreDbContext context)
    {
        Guid id = Guid.CreateVersion7();
        context.Tenants.Add(new Tenant
        {
            Id = id,
            FullName = "Email impact tenant",
            Slug = $"impact-{id:N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        return id;
    }

    private static Task SetTenantSettingsAsync(ExploreDbContext context, Guid tenantId,
        params (string Key, string Value)[] settings) => settings.Length == 1
            ? SetEmailSettingAsync(context, settings[0].Key, settings[0].Value, tenantId: tenantId)
            : ApplyEmailSettingsAsync(context,
                [.. settings.Select(setting => new EmailDeliverySettingMutation(TenantId: tenantId,
                    Key: setting.Key, Kind: EmailDeliverySettingMutationKind.SetValue, Value: setting.Value))]);

    private static Task UnlockDelegationAsync(ExploreDbContext context) =>
        SetEmailSettingAsync(context, GovernanceSettingKeys.TenantDelegation.LockSmtp, "false");

    private static async Task ApplyEmailSettingsAsync(ExploreDbContext context,
        System.Collections.Immutable.ImmutableArray<EmailDeliverySettingMutation> effects)
    {
        var unit = new EfCoreUnitOfWork(context);
        var writer = new InternalEmailDeliverySettingsWriter(context,
            new RelationalSettingMutationLock(context, unit), unit);
        var result = await writer.ApplyInternalAsync(effects, null);
        await Assert.That(result.Status is EmailDeliverySettingsWriteStatus.Applied
            or EmailDeliverySettingsWriteStatus.NoChange).IsTrue();
    }

    private static async Task SetEmailSettingAsync(ExploreDbContext context, string key, string value,
        Guid? tenantId = null)
    {
        if (key != GovernanceSettingKeys.Email.DeliveryEnabled || value != "false")
        {
            await ApplyEmailSettingsAsync(context, [new(tenantId, key, EmailDeliverySettingMutationKind.SetValue, value)]);
            return;
        }
        var unit = new EfCoreUnitOfWork(context);
        var writer = new InternalEmailDeliverySettingsWriter(context,
            new RelationalSettingMutationLock(context, unit), unit);
        var result = await writer.DisableInternalAsync(tenantId, null);
        await Assert.That(result.Status is EmailDeliverySettingsWriteStatus.Applied
            or EmailDeliverySettingsWriteStatus.NoChange).IsTrue();
    }

    private static async Task<EmailDeliveryDisableImpactSnapshot?> ExecutePreviewAsync(
        IServiceProvider services, Guid? tenantId,
        Func<CancellationToken, Task<EmailDeliveryDisableImpactSnapshot?>> observe)
    {
        var issuer = services.GetRequiredService<IHumanEmailMutationPreparation>();
        var proof = await issuer.PreparePreviewAsync(tenantId);
        return await issuer.ExecutePreparedAsync(proof, observe);
    }

    private static Task<EmailDeliveryDisableImpactSnapshot?> ReadImpactAsync(IServiceProvider services, Guid? tenantId) =>
        ExecutePreviewAsync(services, tenantId,
            token => services.GetRequiredService<IEmailDeliveryDisableImpactReader>().ReadAsync(tenantId, token));

    private static async Task<EmailDeliveryDisableImpactSnapshot?> ReadImpactAsync(
        SmtpAuthenticatedFixture fixture, Guid? tenantId)
    {
        EmailDeliveryDisableImpactSnapshot? result = null;
        await fixture.ExecuteAsync(tenantOwner: tenantId.HasValue, async services =>
            result = await ReadImpactAsync(services, tenantId), selectedTenantId: tenantId);
        return result;
    }
}
