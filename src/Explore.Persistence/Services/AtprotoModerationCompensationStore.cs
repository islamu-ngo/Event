using System.Collections.Immutable;
using System.Data;
using System.Runtime.CompilerServices;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Federation.Atproto.Services;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Federation;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Services;

/// <summary>
/// Projects compensation evidence without publication payloads or session envelopes.
/// All reads and fixed writes belong to the caller's Serializable transaction.
/// </summary>
public sealed class AtprotoModerationCompensationStore(ExploreDbContext context)
    : IAtprotoModerationCompensationStore
{
    private const int PageSize = 256;
    internal const string EmptyPayloadHash =
        "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    public async IAsyncEnumerable<AtprotoModerationDeleteCandidate> ReadDeleteCandidatesAsync(
        AtprotoModerationTarget target,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        RequireTransaction();
        ValidateTarget(target);
        long offset = 0;
        AtprotoPublicationLineage? after = null;
        while (true)
        {
            RequireTransaction();
            var native = await PostgresAtprotoModerationCompensationContract.TryReadPageAsync(
                context, target, offset, cancellationToken);
            if (native is not null)
            {
                IReadOnlyList<AtprotoModerationDeleteCandidate> page = native;
                foreach (AtprotoModerationDeleteCandidate candidate in page)
                {
                    RequireTransaction();
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return candidate;
                }
                if (page.Count < PageSize)
                    yield break;
                offset = checked(offset + page.Count);
            }
            else
            {
                await RequireSubjectAsync(target, cancellationToken);
                IQueryable<LineageRow> lineages = Lineages(target);
                if (after is not null)
                {
                    AtprotoPublicationLineage boundary = after;
                    lineages = lineages.Where(value =>
                        value.TenantId.CompareTo(boundary.TenantId) > 0 ||
                        value.TenantId == boundary.TenantId &&
                        (value.SourceEntityType.CompareTo(boundary.SourceEntityType) > 0 ||
                         value.SourceEntityType == boundary.SourceEntityType &&
                         (value.SourceEntityId.CompareTo(boundary.SourceEntityId) > 0 ||
                          value.SourceEntityId == boundary.SourceEntityId &&
                          (value.UserId.CompareTo(boundary.UserId) > 0 ||
                           value.UserId == boundary.UserId &&
                           (value.Did.CompareTo(boundary.Did) > 0 ||
                            value.Did == boundary.Did &&
                            (value.Collection.CompareTo(boundary.Collection) > 0 ||
                             value.Collection == boundary.Collection &&
                             value.RecordKey.CompareTo(boundary.RecordKey) > 0))))));
                }
                List<AtprotoPublicationLineage> page = await lineages
                    .OrderBy(value => value.TenantId)
                    .ThenBy(value => value.SourceEntityType)
                    .ThenBy(value => value.SourceEntityId)
                    .ThenBy(value => value.UserId)
                    .ThenBy(value => value.Did)
                    .ThenBy(value => value.Collection)
                    .ThenBy(value => value.RecordKey)
                    .Take(PageSize)
                    .Select(value => new AtprotoPublicationLineage(value.TenantId, value.SourceEntityType,
                        value.SourceEntityId, value.UserId, value.Did, value.Collection, value.RecordKey))
                    .ToListAsync(cancellationToken);
                foreach (AtprotoPublicationLineage lineage in page)
                {
                    RequireTransaction();
                    yield return await ReadCandidateAsync(target, lineage, cancellationToken);
                }
                if (page.Count < PageSize)
                    yield break;
                // Seek paging avoids narrowing the long position to LINQ's int Skip.
                offset = checked(offset + page.Count);
                after = page[^1];
            }
        }
    }

    public async Task WriteDeleteAsync(
        AtprotoModerationTarget target,
        AtprotoModerationDeleteWrite write,
        CancellationToken cancellationToken)
    {
        RequireTransaction();
        ValidateTarget(target);
        ArgumentNullException.ThrowIfNull(write);
        ValidateLineage(write.Lineage);
        if (write.SuccessorId == Guid.Empty || write.CreatedAtUtc.Kind != DateTimeKind.Utc ||
            write.NotBefore is { Kind: not DateTimeKind.Utc })
            throw new ArgumentException("A successor identifier and UTC timestamps are required.", nameof(write));

        if (await PostgresAtprotoModerationCompensationContract.TryWriteAsync(
                context, target, write, cancellationToken))
        {
            return;
        }

        await RequireSubjectAsync(target, cancellationToken);
        AtprotoModerationDeleteFence fence = await ReadFenceAsync(target, write.Lineage, cancellationToken);
        if (fence != write.Fence)
            throw new DbUpdateConcurrencyException("The moderation compensation fence changed.");

        IQueryable<PdsSyncOutbox> existing = Outboxes().Where(value => value.Id == write.SuccessorId);
        if (await existing.AnyAsync(cancellationToken))
        {
            if (!await MatchingSuccessor(existing, write).AnyAsync(cancellationToken))
                throw new InvalidOperationException("The compensation successor identifier has conflicting contents.");
            return;
        }

        AtprotoModerationDeleteCandidate candidate =
            await ReadCandidateAsync(target, write.Lineage, cancellationToken);
        ValidateWrite(candidate, write);

        // A real new entity is tracked; predecessors are updated through an exact
        // scalar predicate so their private payloads never leave the database.
        var successor = new PdsSyncOutbox
        {
            Id = write.SuccessorId,
            TenantId = write.Lineage.TenantId,
            UserId = write.Lineage.UserId,
            Did = write.Lineage.Did,
            Collection = write.Lineage.Collection,
            RecordKey = write.Lineage.RecordKey,
            SourceEntityType = write.Lineage.SourceEntityType,
            SourceEntityId = write.Lineage.SourceEntityId,
            SourceVersion = write.Fence.SourceVersion,
            Operation = PdsSyncOperation.Delete,
            Payload = null,
            PayloadHash = EmptyPayloadHash,
            IdempotencyKey = write.SuccessorId.ToString("N"),
            PdsHost = write.PdsHost,
            AtprotoRecordId = write.AtprotoRecordId,
            ExpectedCid = write.ExpectedCid,
            // Failed is unclaimable and outside active uniqueness. It is never committed:
            // the successor is activated only after its exact predecessors are superseded.
            Status = PdsSyncStatus.Failed,
            CreatedAt = write.CreatedAtUtc,
            NextRetryAt = write.NotBefore,
            MaxRetries = 10
        };
        context.PdsSyncOutbox.Add(successor);
        await context.SaveChangesAsync(cancellationToken);
        await ExactOutboxes(write.Lineage)
            .Where(value => value.Id != write.SuccessorId &&
                value.Status != PdsSyncStatus.Completed && value.Status != PdsSyncStatus.Superseded &&
                value.SupersededAt == null && value.SupersededById == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.Status, PdsSyncStatus.Superseded)
                .SetProperty(value => value.SupersededById, (Guid?)write.SuccessorId)
                .SetProperty(value => value.SupersededAt, (DateTime?)write.CreatedAtUtc)
                .SetProperty(value => value.LeaseOwner, (string?)null)
                .SetProperty(value => value.LeaseToken, (Guid?)null)
                .SetProperty(value => value.LeaseExpiresAt, (DateTime?)null), cancellationToken);
        successor.Status = PdsSyncStatus.Pending;
        await context.SaveChangesAsync(cancellationToken);
    }

    private void RequireTransaction()
    {
        if (context.Database.CurrentTransaction is not { } transaction ||
            transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Moderation compensation requires an existing Serializable transaction.");
    }

    private static void ValidateTarget(AtprotoModerationTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.ActorId == Guid.Empty || target.SubjectStamp == Guid.Empty ||
            target.IdentityId == Guid.Empty ||
            target.IdentityId.HasValue != (target.Did is not null) ||
            target.Did is not null && string.IsNullOrWhiteSpace(target.Did))
            throw new ArgumentException("An actor or an exact actor-owned identity and subject stamp are required.", nameof(target));
    }

    private static void ValidateLineage(AtprotoPublicationLineage lineage)
    {
        ArgumentNullException.ThrowIfNull(lineage);
        if (lineage.TenantId == Guid.Empty || lineage.SourceEntityId == Guid.Empty ||
            lineage.UserId == Guid.Empty || lineage.SourceEntityType != "Event" ||
            string.IsNullOrWhiteSpace(lineage.Did) ||
            lineage.Collection != AtprotoEventPublicationPlanner.EventCollection ||
            string.IsNullOrWhiteSpace(lineage.RecordKey))
            throw new ArgumentException("An exact Event publication lineage is required.", nameof(lineage));
    }

    private async Task RequireSubjectAsync(AtprotoModerationTarget target, CancellationToken cancellationToken)
    {
        bool grounded = await context.Actors.IncludeDeleted().AsNoTracking()
            .AnyAsync(actor => actor.Id == target.ActorId && !actor.IsDeleted &&
                (target.IdentityId == null
                    ? actor.IsSuspended && actor.ConcurrencyStamp == target.SubjectStamp
                    : context.AtprotoIdentities.IncludeDeleted().Any(identity =>
                        identity.Id == target.IdentityId && identity.ActorId == actor.Id &&
                        identity.Did == target.Did && !identity.IsDeleted && identity.IsSuspended &&
                        identity.ConcurrencyStamp == target.SubjectStamp)), cancellationToken);
        if (!grounded)
            throw new DbUpdateConcurrencyException("The suspended moderation subject changed.");
    }

    private IQueryable<LineageRow> Lineages(AtprotoModerationTarget target)
    {
        var settled =
            from ownership in Ownerships()
            join record in context.AtprotoRecords.AsNoTracking()
                on ownership.AtprotoRecordId equals record.Id
            join source in Events()
                on new { ownership.TenantId, Id = ownership.SourceEntityId }
                equals new { source.TenantId, source.Id }
            where ownership.SourceEntityType == "Event" &&
                record.Collection == AtprotoEventPublicationPlanner.EventCollection && source.ActorId == target.ActorId &&
                record.TombstonedAt == null && record.Cid != null &&
                (target.Did == null || record.Did == target.Did)
            select new LineageRow
            {
                TenantId = ownership.TenantId,
                SourceEntityType = ownership.SourceEntityType,
                SourceEntityId = ownership.SourceEntityId,
                UserId = ownership.UserId,
                Did = record.Did,
                Collection = record.Collection,
                RecordKey = record.RecordKey
            };
        var unsettled =
            from outbox in Outboxes()
            join source in Events()
                on new { outbox.TenantId, Id = outbox.SourceEntityId }
                equals new { source.TenantId, source.Id }
            where outbox.SourceEntityType == "Event" &&
                outbox.Collection == AtprotoEventPublicationPlanner.EventCollection && source.ActorId == target.ActorId &&
                (outbox.Status == PdsSyncStatus.Pending || outbox.Status == PdsSyncStatus.Processing) &&
                outbox.SupersededAt == null && outbox.SupersededById == null &&
                (target.Did == null || outbox.Did == target.Did)
            select new LineageRow
            {
                TenantId = outbox.TenantId,
                SourceEntityType = outbox.SourceEntityType,
                SourceEntityId = outbox.SourceEntityId,
                UserId = outbox.UserId,
                Did = outbox.Did,
                Collection = outbox.Collection,
                RecordKey = outbox.RecordKey
            };
        return settled.Union(unsettled);
    }

    private async Task<AtprotoModerationDeleteFence> ReadFenceAsync(
        AtprotoModerationTarget target, AtprotoPublicationLineage lineage, CancellationToken cancellationToken)
    {
        if (target.Did is not null && lineage.Did != target.Did)
            throw new DbUpdateConcurrencyException("The publication DID does not match the moderated identity.");
        var source = await (
            from value in Events()
            join actor in context.Actors.IncludeDeleted().AsNoTracking() on value.ActorId equals actor.Id
            where value.TenantId == lineage.TenantId && value.Id == lineage.SourceEntityId &&
                actor.Id == target.ActorId && !actor.IsDeleted
            select new { SourceVersion = value.ConcurrencyStamp, ActorStamp = actor.ConcurrencyStamp })
            .SingleOrDefaultAsync(cancellationToken);
        if (source is null)
            throw new DbUpdateConcurrencyException("The publication source is not owned by the moderated actor.");
        Guid? identityStamp = await context.AtprotoIdentities.IncludeDeleted().AsNoTracking()
            .Where(value => value.ActorId == target.ActorId && value.Did == lineage.Did && !value.IsDeleted &&
                (target.IdentityId == null || value.Id == target.IdentityId))
            .Select(value => (Guid?)value.ConcurrencyStamp)
            .SingleOrDefaultAsync(cancellationToken);
        return new(source.SourceVersion, source.ActorStamp, identityStamp);
    }

    private async Task<AtprotoModerationDeleteCandidate> ReadCandidateAsync(
        AtprotoModerationTarget target, AtprotoPublicationLineage lineage, CancellationToken cancellationToken)
    {
        await RequireSubjectAsync(target, cancellationToken);
        AtprotoModerationDeleteFence fence = await ReadFenceAsync(target, lineage, cancellationToken);
        AtprotoModerationSettledRecord? settled = await (
            from ownership in Ownerships()
            join record in context.AtprotoRecords.AsNoTracking() on ownership.AtprotoRecordId equals record.Id
            where ownership.TenantId == lineage.TenantId &&
                ownership.SourceEntityType == lineage.SourceEntityType &&
                ownership.SourceEntityId == lineage.SourceEntityId && ownership.UserId == lineage.UserId &&
                record.Did == lineage.Did && record.Collection == lineage.Collection &&
                record.RecordKey == lineage.RecordKey && record.TombstonedAt == null && record.Cid != null
            select new AtprotoModerationSettledRecord(record.Id, record.Cid))
            .SingleOrDefaultAsync(cancellationToken);
        List<AtprotoModerationMutation> unsettled = await ExactOutboxes(lineage)
            .Where(value => (value.Status == PdsSyncStatus.Pending || value.Status == PdsSyncStatus.Processing) &&
                value.SupersededAt == null && value.SupersededById == null)
            .OrderBy(value => value.CreatedAt).ThenBy(value => value.Id)
            .Select(value => new AtprotoModerationMutation(value.Id, value.Operation, value.Status,
                value.CreatedAt, value.NextRetryAt, value.LeaseExpiresAt, value.LeaseFence,
                value.PdsHost, value.ExpectedCid))
            .ToListAsync(cancellationToken);
        var sessions = await context.UserAuthenticationTokens
            .IgnoreTenantFilter(TenantFilterBypassReasons.AtprotoGlobalActorModeration).AsNoTracking()
            .Where(value => value.TenantId == lineage.TenantId && value.UserId == lineage.UserId &&
                value.Provider == AuthenticationProviderDeploymentOptions.AtprotoProvider)
            .OrderBy(value => value.Id).Take(2)
            .Select(value => new { value.SubjectDid, value.PdsHost })
            .ToListAsync(cancellationToken);
        bool linked = await context.UserExternalLogins.AsNoTracking().AnyAsync(value =>
            value.UserId == lineage.UserId &&
            value.AuthenticationProviderId == (int)AuthenticationProviderKind.Atproto &&
            value.ProviderKey == lineage.Did, cancellationToken);
        var account = new AtprotoAccountBindingFacts(sessions.Count,
            sessions.Count == 1 ? sessions[0].SubjectDid : null,
            sessions.Count == 1 ? sessions[0].PdsHost : null, linked);
        var policy = new AtprotoModerationPolicyFacts(
            await ReadSettingAsync(GovernanceSettingKeys.Federation.AtprotoEventsEnabled, lineage, cancellationToken),
            await ReadSettingAsync(GovernanceSettingKeys.Federation.AtprotoEventValidationProfile, lineage, cancellationToken),
            await ReadSettingAsync(GovernanceSettingKeys.Federation.AtprotoPublishMyEvents, lineage, cancellationToken));
        // Portable timestamp columns store UTC clock values without preserving DateTime.Kind.
        // Restore that contract before computing the processing lease safety window.
        return new(lineage, fence, settled, unsettled.Select(value => value with
        {
            CreatedAt = DateTime.SpecifyKind(value.CreatedAt, DateTimeKind.Utc),
            NextRetryAt = value.NextRetryAt.HasValue
                ? DateTime.SpecifyKind(value.NextRetryAt.Value, DateTimeKind.Utc) : null,
            LeaseExpiresAt = value.LeaseExpiresAt.HasValue
                ? DateTime.SpecifyKind(value.LeaseExpiresAt.Value, DateTimeKind.Utc) : null
        }).ToImmutableArray(), policy, account);
    }

    private async Task<SettingMergeFacts> ReadSettingAsync(
        string key, AtprotoPublicationLineage lineage, CancellationToken cancellationToken)
    {
        SystemSettingFacts? system = await context.SystemSettings.AsNoTracking()
            .Where(value => value.SettingKey == key)
            .Select(value => new SystemSettingFacts(value.Value, (SettingValueType)value.SettingValueTypeId,
                value.IsLocked, value.Description, value.Category, value.AllowedValues))
            .SingleOrDefaultAsync(cancellationToken);
        TenantSettingFacts? tenant = await context.TenantSettingOverrides
            .IgnoreTenantFilter(TenantFilterBypassReasons.AtprotoGlobalActorModeration).AsNoTracking()
            .Where(value => value.TenantId == lineage.TenantId && value.SettingKey == key)
            .Select(value => new TenantSettingFacts(value.Value, value.IsLocked))
            .SingleOrDefaultAsync(cancellationToken);
        string? user = await context.UserPreferences
            .IgnoreTenantFilter(TenantFilterBypassReasons.AtprotoGlobalActorModeration).AsNoTracking()
            .Where(value => value.TenantId == lineage.TenantId && value.UserId == lineage.UserId &&
                value.SettingKey == key)
            .Select(value => value.Value).SingleOrDefaultAsync(cancellationToken);
        return new(system, tenant, null, null, user, true, null);
    }

    private IQueryable<Event> Events() => context.Events
        .IgnoreTenantFilter(TenantFilterBypassReasons.AtprotoGlobalActorModeration)
        .IncludeDeleted().AsNoTracking();

    private IQueryable<AtprotoOutboundRecordOwnership> Ownerships() => context.AtprotoOutboundRecordOwnerships
        .IgnoreTenantFilter(TenantFilterBypassReasons.AtprotoGlobalActorModeration).AsNoTracking();

    private IQueryable<PdsSyncOutbox> Outboxes() => context.PdsSyncOutbox
        .IgnoreTenantFilter(TenantFilterBypassReasons.AtprotoGlobalActorModeration).AsNoTracking();

    private IQueryable<PdsSyncOutbox> ExactOutboxes(AtprotoPublicationLineage lineage) => Outboxes()
        .Where(value => value.TenantId == lineage.TenantId &&
            value.SourceEntityType == lineage.SourceEntityType && value.SourceEntityId == lineage.SourceEntityId &&
            value.UserId == lineage.UserId && value.Did == lineage.Did &&
            value.Collection == lineage.Collection && value.RecordKey == lineage.RecordKey);

    private static IQueryable<PdsSyncOutbox> MatchingSuccessor(
        IQueryable<PdsSyncOutbox> query, AtprotoModerationDeleteWrite write) =>
        query.Where(value => value.TenantId == write.Lineage.TenantId &&
            value.SourceEntityType == write.Lineage.SourceEntityType &&
            value.SourceEntityId == write.Lineage.SourceEntityId &&
            value.UserId == write.Lineage.UserId && value.Did == write.Lineage.Did &&
            value.Collection == write.Lineage.Collection && value.RecordKey == write.Lineage.RecordKey &&
            value.SourceVersion == write.Fence.SourceVersion && value.Operation == PdsSyncOperation.Delete &&
            value.Payload == null && value.PayloadHash == EmptyPayloadHash &&
            value.IdempotencyKey == write.SuccessorId.ToString("N") && value.PdsHost == write.PdsHost &&
            value.AtprotoRecordId == write.AtprotoRecordId && value.ExpectedCid == write.ExpectedCid &&
            value.DependsOnAtprotoRecordId == null && value.DependsOnCid == null &&
            value.CreatedAt == write.CreatedAtUtc && value.NextRetryAt == write.NotBefore &&
            value.MaxRetries == 10);

    private static void ValidateWrite(
        AtprotoModerationDeleteCandidate candidate, AtprotoModerationDeleteWrite write)
    {
        if (candidate.Fence != write.Fence ||
            candidate.Settled?.RecordId != write.AtprotoRecordId ||
            (candidate.Unsettled.IsEmpty ? candidate.Settled?.Cid : null) != write.ExpectedCid ||
            candidate.Unsettled.IsEmpty && string.IsNullOrWhiteSpace(candidate.Settled?.Cid))
            throw new DbUpdateConcurrencyException("The grounded publication evidence changed.");
        AtprotoAccountBindingFacts account = candidate.Account;
        if (account.SessionCount != 1 || account.SubjectDid != write.Lineage.Did ||
            !account.ExactExternalLoginExists ||
            !Uri.TryCreate(account.PdsHost, UriKind.Absolute, out Uri? pds) ||
            pds.Scheme != Uri.UriSchemeHttps || pds.Query.Length != 0 || pds.Fragment.Length != 0 ||
            pds.AbsoluteUri != write.PdsHost ||
            candidate.Unsettled.Any(value => !AtprotoEventPublicationPlanner.SamePds(pds, value.PdsHost)))
            throw new DbUpdateConcurrencyException("The grounded publication account binding changed.");
        DateTime? notBefore = null;
        foreach (AtprotoModerationMutation mutation in candidate.Unsettled)
        {
            DateTime? delay = AtprotoEventPublicationPlanner.CompensationNotBefore(
                mutation.Status, mutation.NextRetryAt, mutation.LeaseExpiresAt, write.CreatedAtUtc);
            if (delay.HasValue && (!notBefore.HasValue || delay.Value > notBefore.Value))
                notBefore = delay;
        }
        if (notBefore != write.NotBefore)
            throw new DbUpdateConcurrencyException("The grounded predecessor delay changed.");
    }

    private sealed class LineageRow
    {
        public Guid TenantId { get; init; }
        public string SourceEntityType { get; init; } = null!;
        public Guid SourceEntityId { get; init; }
        public Guid UserId { get; init; }
        public string Did { get; init; } = null!;
        public string Collection { get; init; } = null!;
        public string RecordKey { get; init; } = null!;
    }
}
