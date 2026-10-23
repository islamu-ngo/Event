using Explore.Application.Contracts.Moderation;
using Explore.Domain;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationCaseTargetProofOwner
{
    public async Task<ModerationCaseAttributionSource?> StageAttributionRenewalAsync(
        ModerationCaseAttributionRenewalRequest request, CancellationToken cancellationToken)
    {
        ResetAttributionTransaction();
        if (_attribution is not null || _pending is not null || _proof is not null
            || request.CaseId == Guid.Empty || request.ExpectedSourceId == Guid.Empty
            || request.ExpectedCaseRevision < 1 || request.ExpectedGeneration < 1
            || request.ExpectedGeneration == long.MaxValue
            || request.ExpectedHeadRevision != request.ExpectedGeneration)
            return null;
        var qualification = request.Qualification;
        var facts = await AttributionFactsAsync(qualification, true, cancellationToken);
        if (facts is null || facts.Grant.CallerUserId != facts.Environment.CallerId) return null;
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context,
            $"moderation.case-attribution:{facts.Environment.TenantId:D}:{qualification.OccurrenceCaseId:D}:" +
            $"{(int)qualification.SubjectKind}:{qualification.SubjectId:D}", cancellationToken);
        var discoveredHead = await context.ModerationCaseAttributionHeads.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CaseId == request.CaseId, cancellationToken);
        if (discoveredHead is null
            || !await FenceAsync<ModerationCaseAttributionHead>(discoveredHead.Id, true, cancellationToken)
            || !await FenceAsync<ModerationCase>(request.CaseId, true, cancellationToken))
            return null;
        var head = await context.ModerationCaseAttributionHeads.AsNoTracking()
            .SingleAsync(row => row.Id == discoveredHead.Id, cancellationToken);
        var adjudication = await context.ModerationCases.AsNoTracking()
            .SingleAsync(row => row.Id == request.CaseId, cancellationToken);
        if (adjudication.State != ModerationCaseState.Open || adjudication.Subject != facts.Subject
            || adjudication.Scope != facts.Environment.Scope || adjudication.Revision != request.ExpectedCaseRevision)
            return null;
        var source = await context.ModerationCaseAttributionSources.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == qualification.OperationId, cancellationToken);
        ModerationCaseAttributionRenewalReceipt receipt;
        if (source is not null)
        {
            var discoveredReceipt = await context.ModerationCaseAttributionRenewalReceipts.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == source.Id, cancellationToken);
            if (discoveredReceipt is null
                || !await FenceAsync<ModerationCaseAttributionRenewalReceipt>(source.Id, true, cancellationToken)
                || !await FenceAsync<ModerationCaseAttributionSource>(source.Id, true, cancellationToken))
                return null;
            receipt = await context.ModerationCaseAttributionRenewalReceipts.AsNoTracking()
                .SingleAsync(row => row.Id == source.Id, cancellationToken);
            source = await context.ModerationCaseAttributionSources.AsNoTracking()
                .SingleAsync(row => row.Id == source.Id, cancellationToken);
            if (!RenewalReceiptMatches(receipt, request, head, source)
                || !AttributionMatches(source, adjudication, head, qualification, facts, clock.GetUtcNow()))
                return null;
        }
        else
        {
            if (head.SourceId != request.ExpectedSourceId || head.Generation != request.ExpectedGeneration
                || head.Revision != request.ExpectedHeadRevision
                || !await FenceAsync<ModerationCaseAttributionSource>(head.SourceId, true, cancellationToken))
                return null;
            var prior = await context.ModerationCaseAttributionSources.AsNoTracking()
                .SingleAsync(row => row.Id == head.SourceId, cancellationToken);
            var host = await context.ModerationHostBindings.AsNoTracking()
                .SingleAsync(row => row.Id == facts.Environment.HostId, cancellationToken);
            var epoch = await context.ModerationQualificationEpochs.AsNoTracking()
                .SingleAsync(row => row.Id == facts.Environment.EpochId, cancellationToken);
            var now = PersistedTime(clock.GetUtcNow());
            source = ModerationCaseAttributionSource.Qualify(qualification.OperationId, adjudication.Id,
                checked(head.Generation + 1), facts.Occurrence, qualification.EvidenceCaseRevision,
                facts.Material, facts.Subject, qualification.Basis, host, epoch, facts.Grant, facts.Policy,
                facts.Qualifier.GrantId, facts.Qualifier.Platform, facts.Digest, facts.AuthorityDigest,
                now, qualification.ValidUntil, facts.Investigation);
            try
            {
                receipt = ModerationCaseAttributionRenewalReceipt.Record(adjudication, head, source, host, now);
                context.Attach(head);
                head.Advance(request.ExpectedSourceId, request.ExpectedGeneration, request.ExpectedHeadRevision,
                    adjudication, prior, source, host, now);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
            context.ModerationCaseAttributionSources.Add(source);
            context.ModerationCaseAttributionRenewalReceipts.Add(receipt);
            _fences.Add((typeof(ModerationCaseAttributionSource), source.Id));
            _fences.Add((typeof(ModerationCaseAttributionRenewalReceipt), receipt.Id));
        }
        HoldAttribution(qualification, source, adjudication, head, facts);
        var held = _attribution!;
        _attribution = held with
        {
            Recheck = async token =>
                await held.Recheck(token)
                && await context.ModerationCaseAttributionRenewalReceipts.AsNoTracking().AnyAsync(row =>
                    row.Id == receipt.Id && row.CaseId == request.CaseId && row.HeadId == head.Id
                    && row.TenantId == facts.Environment.TenantId && row.TrustDomainId == facts.Environment.TrustId
                    && row.ExpectedCaseRevision == request.ExpectedCaseRevision
                    && row.ExpectedSourceId == request.ExpectedSourceId
                    && row.ExpectedGeneration == request.ExpectedGeneration
                    && row.ExpectedHeadRevision == request.ExpectedHeadRevision
                    && row.QualifiedAt == source.QualifiedAt, token)
        };
        return source;
    }

    private static bool RenewalReceiptMatches(ModerationCaseAttributionRenewalReceipt receipt,
        ModerationCaseAttributionRenewalRequest request, ModerationCaseAttributionHead head,
        ModerationCaseAttributionSource source)
    {
        receipt.ValidatePersistedState();
        return receipt.Id == source.Id && receipt.CaseId == request.CaseId && receipt.HeadId == head.Id
            && receipt.TenantId == head.TenantId && receipt.TrustDomainId == head.TrustDomainId
            && receipt.ExpectedCaseRevision == request.ExpectedCaseRevision
            && receipt.ExpectedSourceId == request.ExpectedSourceId
            && receipt.ExpectedGeneration == request.ExpectedGeneration
            && receipt.ExpectedHeadRevision == request.ExpectedHeadRevision
            && source.Generation == request.ExpectedGeneration + 1 && receipt.QualifiedAt == source.QualifiedAt
            && head.SourceId == source.Id && head.Generation == source.Generation
            && head.Revision == request.ExpectedHeadRevision + 1;
    }
}
