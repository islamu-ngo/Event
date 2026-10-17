using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Explore.Persistence.Repositories;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Services;

/// <summary>Fixed governance effects enlisted in the setting writer's transaction.</summary>
public sealed class LocationPrivacyGovernanceStore(ExploreDbContext context,
    HumanSettingsDataBoundary humanBoundary) : ILocationPrivacyGovernanceStore, IMixedLocationPrivacyGovernanceStore
{
    private const string BypassReason = TenantFilterBypassReasons.DiscoveryDisclosureMutation;

    public DateTime? MixedOccurredAt => MixedSettingsBatchStore.Current(context)?.OccurredAt;

    public Task CompleteMixedAsync(string key, Guid? tenantId, Guid actorUserId, CancellationToken cancellationToken)
    {
        var mixed = MixedSettingsBatchStore.Current(context)
            ?? throw new UnauthorizedAccessException("Original mixed location custody is required.");
        if (mixed.Mutation.TenantId != tenantId)
            throw new UnauthorizedAccessException("Location completion cannot change the original mixed target.");
        return mixed.CompleteLocationAsync(this, key, actorUserId, cancellationToken);
    }

    public async Task<IReadOnlyList<LocationPrivacyGovernanceOverride>> ReadOverridesAsync(
        string key, Guid? tenantId, CancellationToken cancellationToken)
    {
        RequireTransaction();
        ValidateScope(key, tenantId);
        await RequireReadAsync(key, tenantId, cancellationToken);
        if (MixedSettingsBatchStore.Current(context) is { } mixed)
            return mixed.Rows<TenantSetting>().Where(row => row.SettingKey == key && (tenantId is null || row.TenantId == tenantId))
                .OrderBy(row => row.TenantId).ThenBy(row => row.Id)
                .Select(row => new LocationPrivacyGovernanceOverride(row.TenantId, row.Value)).ToArray();
        return await LocationPrivacyGovernanceProviderOperations.ReadOverridesAsync(context, key, tenantId, cancellationToken);
    }

    public async Task<IReadOnlyList<EventLocationGovernanceState>> ReadActiveProjectionsAsync(
        Guid? tenantId, CancellationToken cancellationToken)
    {
        RequireTransaction();
        await RequireReadAsync(null, tenantId, cancellationToken);
        if (tenantId.HasValue)
            RequireUuid(tenantId.Value);
        if (MixedSettingsBatchStore.Current(context) is { } mixed)
            return mixed.LocationProjections(tenantId);
        return await LocationPrivacyGovernanceProviderOperations.ReadActiveProjectionsAsync(context, tenantId, cancellationToken);
    }

    public async Task ApplyCorrectionsAsync(
        string key, Guid? tenantId, IReadOnlyList<LocationPrivacyGovernanceCorrection> corrections,
        Guid actorUserId, DateTime changedAtUtc, CancellationToken cancellationToken)
    {
        var transaction = RequireTransaction();
        var mixed = MixedSettingsBatchStore.Current(context);
        try
        {
            await LocationPrivacyGovernanceMutationService.RequireStoreCorrectionsAsync(
                key, tenantId, corrections, actorUserId, changedAtUtc, cancellationToken);
            await RequireReadAsync(key, tenantId, cancellationToken);
            ValidateScope(key, tenantId);
            RequireUuid(actorUserId);
            RequireTime(changedAtUtc);
            ArgumentNullException.ThrowIfNull(corrections);
            if (corrections.Count == 0)
                throw new InvalidOperationException("Governance correction targets cannot be empty.");
            var targets = new HashSet<Guid>();
            var audits = new HashSet<Guid>();
            var messages = new HashSet<Guid>();
            foreach (var correction in corrections)
            {
                ValidateCorrection(correction, tenantId, actorUserId, changedAtUtc);
                if (!targets.Add(correction.Before.EventLocationId)
                    || !audits.Add(correction.Audit.Id) || !messages.Add(correction.Message.Id))
                    throw new InvalidOperationException("Governance correction targets and effects must be unique.");
            }

            foreach (var batch in corrections.Chunk(500))
            {
                var affected = mixed is null
                    ? await LocationPrivacyGovernanceProviderOperations.TryApplyNativeCorrectionsAsync(
                        context, key, tenantId, batch, actorUserId, changedAtUtc, cancellationToken)
                    : null;
                if (affected is not null)
                {
                    if (affected.Count != batch.Length)
                        throw new DbUpdateConcurrencyException("Governance correction did not return every target.");
                    context.DisclosureMutations.Enlist(affected);
                }
                else
                {
                    Guid[] ids = batch.Select(item => item.Before.EventLocationId).ToArray();
                    var facts = mixed is not null
                        ? mixed.LocationProjections(tenantId).Where(item => ids.Contains(item.EventLocationId))
                            .ToDictionary(item => item.EventLocationId)
                        : await LocationPrivacyGovernanceProviderOperations.PortableFacts(context, tenantId, ids)
                            .ToDictionaryAsync(item => item.EventLocationId, cancellationToken);
                    var entities = mixed is not null
                        ? await mixed.LoadLocationRowsAsync(ids, cancellationToken)
                        : await context.Set<EventLocation>().IgnoreAllFilters(BypassReason)
                            .Where(item => ids.Contains(item.Id) && !item.IsDeleted).ToDictionaryAsync(item => item.Id, cancellationToken);
                    foreach (var correction in batch)
                    {
                        var before = correction.Before;
                        if (!facts.TryGetValue(before.EventLocationId, out var stored) || stored != before
                            || !entities.TryGetValue(before.EventLocationId, out var entity)
                            || entity.TenantId != before.TenantId || entity.EventId != before.EventId
                            || entity.PolicyVersion != before.PolicyVersion || entity.ConcurrencyStamp != before.ConcurrencyStamp
                            || context.Entry(entity).State != EntityState.Unchanged)
                            throw new DbUpdateConcurrencyException("Governance correction target is missing, changed, or stale.");
                        entity.ApplyGovernanceTightening(true, actorUserId, changedAtUtc);
                        context.Add(correction.Audit);
                        context.Add(correction.Message);
                        context.Add(LocationPrivacyCorrectionReceipt.ForGovernance(
                            correction.Message.Id, before.EventId, correction.Audit, changedAtUtc));
                    }
                    if (mixed is not null)
                        await mixed.WriteLocationCorrectionsAsync(this, key, corrections, actorUserId, changedAtUtc, cancellationToken);
                    else
                        await context.SaveChangesAsync(cancellationToken);
                }
            }
        }
        catch
        {
            // A caller-owned transaction must not commit an earlier batch or setting
            // write after rejecting a later target, including client-side rejection.
            if (mixed is null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task RequireReadAsync(string? key, Guid? tenantId, CancellationToken token)
    {
        var effect = await LocationPrivacyGovernanceMutationService.RequireStoreReadAsync(key, tenantId, token);
        await humanBoundary.RequireAsync(context, effect.Scope, effect.TenantId, effect.UserId,
            effect.Key, effect.Kind, effect.Value, effect.IsLocked, cancellationToken: token);
    }

    private void ValidateCorrection(
        LocationPrivacyGovernanceCorrection correction, Guid? tenantId, Guid actor, DateTime changed)
    {
        ArgumentNullException.ThrowIfNull(correction);
        var before = correction.Before;
        var audit = correction.Audit;
        var message = correction.Message;
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(message);
        audit.BindCorrection(message.Id);
        foreach (Guid id in new[] { before.TenantId, before.EventId, before.EventLocationId,
                     before.ConcurrencyStamp, audit.Id, message.Id })
            RequireUuid(id);
        RequireTime(before.CreatedAt);
        if (before.RevealFullDetailsFromUtc.HasValue)
            RequireTime(before.RevealFullDetailsFromUtc.Value);
        if (before.IsDeleted || tenantId.HasValue && before.TenantId != tenantId
            || before.PolicyVersion < 1 || before.PolicyVersion == int.MaxValue
            || (before.Fields & ~EventLocationDisclosureFields.All) != 0 || !Enum.IsDefined(before.Audience)
            || audit.TenantId != before.TenantId || audit.EventLocationId != before.EventLocationId
            || audit.ActorUserId != actor || audit.OccurredAtUtc != changed
            || audit.OccurredAtUtc.Kind != DateTimeKind.Utc
            || audit.Reason != EventLocationDisclosureAuditReasonEnum.GovernanceTightening
            || audit.PreviousFields != before.Fields || audit.NewFields != before.Fields
            || audit.PreviousAudienceId != (int)before.Audience || audit.NewAudienceId != (int)before.Audience
            || audit.PreviousRevealFullDetailsFromUtc != before.RevealFullDetailsFromUtc
            || audit.NewRevealFullDetailsFromUtc != before.RevealFullDetailsFromUtc
            || audit.PreviousPolicyVersion != before.PolicyVersion || audit.NewPolicyVersion != before.PolicyVersion + 1)
            throw new InvalidOperationException("Governance audit does not match its fixed transition.");
        if (audit.PreviousRevealFullDetailsFromUtc.HasValue)
            RequireTime(audit.PreviousRevealFullDetailsFromUtc.Value);
        if (audit.NewRevealFullDetailsFromUtc.HasValue)
            RequireTime(audit.NewRevealFullDetailsFromUtc.Value);
        RequireTime(message.CreatedAt);
        var expected = LocationPrivacyOutboxMessageFactory.CreateProjectionCorrection(
            message.Id, before.EventLocationId, before.TenantId, before.EventId, before.PolicyVersion + 1, changed);
        // Include lifecycle properties, not merely the fields used by this adapter.
        // The expected object comes from the existing fixed application factory.
        foreach (var property in context.Model.FindEntityType(typeof(OutboxMessage))!.GetProperties())
        {
            var getter = property.PropertyInfo
                ?? throw new InvalidOperationException("Outbox correction requires explicit scalar properties.");
            if (property.Name == nameof(OutboxMessage.Payload))
            {
                using var supplied = JsonDocument.Parse(message.Payload
                    ?? throw new InvalidOperationException("Governance outbox payload is required."));
                using var required = JsonDocument.Parse(expected.Payload
                    ?? throw new InvalidOperationException("The correction factory must produce a payload."));
                if (!JsonElement.DeepEquals(supplied.RootElement, required.RootElement))
                    throw new InvalidOperationException("Governance outbox payload does not match its fixed transition.");
            }
            else if (!Equals(getter.GetValue(message), getter.GetValue(expected)))
                throw new InvalidOperationException("Governance outbox envelope or lifecycle is invalid.");
        }
    }

    private IDbContextTransaction RequireTransaction() =>
        context.Database.CurrentTransaction
        ?? throw new InvalidOperationException("Location privacy governance requires its owning transaction.");

    private static void ValidateScope(string key, Guid? tenantId)
    {
        if (!PostgresLocationPrivacyGovernanceContract.SettingKeys.Contains(key, StringComparer.Ordinal))
            throw new ArgumentOutOfRangeException(nameof(key), "Unknown location privacy governance key.");
        if (tenantId.HasValue)
            RequireUuid(tenantId.Value);
    }

    private static void RequireUuid(Guid value)
    {
        if (value == Guid.Empty || value.Version != 7 || value.Variant is < 8 or > 11)
            throw new InvalidOperationException("Governance identities must be RFC 4122 UUIDv7 values.");
    }

    private static void RequireTime(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc || value == DateTime.MinValue || value == DateTime.MaxValue)
            throw new InvalidOperationException("Governance timestamps must be finite, non-default UTC values.");
    }

}
