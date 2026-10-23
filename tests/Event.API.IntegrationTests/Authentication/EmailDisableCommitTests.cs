using Explore.Application.DTOs.EmailDispatch;
using Explore.Application.Features.EmailDispatch.Handlers.Commands;
using Explore.Application.Features.EmailDispatch.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using static Event.Api.IntegrationTests.Authentication.EmailDisableCommitAuthenticatedFixture;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
public sealed class EmailDeliveryDisableCommitTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ConfirmedDisable_CommitsOnlySelectedIntentAndRetainsConfiguration(bool tenantScope)
    {
        await using var fixture = await EmailDisableCommitAuthenticatedFixture.CreateAsync(tenantScope);
        Guid? target = tenantScope ? fixture.TenantId : null;
        await using var context = fixture.CreateIndependentContext();
        var before = await ReadStateAsync(context, target);
        var otherBefore = await ReadStateAsync(context, tenantScope ? null : fixture.TenantId);
        var inheritedBefore = await ReadInheritedControlAsync(context, fixture.InheritedTenantId);
        var retainedBefore = await ReadRetainedMetadataAsync(context);
        var preview = await PreviewAsync(fixture, target);
        await Assert.That(await ReadStateAsync(context, target)).IsEqualTo(before);
        await Assert.That(preview.AffectedScopes.Any(scope => scope.TenantId == target)).IsTrue();
        if (!tenantScope)
        {
            await Assert.That(preview.AffectedScopes.Any(scope => scope.TenantId == fixture.TenantId)).IsFalse();
            await Assert.That(preview.AffectedScopes.Any(scope => scope.TenantId == fixture.InheritedTenantId)).IsTrue();
        }

        var response = await DisableAsync(fixture, target, Command(preview));

        await Assert.That(response.IsSuccess).IsTrue();
        await using var observer = fixture.CreateIndependentContext();
        var after = await ReadStateAsync(observer, target);
        await Assert.That(after.Value).IsEqualTo("false");
        await Assert.That(after.Revision).IsEqualTo(before.Revision + 1);
        await Assert.That(after.SuppressedThroughRevision).IsEqualTo(after.Revision);
        await Assert.That(after.SuppressedThroughUtc).IsNotNull();
        await Assert.That(await ReadStateAsync(observer, tenantScope ? null : fixture.TenantId)).IsEqualTo(otherBefore);
        var inheritedAfter = await ReadInheritedControlAsync(observer, fixture.InheritedTenantId);
        await Assert.That(inheritedAfter.DeliveryPolicyRevision).IsEqualTo(
            inheritedBefore.DeliveryPolicyRevision + (tenantScope ? 0 : 1));
        await Assert.That(inheritedAfter.OptionalSuppressedThroughRevision).IsEqualTo(
            tenantScope ? inheritedBefore.OptionalSuppressedThroughRevision : inheritedAfter.DeliveryPolicyRevision);
        await Assert.That(inheritedAfter.OptionalSuppressedThroughUtc).IsEqualTo(
            tenantScope ? inheritedBefore.OptionalSuppressedThroughUtc : after.SuppressedThroughUtc);
        await Assert.That((await ReadRetainedMetadataAsync(observer)).SequenceEqual(retainedBefore)).IsTrue();
        await Assert.That(fixture.Notifications.Published.Single().NewValue).IsEqualTo("false");
        await Assert.That(fixture.Notifications.Published.Single().TenantId).IsEqualTo(target);
        await Assert.That(fixture.Notifications.Published.Single().ActorUserId).IsEqualTo(fixture.ActorId);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ChangedRevisionOrAffectedMembership_RejectsOldConfirmation(bool addAffectedTenant)
    {
        await using var fixture = await EmailDisableCommitAuthenticatedFixture.CreateAsync(tenantAdministrator: false);
        var preview = await PreviewAsync(fixture, target: null);
        await using (var writer = fixture.CreateIndependentContext())
        {
            if (addAffectedTenant)
                await AddTenantAsync(writer);
            else
                await ApplyInternalAsync(writer,
                    [new(null, GovernanceSettingKeys.Email.FromName,
                        Explore.Application.Contracts.Persistence.EmailDeliverySettingMutationKind.SetValue, "\"New sender name\"")]);
        }
        var fresh = await PreviewAsync(fixture, target: null);
        if (addAffectedTenant)
        {
            await Assert.That(fresh.ExpectedRevision).IsEqualTo(preview.ExpectedRevision);
            await Assert.That(fresh.AffectedScopes.Length).IsEqualTo(preview.AffectedScopes.Length + 1);
        }
        else
            await Assert.That(fresh.ExpectedRevision).IsGreaterThan(preview.ExpectedRevision);
        await using var context = fixture.CreateIndependentContext();
        var before = await ReadStateAsync(context, target: null);

        var response = await DisableAsync(fixture, selectedTenant: null, Command(preview));

        await Assert.That(response.FailureCode).IsEqualTo(FailureCodes.ConcurrencyConflict);
        await using var observer = fixture.CreateIndependentContext();
        await Assert.That(await ReadStateAsync(observer, target: null)).IsEqualTo(before);
        await Assert.That(fixture.Notifications.Published.Count).IsEqualTo(0);
    }

    public enum InvalidConfirmation { Acknowledgement, DifferentActor, RevokedTenantRole, DifferentCurrentTenant }

    [Test]
    [Arguments(InvalidConfirmation.Acknowledgement)]
    [Arguments(InvalidConfirmation.DifferentActor)]
    [Arguments(InvalidConfirmation.RevokedTenantRole)]
    [Arguments(InvalidConfirmation.DifferentCurrentTenant)]
    public async Task Confirmation_DoesNotReplaceExactAcknowledgementOrFreshAuthority(InvalidConfirmation invalid)
    {
        bool tenantScope = invalid is InvalidConfirmation.RevokedTenantRole or InvalidConfirmation.DifferentCurrentTenant;
        await using var fixture = await EmailDisableCommitAuthenticatedFixture.CreateAsync(
            tenantScope, secondAdministrator: invalid == InvalidConfirmation.DifferentActor);
        Guid? target = tenantScope ? fixture.TenantId : null;
        var preview = await PreviewAsync(fixture, target);
        await using var context = fixture.CreateIndependentContext();
        var before = await ReadStateAsync(context, target);
        var request = Command(preview);
        BaseCommandResponse<Guid> response;
        if (invalid == InvalidConfirmation.DifferentActor)
        {
            response = await DisableAsync(fixture, target, request, differentActor: true);
            await Assert.That(response.FailureCode).IsEqualTo(FailureCodes.ConcurrencyConflict);
            await Assert.That(fixture.Notifications.Published.Count).IsEqualTo(0);
        }
        else if (invalid == InvalidConfirmation.DifferentCurrentTenant)
        {
            response = await DisableAsync(fixture, fixture.InheritedTenantId, request);
            await Assert.That(response.FailureCode).IsEqualTo(FailureCodes.AdminRequired);
            var deniedPreview = await PreviewResponseAsync(fixture, fixture.InheritedTenantId, target);
            await Assert.That(deniedPreview.FailureCode).IsEqualTo(FailureCodes.AdminRequired);
            await Assert.That(fixture.Notifications.Published).IsEmpty();
        }
        else
        {
            if (invalid == InvalidConfirmation.Acknowledgement)
                request = request with { Acknowledgement = DisableEmailDeliveryCommand.RequiredAcknowledgement + " " };
            else if (invalid == InvalidConfirmation.RevokedTenantRole)
            {
                await using var writer = fixture.CreateIndependentContext();
                var grant = await writer.TenantUserRoleGrants
                    .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                    .SingleAsync(row => row.TenantId == fixture.TenantId && row.RoleId == (int)RoleEnum.TenantAdmin);
                grant.RevokedAt = DateTime.UtcNow;
                await writer.SaveChangesAsync();
            }
            response = await DisableAsync(fixture, target, request);
            if (tenantScope)
            {
                await Assert.That(response.FailureCode).IsEqualTo(FailureCodes.AdminRequired);
                var deniedPreview = await PreviewResponseAsync(fixture, target, target);
                await Assert.That(deniedPreview.FailureCode).IsEqualTo(FailureCodes.AdminRequired);
            }
            else
                await Assert.That(response.Errors).IsNotNull();
        }
        await Assert.That(response.IsSuccess).IsFalse();
        await using var observer = fixture.CreateIndependentContext();
        await Assert.That(await ReadStateAsync(observer, target)).IsEqualTo(before);
        await Assert.That(fixture.Notifications.Published.Count).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ConfirmedToken_CannotDisableAgainAfterReenable(bool tenantScope)
    {
        await using var fixture = await EmailDisableCommitAuthenticatedFixture.CreateAsync(tenantScope);
        Guid? target = tenantScope ? fixture.TenantId : null;
        var preview = await PreviewAsync(fixture, target);
        await Assert.That((await DisableAsync(fixture, target, Command(preview))).IsSuccess).IsTrue();
        await using (var writer = fixture.CreateIndependentContext())
            await ApplyInternalAsync(writer,
                [new(target, GovernanceSettingKeys.Email.DeliveryEnabled,
                    Explore.Application.Contracts.Persistence.EmailDeliverySettingMutationKind.SetValue, "true")]);
        await using var context = fixture.CreateIndependentContext();
        var reenabled = await ReadStateAsync(context, target);

        var replay = await DisableAsync(fixture, target, Command(preview));

        await Assert.That(replay.FailureCode).IsEqualTo(FailureCodes.ConcurrencyConflict);
        await using var observer = fixture.CreateIndependentContext();
        await Assert.That(reenabled.Value).IsEqualTo("true");
        await Assert.That(await ReadStateAsync(observer, target)).IsEqualTo(reenabled);
        await Assert.That(fixture.Notifications.Published.Count).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RevisionWriteFailure_RollsBackConfirmedSettingAndSuppression(bool tenantScope)
    {
        await using var fixture = await EmailDisableCommitAuthenticatedFixture.CreateAsync(tenantScope);
        Guid? target = tenantScope ? fixture.TenantId : null;
        var preview = await PreviewAsync(fixture, target);
        await using var context = fixture.CreateIndependentContext();
        var before = await ReadStateAsync(context, target);
        var otherBefore = await ReadStateAsync(context, tenantScope ? null : fixture.TenantId);
        var inheritedBefore = await ReadInheritedControlAsync(context, fixture.InheritedTenantId);
        var retainedBefore = await ReadRetainedMetadataAsync(context);
        var entity = context.Model.FindEntityType(tenantScope ? typeof(EmailDispatchTenantControl) : typeof(EmailDispatchProcessorState))!;
        string table = context.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!);
        // Keep the original provider AFTER UPDATE failure: the setting write precedes this
        // revision update, so only a real transaction rollback can restore the independent view.
        await context.Database.ExecuteSqlRawAsync(
            $"CREATE TRIGGER reject_confirmed_disable AFTER UPDATE ON {table} BEGIN SELECT RAISE(ABORT, 'disable_revision_rejected'); END");

        bool rejected = false;
        try { await DisableAsync(fixture, target, Command(preview)); }
        catch (Exception exception) when (exception is SqliteException or DbUpdateException)
        {
            await Assert.That(exception.ToString().Contains("disable_revision_rejected", StringComparison.Ordinal)).IsTrue();
            rejected = true;
        }

        await Assert.That(rejected).IsTrue();
        await using var observer = fixture.CreateIndependentContext();
        await Assert.That(await ReadStateAsync(observer, target)).IsEqualTo(before);
        await Assert.That(await ReadStateAsync(observer, tenantScope ? null : fixture.TenantId)).IsEqualTo(otherBefore);
        var inheritedAfter = await ReadInheritedControlAsync(observer, fixture.InheritedTenantId);
        await Assert.That(inheritedAfter.DeliveryPolicyRevision).IsEqualTo(inheritedBefore.DeliveryPolicyRevision);
        await Assert.That(inheritedAfter.OptionalSuppressedThroughRevision).IsEqualTo(inheritedBefore.OptionalSuppressedThroughRevision);
        await Assert.That(inheritedAfter.OptionalSuppressedThroughUtc).IsEqualTo(inheritedBefore.OptionalSuppressedThroughUtc);
        await Assert.That((await ReadRetainedMetadataAsync(observer)).SequenceEqual(retainedBefore)).IsTrue();
        await Assert.That(fixture.Notifications.Published.Count).IsEqualTo(0);
    }

    [Test]
    public async Task NewlyLockedTenantDelegation_ConflictsWithPreviouslyValidDisablePreview()
    {
        await using var fixture = await EmailDisableCommitAuthenticatedFixture.CreateAsync(tenantAdministrator: true);
        var preview = await PreviewAsync(fixture, fixture.TenantId);
        await using (var writer = fixture.CreateIndependentContext())
            await ApplyInternalAsync(writer,
                [new(null, GovernanceSettingKeys.TenantDelegation.LockSmtp,
                    Explore.Application.Contracts.Persistence.EmailDeliverySettingMutationKind.SetValue, "true")]);
        await using var context = fixture.CreateIndependentContext();
        var before = await ReadStateAsync(context, fixture.TenantId);
        var metadata = await ReadRetainedMetadataAsync(context);

        var result = await DisableAsync(fixture, fixture.TenantId, Command(preview));

        await Assert.That(result.FailureCode).IsEqualTo(FailureCodes.ConcurrencyConflict);
        await using var observer = fixture.CreateIndependentContext();
        await Assert.That(await ReadStateAsync(observer, fixture.TenantId)).IsEqualTo(before);
        await Assert.That((await ReadRetainedMetadataAsync(observer)).SequenceEqual(metadata)).IsTrue();
        await Assert.That(fixture.Notifications.Published).IsEmpty();
    }

    private static async Task<EmailDeliveryDisablePreviewDto> PreviewAsync(
        EmailDisableCommitAuthenticatedFixture fixture, Guid? target)
    {
        var result = await PreviewResponseAsync(fixture, target, target);
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Id!.CanDisable).IsTrue();
        await Assert.That(result.Id.ConfirmationToken).IsNotNull();
        return result.Id;
    }

    // Exercise the concrete handler inside the original selected owner. The dispatch decorator
    // would select from the request again and mask the different-current-tenant handler control.
    // Every constructor port, including preparation/finalizer dependencies, remains production DI.
    private static Task<BaseCommandResponse<EmailDeliveryDisablePreviewDto>> PreviewResponseAsync(
        EmailDisableCommitAuthenticatedFixture fixture, Guid? selectedTenant, Guid? target) =>
        fixture.ExecuteAsync(selectedTenant, services =>
            ActivatorUtilities.CreateInstance<PreviewEmailDeliveryDisableCommandHandler>(services)
                .ExecuteAsync(new PreviewEmailDeliveryDisableCommand(TenantId: target), CancellationToken.None));

    private static Task<BaseCommandResponse<Guid>> DisableAsync(
        EmailDisableCommitAuthenticatedFixture fixture, Guid? selectedTenant, DisableEmailDeliveryCommand command,
        bool differentActor = false) =>
        fixture.ExecuteAsync(selectedTenant, services =>
            ActivatorUtilities.CreateInstance<DisableEmailDeliveryCommandHandler>(services)
                .ExecuteAsync(command, CancellationToken.None), differentActor);

    private static DisableEmailDeliveryCommand Command(EmailDeliveryDisablePreviewDto preview) =>
        new(TenantId: preview.TenantId, ExpectedRevision: preview.ExpectedRevision,
            Acknowledgement: DisableEmailDeliveryCommand.RequiredAcknowledgement, ConfirmationToken: preview.ConfirmationToken);

    private static Task<EmailDispatchTenantControl> ReadInheritedControlAsync(ExploreDbContext context, Guid tenantId) =>
        context.EmailDispatchTenantControls
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate).AsNoTracking()
            .SingleAsync(row => row.TenantId == tenantId);

    private static async Task<PolicyState> ReadStateAsync(ExploreDbContext context, Guid? target)
    {
        if (target is Guid tenantId)
        {
            var setting = await context.TenantSettingOverrides
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate).AsNoTracking()
                .SingleAsync(row => row.TenantId == tenantId && row.SettingKey == GovernanceSettingKeys.Email.DeliveryEnabled);
            var control = await context.EmailDispatchTenantControls
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate).AsNoTracking()
                .SingleAsync(row => row.TenantId == tenantId);
            return new(setting.Value, control.DeliveryPolicyRevision, control.OptionalSuppressedThroughRevision, control.OptionalSuppressedThroughUtc);
        }
        var globalSetting = await context.SystemSettings.AsNoTracking().SingleAsync(row => row.SettingKey == GovernanceSettingKeys.Email.DeliveryEnabled);
        var processor = await context.EmailDispatchProcessorStates.AsNoTracking().SingleAsync();
        return new(globalSetting.Value, processor.DeliveryPolicyRevision, processor.OptionalSuppressedThroughRevision, processor.OptionalSuppressedThroughUtc);
    }

    private static async Task<string[]> ReadRetainedMetadataAsync(ExploreDbContext context)
    {
        // Compare detached scalar projections, excluding only the intended value and policy/audit mutation fields.
        var system = await context.SystemSettings.AsNoTracking().OrderBy(row => row.SettingKey)
            .Select(row => new
            {
                row.Id,
                row.SettingKey,
                Value = row.SettingKey == GovernanceSettingKeys.Email.DeliveryEnabled ? null : row.Value,
                row.IsLocked,
                row.CreatedAt,
                row.CreatedBy,
                row.Category,
                row.Description,
                row.DisplayOrder,
                row.AllowedValues,
                row.SettingValueTypeId
            }).ToArrayAsync();
        var tenants = await context.TenantSettingOverrides
            .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDeliveryPolicyReconciliation).AsNoTracking().OrderBy(row => row.Id)
            .Select(row => new
            {
                row.Id,
                row.TenantId,
                row.SettingKey,
                Value = row.SettingKey == GovernanceSettingKeys.Email.DeliveryEnabled ? null : row.Value,
                row.IsLocked,
                row.CreatedAt,
                row.CreatedBy
            }).ToArrayAsync();
        var processor = await context.EmailDispatchProcessorStates.AsNoTracking().OrderBy(row => row.Id)
            .Select(row => new
            {
                row.Id,
                row.IsPaused,
                row.PauseReason,
                row.PausedAt,
                row.PausedBy,
                row.GlobalSmtpRateLimitPerMinuteOverride,
                row.OptionalRemindersDeferred,
                row.SmtpAvailableTokens,
                row.SmtpRefillAt
            }).ToArrayAsync();
        var controls = await context.EmailDispatchTenantControls
            .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDeliveryPolicyReconciliation).AsNoTracking().OrderBy(row => row.Id)
            .Select(row => new { row.Id, row.TenantId, row.IsPaused, row.PauseReason, row.PausedAt, row.PausedBy, row.SmtpAvailableTokens, row.SmtpRefillAt }).ToArrayAsync();
        return [System.Text.Json.JsonSerializer.Serialize(system), System.Text.Json.JsonSerializer.Serialize(tenants),
            System.Text.Json.JsonSerializer.Serialize(processor), System.Text.Json.JsonSerializer.Serialize(controls)];
    }

    private sealed record PolicyState(string Value, long Revision, long? SuppressedThroughRevision, DateTime? SuppressedThroughUtc);
}
