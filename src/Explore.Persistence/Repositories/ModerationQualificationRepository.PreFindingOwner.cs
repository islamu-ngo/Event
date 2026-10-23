using Explore.Application.Contracts.Moderation;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationQualificationRepository : IModerationPreFindingResponseOwner
{
    private PreFindingDiscovery? _preFindingDiscovery;
    private PreFindingAcquisition? _preFindingResponse;

    public async Task<IReadOnlyList<Guid>?> DiscoverResponseHumansAsync(Guid caseId, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is not { } transaction || context.IsTenantFilterBypassed)
            return null;
        var discovery = await ModerationQualificationAuthority
            .DiscoverPreFindingHumansAsync(context, caseId, [], cancellationToken);
        if (discovery is null) return null;
        if (_preFindingDiscovery?.TransactionId != transaction.TransactionId) _preFindingResponse = null;
        // Discovery is once per native case acquisition, not a way to add humans after fencing.
        if (_preFindingDiscovery is { } prior && prior.TransactionId == transaction.TransactionId
            && (prior.CaseId != caseId || !prior.Humans.SequenceEqual(discovery.Humans)))
            return null;
        if (_preFindingDiscovery?.TransactionId != transaction.TransactionId)
            _preFindingDiscovery = new(transaction.TransactionId, caseId, discovery);
        return discovery.Humans;
    }

    public Task<ModerationFindingEvidenceReference?> AcquireCurrentResponseAsync(
        Guid caseId, Guid responseId, CancellationToken cancellationToken) =>
        AcquireResponseAsync(null, caseId, responseId, cancellationToken);

    public Task<ModerationFindingEvidenceReference?> AcquireCurrentResponseAsync(
        IModerationFindingProvenanceSession provenance, Guid caseId, Guid responseId, CancellationToken cancellationToken) =>
        provenance is ModerationAuthorityParticipant.FindingProvenanceSession native
            ? AcquireResponseAsync(native, caseId, responseId, cancellationToken)
            : Task.FromResult<ModerationFindingEvidenceReference?>(null);

    private async Task<ModerationFindingEvidenceReference?> AcquireResponseAsync(
        ModerationAuthorityParticipant.FindingProvenanceSession? provenance,
        Guid caseId, Guid responseId, CancellationToken cancellationToken)
    {
        if (responseProtection is null || caseAttribution is null
            || context.Database.CurrentTransaction is not { } transaction
            || _preFindingDiscovery is not { } discovery || discovery.CaseId != caseId
            || discovery.TransactionId != transaction.TransactionId
            || _preFindingResponse is { } prior && prior.TransactionId == transaction.TransactionId
                && !ReferenceEquals(prior.Provenance, provenance))
            return null;
        try
        {
            var authorityOwner = new ModerationQualificationAuthority(context, admin, machine);
            var current = await ModerationQualificationAuthority
                .DiscoverPreFindingHumansAsync(context, caseId, [], cancellationToken);
            if (current is null || !discovery.Humans.SequenceEqual(current.Humans)) return null;
            var authority = provenance is null
                ? _preFindingResponse is { } original && original.TransactionId == transaction.TransactionId
                    ? original.Authority
                    : await authorityOwner.AcquirePreFindingAsync(caseId, [], cancellationToken,
                        discovered: discovery.Sources)
                : await provenance.AcquireAsync(context, caseId, cancellationToken);
            if (authority is null || discovery.Humans.Any(human => !authority.FencedHumans.Contains(human))) return null;
            var owner = await ReadCaseAsync(caseId, authority.Tenant, null, authority, cancellationToken,
                provenance: provenance);
            _preFindingUntil = DateTimeOffset.MaxValue;
            // This proves native provenance only. It never returns private text or grants the
            // observing actor access to the notice, response or confidential occurrence.
            var reference = await AcquirePreFindingResponseAsync(owner, responseId, authority, cancellationToken,
                requireObserverReview: false);
            if (reference is null || clock.GetUtcNow() >= _preFindingUntil) return null;
            if (_preFindingResponse is { } before && before.TransactionId == transaction.TransactionId
                && (before.CaseId != caseId || before.ResponseId != responseId || before.Reference != reference))
                return null;
            if (_preFindingResponse is { } retained && retained.TransactionId == transaction.TransactionId)
            {
                BoundPreFinding(retained.QualifiedUntil);
                if (clock.GetUtcNow() >= _preFindingUntil) return null;
            }
            _preFindingResponse = new(transaction.TransactionId, caseId, responseId, reference, authority, provenance,
                _preFindingUntil);
            return reference;
        }
        catch (AbortQualification) { return null; }
        catch (ArgumentException) { return null; }
        catch (System.Security.Cryptography.CryptographicException) { return null; }
        catch (System.Text.Json.JsonException) { return null; }
    }

    public async Task<bool> RecheckResponseAsync(CancellationToken cancellationToken)
    {
        if (_preFindingResponse is not { } acquired || context.ChangeTracker.HasChanges()
            || context.Database.CurrentTransaction is not { } transaction
            || transaction.TransactionId != acquired.TransactionId)
            return false;
        try
        {
            if (await AcquireResponseAsync(acquired.Provenance, acquired.CaseId, acquired.ResponseId, cancellationToken)
                != acquired.Reference)
                return false;
            await RequirePreFindingTerminalAsync(_preFindingResponse!.Authority, transaction, cancellationToken,
                acquired.Provenance);
            return clock.GetUtcNow() < _preFindingUntil;
        }
        catch (AbortQualification) { return false; }
    }

    private sealed record PreFindingDiscovery(Guid TransactionId, Guid CaseId,
        ModerationQualificationAuthority.PreFindingHumanDiscovery Sources)
    {
        internal IReadOnlyList<Guid> Humans => Sources.Humans;
    }
    private sealed record PreFindingAcquisition(
        Guid TransactionId, Guid CaseId, Guid ResponseId, ModerationFindingEvidenceReference Reference,
        ModerationSourceAuthority Authority, ModerationAuthorityParticipant.FindingProvenanceSession? Provenance,
        DateTimeOffset QualifiedUntil);
}
