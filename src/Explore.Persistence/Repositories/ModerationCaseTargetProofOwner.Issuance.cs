using System.Security.Cryptography;
using Explore.Application.Contracts.Moderation;
using Explore.Domain;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationCaseTargetProofOwner
{
    public async Task<ModerationCaseIssuedReceipt?> IssueSubmissionAsync(
        ModerationCaseTargetIssueRequest request, CancellationToken cancellationToken)
    {
        ResetExpiredTransactionState();
        if (_pending is not null || !Enum.IsDefined(request.Kind)
            || request.NativeKind is < ModerationCaseTargetKind.EventVersion or > ModerationCaseTargetKind.Destination
            || request.Kind is not (ModerationCaseTargetKind.LocalIncident or ModerationCaseTargetKind.HiddenContentReceipt)
                && request.Kind != request.NativeKind
            || request.Kind == ModerationCaseTargetKind.LocalIncident
                && (request.NativeKind != ModerationCaseTargetKind.EventVersion || request.NativeTargetId != request.EventId)
            || !Enum.IsDefined(request.RelationshipKind) || request.RelationshipKind == ModerationCaseRelationshipKind.CaseReadGrant
            || request.RelationshipKind == ModerationCaseRelationshipKind.PublicEvent
                && request.Kind != ModerationCaseTargetKind.EventVersion
            || request.OccurrenceMaterial.Length is < 1 or > 65536 || request.Material.Length is < 1 or > 65536)
            return null;
        // Freeze material before the first await; the caller cannot race mutable request buffers.
        var occurrenceDigest = Digest(request.OccurrenceMaterial.Span);
        var materialDigest = Digest(request.Material.Span);
        var environment = await EnvironmentAsync(true, cancellationToken,
            request.NativeKind is ModerationCaseTargetKind.EventVersion or ModerationCaseTargetKind.Actor or ModerationCaseTargetKind.Service
                ? request.EventId : null);
        if (environment is null) return null;
        var policy = await PolicyAsync(request.Kind, true, cancellationToken);
        if (policy is null) return null;
        var native = await NativeTargetAsync(request.NativeKind, request.NativeTargetId,
            request.EventId, true, cancellationToken);
        if (native is null) return null;
        var now = PersistedTime(clock.GetUtcNow());
        await using var sourceFence = await RelationalNamedLock.AcquireTransactionAsync(context,
            $"moderation.source:{environment.TenantId:D}:{request.NativeTargetId:D}:{occurrenceDigest}", cancellationToken);
        var sameOccurrence = context.ModerationCaseTargetSources.AsNoTracking().Where(row =>
            row.Kind == request.Kind && row.NativeKind == request.NativeKind
            && row.EventId == request.EventId && row.EventVersion == native.EventVersion
            && row.NativeTargetId == request.NativeTargetId && row.NativeTargetVersion == native.TargetVersion
            && row.NativeFactsDigest == native.Fingerprint && row.OccurrenceDigest == occurrenceDigest);
        var existing = await sameOccurrence.OrderBy(row => row.Id).FirstOrDefaultAsync(cancellationToken);
        var source = await sameOccurrence.SingleOrDefaultAsync(row => row.MaterialDigest == materialDigest, cancellationToken)
            ?? ModerationCaseTargetSource.Record(Guid.CreateVersion7(), Guid.CreateVersion7(),
                environment.TenantId, environment.TrustId, request.EventId, native.EventVersion,
                request.Kind, request.NativeKind, request.NativeTargetId, native.TargetVersion, native.Fingerprint,
                existing?.OccurrenceId ?? Guid.CreateVersion7(), existing?.OccurrenceVersion ?? Guid.CreateVersion7(),
                Guid.CreateVersion7(), occurrenceDigest, materialDigest, now);
        var relationship = await RelationshipAsync(request.RelationshipKind, request.RelationshipSourceId,
            source, native, environment, Guid.Empty, true, now, cancellationToken);
        if (relationship is null) return null;
        if (context.Entry(source).State == EntityState.Detached
            && !await context.ModerationCaseTargetSources.AsNoTracking().AnyAsync(row => row.Id == source.Id, cancellationToken))
            context.ModerationCaseTargetSources.Add(source);
        else if (!await FenceAsync<ModerationCaseTargetSource>(source.Id, true, cancellationToken))
            return null;
        // An inserted row is held by this transaction after SaveChanges as well.
        _fences.Add((typeof(ModerationCaseTargetSource), source.Id));
        return StageReceipt(source, policy, relationship, request.RelationshipKind, request.RelationshipSourceId,
            environment, Guid.Empty, now);
    }

    public async Task<ModerationCaseIssuedReceipt?> IssueReadAsync(Guid readGrantId, CancellationToken cancellationToken)
    {
        ResetExpiredTransactionState();
        if (_pending is not null) return null;
        var environment = await EnvironmentAsync(true, cancellationToken, readGrantId: readGrantId);
        if (environment is null || !await FenceAsync<ModerationCaseReadGrant>(readGrantId, true, cancellationToken))
            return null;
        var grant = await context.ModerationCaseReadGrants.AsNoTracking().SingleOrDefaultAsync(
            row => row.Id == readGrantId && row.CallerUserId == environment.CallerId, cancellationToken);
        if (grant is null || _readIssuancePlan is not { } plan || ReadGrantCoordinates.From(grant) != plan.Grant
            || !await FenceAsync<ModerationCaseTargetSource>(plan.Source.Id, true, cancellationToken))
            return null;
        var source = await context.ModerationCaseTargetSources.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == grant.SourceId, cancellationToken);
        if (source is null || ReadSourceCoordinates.From(source) != plan.Source) return null;
        var policy = await PolicyAsync(source.Kind, true, cancellationToken);
        var native = await NativeTargetAsync(source.NativeKind, source.NativeTargetId, source.EventId, true, cancellationToken);
        if (policy is null || native is null || native.Fingerprint != source.NativeFactsDigest) return null;
        var now = PersistedTime(clock.GetUtcNow());
        var relationship = await RelationshipAsync(ModerationCaseRelationshipKind.CaseReadGrant, grant.Id,
            source, native, environment, grant.CaseId, true, now, cancellationToken);
        return relationship is null ? null : StageReceipt(source, policy, relationship,
            ModerationCaseRelationshipKind.CaseReadGrant, grant.Id, environment, grant.CaseId, now, plan.Event);
    }

    private ModerationCaseIssuedReceipt? StageReceipt(ModerationCaseTargetSource source, ModerationCaseTargetPolicy policy,
        Relationship relationship, ModerationCaseRelationshipKind relationshipKind, Guid relationshipId,
        NativeEnvironment environment, Guid caseId, DateTimeOffset now, EventActorPlan? readEventPlan = null)
    {
        var read = relationshipKind == ModerationCaseRelationshipKind.CaseReadGrant;
        var expiresAt = PersistedTime(now.Add(read ? policy.ReadLifetime : policy.SubmissionLifetime));
        if (relationship.Until < expiresAt) expiresAt = relationship.Until;
        if (expiresAt <= now) return null;
        var raw = RandomNumberGenerator.GetBytes(32);
        var receipt = ModerationCaseTargetReceipt.Issue(Guid.CreateVersion7(), environment.Scope, environment.CallerId,
            environment.HostId, environment.EpochId, environment.EpochRevision, relationshipId, relationshipKind,
            relationship.Generation, policy.Id, policy.Revision, source.Evidence(environment.Scope),
            SHA256.HashData(raw), now, expiresAt,
            relationshipKind == ModerationCaseRelationshipKind.InstanceAdministrator
                ? Digest(System.Text.Encoding.UTF8.GetBytes(relationship.Fingerprint)) : null);
        context.ModerationCaseTargetReceipts.Add(receipt);
        _fences.Add((typeof(ModerationCaseTargetReceipt), receipt.Id));
        var expectedAuthority = $"{source.Version:D}|{policy.Id:D}|{policy.Revision}|{source.NativeFactsDigest}|{relationship.Fingerprint}";
        _pending = new(environment, now, expiresAt, async token =>
        {
            var persisted = await context.ModerationCaseTargetReceipts.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == receipt.Id, token);
            if (persisted is null || persisted.Revision != 1 || persisted.TokenDigest != receipt.TokenDigest
                || persisted.CallerUserId != environment.CallerId || persisted.Evidence != receipt.Evidence
                || persisted.RelationshipKind != relationshipKind || persisted.RelationshipSourceId != relationshipId
                || persisted.RelationshipGeneration != relationship.Generation
                || persisted.RelationshipFactsDigest != receipt.RelationshipFactsDigest
                || persisted.IssuedAt != receipt.IssuedAt || persisted.ExpiresAt != expiresAt
                || persisted.HostBindingId != environment.HostId || persisted.QualificationEpochId != environment.EpochId
                || persisted.QualificationRevision != environment.EpochRevision
                || persisted.LifetimePolicyId != policy.Id || persisted.LifetimePolicyRevision != policy.Revision)
                return false;
            // A role swap can preserve both the Event stamp and the set of already-held Actor anchors.
            if (read && !await ReadIssuanceCoordinatesMatchAsync(token)) return false;
            if (readEventPlan is not null && await ReadEventActorPlanAsync(readEventPlan.EventId, token) != readEventPlan)
                return false;
            var authority = await ReceiptAuthorityAsync(persisted,
                read ? ModerationCaseAccessPurpose.ReadAllegation : ModerationCaseAccessPurpose.SubmitAllegation,
                caseId, environment, false, clock.GetUtcNow(), token);
            return authority?.Fingerprint == expectedAuthority && clock.GetUtcNow() < expiresAt;
        });
        return new(receipt, raw);
    }

    private async Task<ModerationCaseTargetPolicy?> PolicyAsync(
        ModerationCaseTargetKind kind, bool acquire, CancellationToken token)
    {
        var discovered = await context.ModerationCaseTargetPolicies.AsNoTracking()
            .SingleOrDefaultAsync(row => row.TargetKind == kind, token);
        if (discovered is null || !await FenceAsync<ModerationCaseTargetPolicy>(discovered.Id, acquire, token))
            return null;
        return await context.ModerationCaseTargetPolicies.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == discovered.Id && row.TargetKind == kind && row.Enabled, token);
    }

    public async Task<bool> RecheckIssuanceAsync(CancellationToken cancellationToken)
    {
        if (_pending is not { } pending || context.ChangeTracker.HasChanges()
            || await EnvironmentAsync(false, cancellationToken) != pending.Environment)
            return false;
        if (!await pending.Recheck(cancellationToken)
            || await EnvironmentAsync(false, cancellationToken) != pending.Environment)
            return false;
        var completedAt = clock.GetUtcNow();
        return completedAt >= pending.NotBefore && completedAt < pending.ValidUntil;
    }
}
