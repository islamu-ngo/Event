using System.Data;
using System.Security.Cryptography;
using Explore.Application.Contracts.Moderation;
using Explore.Domain;
using Explore.Persistence.Database.ProviderPrimitives;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationCaseTargetProofOwner
{
    private ProtectedReadPlan? _protectedReadPlan;

    public async Task<ModerationCaseReadSourcePlan?> DiscoverReadSourcesAsync(
        ModerationCaseAccessRequest request, CancellationToken cancellationToken)
    {
        ResetExpiredTransactionState();
        var transaction = context.Database.CurrentTransaction;
        if (_protectedReadPlan is not null || _proof is not null || _pending is not null
            || request.Purpose != ModerationCaseAccessPurpose.ReadAllegation || request.OperationId != Guid.Empty
            || request.CaseId == Guid.Empty || request.ExpectedCaseRevision < 1 || !request.Narrative.IsEmpty
            || request.PresentedToken.Length != 32 || context.IsTenantFilterBypassed
            || !context.Database.IsRelational() || transaction is null
            || transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable
            || !caller.IsAuthenticated || caller.UserId is not { } userId || machine.Current is not null
            || context.TenantFilterTenantId is not { } tenantId || tenantId == Guid.Empty)
            return null;
        var tokenDigest = Convert.ToHexStringLower(SHA256.HashData(request.PresentedToken.Span));
        var discovered = await EventAuthorityProviderOperations.ReadWithoutRetainingLocksAsync(context, async planning =>
        {
            var host = await planning.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (host is null || planning.ModerationTrustDomainId is { } trust && trust != host.TrustDomainId) return null;
            var previousTrust = planning.ModerationTrustDomainId;
            try
            {
                planning.ModerationTrustDomainId = host.TrustDomainId;
                var receipt = await planning.ModerationCaseTargetReceipts.AsNoTracking().SingleOrDefaultAsync(row =>
                    row.Id == request.ReceiptId && row.CallerUserId == userId && row.TenantId == tenantId
                    && row.HostBindingId == host.Id, cancellationToken);
                if (receipt is null || receipt.RelationshipKind != ModerationCaseRelationshipKind.CaseReadGrant) return null;
                receipt.ValidatePersistedState();
                var grant = await planning.ModerationCaseReadGrants.AsNoTracking().SingleOrDefaultAsync(row =>
                    row.Id == receipt.RelationshipSourceId && row.CallerUserId == userId && row.TenantId == tenantId
                    && row.CaseId == request.CaseId && row.CaseRevision == request.ExpectedCaseRevision
                    && row.SourceId == receipt.SourceId, cancellationToken);
                if (grant is null) return null;
                var source = await planning.ModerationCaseTargetSources.AsNoTracking().SingleOrDefaultAsync(row =>
                    row.Id == grant.SourceId && row.TenantId == tenantId, cancellationToken);
                if (source is null) return null;
                EventActorPlan? parents = null;
                if (source.NativeKind == ModerationCaseTargetKind.EventVersion
                    || source.EventId is not null
                    && source.NativeKind is (ModerationCaseTargetKind.Actor or ModerationCaseTargetKind.Service))
                {
                    parents = await planning.Events.AsNoTracking().Where(row => row.Id == source.EventId && row.TenantId == tenantId)
                        .Select(row => new EventActorPlan(row.Id, row.ActorId, row.OrganizerActorId))
                        .SingleOrDefaultAsync(cancellationToken);
                    if (parents is null) return null;
                }
                var scope = AuthorityScope.Tenant(AuthorityScope.Instance(AuthorityScope.Solution(
                    AuthorityScope.TrustDomain(host.TrustDomainId), host.SolutionId), host.InstanceId), tenantId);
                var coordinates = new ModerationCaseReadSourcePlan(transaction.TransactionId, userId, receipt.Id,
                    host.Id, host.MaintenanceServicePrincipalId, scope, receipt.TargetKind,
                    grant.IssuerUserId, grant.IssuerRoleGrantId, grant.IssuerPlatformGrant,
                    parents?.EventId, parents?.ActorId, parents?.OrganizerActorId);
                return new ProtectedReadPlan(coordinates, request.CaseId, request.ExpectedCaseRevision,
                    tokenDigest, receipt, new(ReadHostCoordinates.From(host), ReadGrantCoordinates.From(grant),
                        ReadSourceCoordinates.From(source), parents));
            }
            finally { planning.ModerationTrustDomainId = previousTrust; }
        }, cancellationToken);
        if (discovered is null || !ReferenceEquals(transaction, context.Database.CurrentTransaction)
            || !caller.IsAuthenticated || caller.UserId != userId || machine.Current is not null
            || context.TenantFilterTenantId != tenantId || context.IsTenantFilterBypassed) return null;
        _protectedReadPlan = discovered;
        return discovered.Coordinates;
    }

    private bool ProtectedReadRequestMatches(ModerationCaseAccessRequest request, ModerationCaseTargetReceipt receipt) =>
        _protectedReadPlan is { } held && held.Coordinates.TransactionId == context.Database.CurrentTransaction?.TransactionId
        && held.Coordinates.CallerUserId == caller.UserId && held.Coordinates.Scope.Id == context.TenantFilterTenantId
        && request.Purpose == ModerationCaseAccessPurpose.ReadAllegation && request.OperationId == Guid.Empty
        && request.CaseId == held.CaseId && request.ExpectedCaseRevision == held.CaseRevision
        && request.ReceiptId == held.Coordinates.ReceiptId && request.PresentedToken.Length == 32
        && Convert.ToHexStringLower(SHA256.HashData(request.PresentedToken.Span)) == held.PresentedDigest
        && SameReadReceipt(receipt, held.Receipt);

    private async Task<bool> ProtectedReadCoordinatesMatchAsync(CancellationToken token)
    {
        if (_protectedReadPlan is not { } held || held.Coordinates.TransactionId != context.Database.CurrentTransaction?.TransactionId
            || held.Coordinates.CallerUserId != caller.UserId || held.Coordinates.Scope.Id != context.TenantFilterTenantId)
            return false;
        var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token);
        if (host is null || ReadHostCoordinates.From(host) != held.Sources.Host) return false;
        var receipt = await context.ModerationCaseTargetReceipts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == held.Coordinates.ReceiptId, token);
        if (receipt is null || !SameReadReceipt(receipt, held.Receipt)) return false;
        var grant = await context.ModerationCaseReadGrants.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == held.Sources.Grant.Id, token);
        if (grant is null || ReadGrantCoordinates.From(grant) != held.Sources.Grant) return false;
        var source = await context.ModerationCaseTargetSources.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == held.Sources.Source.Id, token);
        return source is not null && ReadSourceCoordinates.From(source) == held.Sources.Source
            && (held.Sources.Event is null || await ReadEventActorPlanAsync(held.Sources.Event.EventId, token) == held.Sources.Event);
    }

    internal static bool SameReadReceipt(ModerationCaseTargetReceipt current, ModerationCaseTargetReceipt original) =>
        current.Id == original.Id && current.TenantId == original.TenantId && current.CallerUserId == original.CallerUserId
        && current.HostBindingId == original.HostBindingId && current.QualificationEpochId == original.QualificationEpochId
        && current.QualificationRevision == original.QualificationRevision
        && current.RelationshipSourceId == original.RelationshipSourceId && current.RelationshipKind == original.RelationshipKind
        && current.RelationshipRevision == original.RelationshipRevision
        && current.RelationshipConcurrencyStamp == original.RelationshipConcurrencyStamp
        && current.RelationshipFactsDigest == original.RelationshipFactsDigest
        && current.LifetimePolicyId == original.LifetimePolicyId && current.LifetimePolicyRevision == original.LifetimePolicyRevision
        && current.TokenDigest == original.TokenDigest && current.Evidence == original.Evidence
        && current.IssuedAt == original.IssuedAt && current.ExpiresAt == original.ExpiresAt
        && current.ConsumedCaseId == original.ConsumedCaseId && current.ConsumedOperationId == original.ConsumedOperationId
        && current.ConsumedAt == original.ConsumedAt && current.Revision == original.Revision;

    private sealed record ProtectedReadPlan(ModerationCaseReadSourcePlan Coordinates, Guid CaseId, long CaseRevision,
        string PresentedDigest, ModerationCaseTargetReceipt Receipt, ReadIssuancePlan Sources)
    {
        public override string ToString() => nameof(ProtectedReadPlan);
    }
}
