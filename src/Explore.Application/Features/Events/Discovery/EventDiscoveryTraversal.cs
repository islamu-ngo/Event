using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.PublicExperience;
using Explore.Application.Exceptions;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Services.Discovery;
using Explore.Domain.Settings.Definitions;
using FluentValidation;

namespace Explore.Application.Features.Events.Discovery;

/// <summary>Frozen bounded membership with current source reprojection and terminal native authority validation.</summary>
public sealed class EventDiscoveryTraversal(
    IUnitOfWork unitOfWork,
    IEventDiscoverySnapshotRepository snapshots,
    IEventDiscoveryIdentityRepository identities,
    IEventDiscoveryDisclosureRepository disclosure,
    EventDiscoveryCandidateReader candidates,
    IEventDiscoveryCursorProtector protector,
    IHierarchicalSettingsResolver settings,
    ITenantContext tenant,
    TimeProvider clock) : IQueryHandler<GetEventDiscoveryTraversalQuery, EventDiscoveryTraversalDto>
{
    public async Task<EventDiscoveryTraversalDto> QueryAsync(
        GetEventDiscoveryTraversalQuery request, CancellationToken cancellationToken = default)
    {
        var criteria = request.Criteria;
        string? cursor = request.Cursor;
        await new GetEventDiscoveryTraversalQueryValidator().ValidateAndThrowAsync(request, cancellationToken);

        string hash = EventDiscoveryCriteria.Digest(criteria);
        EventDiscoveryContinuation? continuation = Decode(cursor, hash);
        Guid snapshotId = Guid.CreateVersion7();
        DateTimeOffset startedAt = criteria.OperationNow ?? clock.GetUtcNow();
        criteria = criteria with { OperationNow = startedAt };

        for (int attempt = 0; ; attempt++)
        {
            var initial = await identities.GetRevisionAsync(tenant.TenantId, cancellationToken);
            if (initial is null)
                initial = await unitOfWork.ExecuteReadCommittedAsync(
                    token => disclosure.AcquireCurrentAsync(tenant.TenantId, token), cancellationToken);

            try
            {
                return await unitOfWork.ExecuteSerializableAsync(async token =>
                {
                    await snapshots.AcquireFenceAsync(tenant.TenantId, token);
                    EventDiscoveryTraversalLimits limits = await ResolveLimitsAsync(token);
                    DateTime nowUtc = clock.GetUtcNow().UtcDateTime;
                    EventDiscoverySnapshot? snapshot = continuation is null
                        ? await snapshots.FindReusableAsync(tenant.TenantId, hash,
                            initial.IdentityEpoch, initial.DisclosureEpoch, nowUtc, token)
                        : await snapshots.GetAsync(tenant.TenantId, continuation.SnapshotId, token);
                    bool newlyCaptured = snapshot is null && continuation is null;
                    if (continuation is not null && snapshot is null)
                        throw new EventDiscoveryRestartRequiredException();

                    if (newlyCaptured)
                    {
                        var capture = await candidates.CaptureAsync(criteria, limits, token);
                        DateTimeOffset expiry = startedAt + limits.Lifetime;
                        if (capture.ValidUntilUtc < expiry)
                            expiry = capture.ValidUntilUtc;
                        expiry = expiry.AddTicks(-(expiry.Ticks % 10));
                        if (clock.GetUtcNow() >= expiry)
                            throw new EventDiscoveryCursorExpiredException();
                        snapshot = EventDiscoverySnapshot.Create(
                            snapshotId, tenant.TenantId, hash, initial.IdentityEpoch, initial.DisclosureEpoch,
                            startedAt.UtcDateTime, expiry.UtcDateTime, capture.Truncated,
                            capture.LocalSourceComplete, capture.RemoteSourceComplete, capture.Membership, limits);
                    }

                    var currentSnapshot = snapshot!;
                    if (clock.GetUtcNow().UtcDateTime >= currentSnapshot.ExpiresAtUtc)
                        throw new EventDiscoveryCursorExpiredException();
                    if (currentSnapshot.CriteriaHash != hash
                        || currentSnapshot.IdentityEpoch != initial.IdentityEpoch
                        || currentSnapshot.DisclosureEpoch != initial.DisclosureEpoch)
                        throw new EventDiscoveryRestartRequiredException();
                    if (continuation is not null &&
                        (continuation.IdentityEpoch != currentSnapshot.IdentityEpoch
                         || continuation.DisclosureEpoch != currentSnapshot.DisclosureEpoch
                         || continuation.ExpiresAtUtc.UtcDateTime != currentSnapshot.ExpiresAtUtc
                         || continuation.NextOrdinal >= currentSnapshot.ItemCount))
                        throw new EventDiscoveryCursorInvalidException();

                    long ordinal = continuation?.NextOrdinal ?? 0;
                    IReadOnlyList<EventDiscoverySnapshotItem> membership = newlyCaptured
                        ? currentSnapshot.Items.Take(criteria.PageSize).ToArray()
                        : await snapshots.GetItemsAsync(tenant.TenantId, currentSnapshot.Id,
                            ordinal - 1, criteria.PageSize, token);
                    var cards = await candidates.ReprojectAsync(criteria, membership, token);
                    if (cards is null || membership.Count != Math.Min(criteria.PageSize, currentSnapshot.ItemCount - ordinal))
                        throw new EventDiscoveryRestartRequiredException();

                    // No source reads or enrichment follow this terminal native fence.
                    var current = await disclosure.AcquireCurrentAsync(tenant.TenantId, token);
                    if (current.IdentityEpoch != initial.IdentityEpoch || current.DisclosureEpoch != initial.DisclosureEpoch)
                        throw new EventDiscoveryRestartRequiredException();
                    if (clock.GetUtcNow().UtcDateTime >= currentSnapshot.ExpiresAtUtc)
                        throw new EventDiscoveryCursorExpiredException();
                    if (newlyCaptured)
                        currentSnapshot = await snapshots.CaptureAsync(currentSnapshot, limits,
                            clock.GetUtcNow().UtcDateTime, token) ?? throw new EventDiscoveryUnavailableException();

                    long nextOrdinal = ordinal + membership.Count;
                    bool hasMore = nextOrdinal < currentSnapshot.ItemCount;
                    string? nextCursor;
                    try
                    {
                        nextCursor = hasMore ? protector.Protect(new(
                            tenant.TenantId, currentSnapshot.Id, hash, nextOrdinal,
                            new DateTimeOffset(currentSnapshot.ExpiresAtUtc),
                            currentSnapshot.IdentityEpoch, currentSnapshot.DisclosureEpoch)) : null;
                    }
                    catch (CryptographicException)
                    {
                        throw new EventDiscoveryUnavailableException();
                    }
                    return new EventDiscoveryTraversalDto
                    {
                        Items = cards, SnapshotCount = currentSnapshot.ItemCount, Truncated = currentSnapshot.Truncated,
                        ExpiresAt = new(currentSnapshot.ExpiresAtUtc), HasMore = hasMore, NextCursor = nextCursor,
                        Authority = new(tenant.TenantId, current.IdentityEpoch, current.DisclosureEpoch,
                            new DateTimeOffset(currentSnapshot.ExpiresAtUtc))
                    };
                }, cancellationToken);
            }
            catch (EventDiscoveryRestartRequiredException) when (continuation is null && attempt < 3)
            {
                // A first search rebuilds from fresh committed epochs; an existing cursor never silently restarts.
            }
        }
    }

    private EventDiscoveryContinuation? Decode(string? cursor, string hash)
    {
        if (cursor is null)
            return null;
        EventDiscoveryContinuation continuation;
        try
        {
            continuation = protector.Unprotect(cursor);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or JsonException)
        {
            throw new EventDiscoveryCursorInvalidException();
        }
        if (continuation.TenantId != tenant.TenantId || continuation.SnapshotId == Guid.Empty
            || continuation.CriteriaHash != hash || continuation.NextOrdinal is <= 0 or >= EventDiscoveryTraversalLimits.IdentityCeiling
            || continuation.IdentityEpoch < 0 || continuation.DisclosureEpoch < 0)
            throw new EventDiscoveryCursorInvalidException();
        if (clock.GetUtcNow() >= continuation.ExpiresAtUtc)
            throw new EventDiscoveryCursorExpiredException();
        return continuation;
    }

    private async Task<EventDiscoveryTraversalLimits> ResolveLimitsAsync(CancellationToken cancellationToken)
    {
        var definitions = EventDiscoveryTraversalSettingDefinitions.All;
        var resolved = await settings.ResolveBatchAsync(definitions.Select(definition => definition.Key),
            new SettingContext(TenantId: tenant.TenantId), cancellationToken);
        var values = resolved.ToDictionary(setting => setting.Key, StringComparer.Ordinal);
        var budgets = new int[definitions.Count];
        for (int index = 0; index < definitions.Count; index++)
        {
            if (!values.TryGetValue(definitions[index].Key, out var value)
                || !int.TryParse(value.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out budgets[index]))
                throw new EventDiscoveryUnavailableException();
        }
        return new(budgets[0], budgets[1], budgets[2], budgets[3], budgets[4], budgets[5]);
    }
}
