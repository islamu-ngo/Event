using System.Linq.Expressions;
using Explore.Application.Contracts.Admissions;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class AdmissionCheckInRepository(
    ExploreDbContext dbContext,
    TimeProvider timeProvider,
    IParticipantAdmissionReadinessAuthority readiness) :
    IAdmissionCheckInTransaction
{
    private const int MaximumDigestCandidates = 8;

    public AdmissionCheckInRepository(
        ExploreDbContext dbContext,
        TimeProvider timeProvider) : this(
        dbContext,
        timeProvider,
        new ParticipantAdmissionEligibilityRepository(dbContext))
    {
    }

    public AdmissionCheckInRepository(ExploreDbContext dbContext)
        : this(
            dbContext,
            TimeProvider.System,
            new ParticipantAdmissionEligibilityRepository(dbContext))
    {
    }

    public async Task<AdmissionCheckInDecision?> ExecuteAsync(
        AdmissionCheckInTransactionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireTransaction();
        ValidateCandidates(request.CredentialDigestCandidates);
        if (request.StaffActorId.HasValue == request.ScannerCapabilityId.HasValue)
        {
            return null;
        }

        AdmissionTicket? resolved = await ResolveCredentialCoreAsync(
            request.TenantId,
            request.CredentialDigestCandidates,
            cancellationToken);
        if (resolved is null || resolved.EventId != request.EventId)
        {
            return null;
        }

        ParticipantAdmissionReadinessDecision?
            readinessDecision =
            await readiness.EvaluateForUpdateAsync(
                request.TenantId,
                resolved.RegistrationTicketAssignmentId,
                orderConfirmed: true,
                paymentSatisfied: true,
                cancellationToken);
        if (readinessDecision?.IsReady != true)
        {
            return null;
        }

        await RelationalEntityRowFence.AcquireAsync<AdmissionTicket>(
            dbContext,
            request.TenantId,
            ticket => ticket.Id,
            resolved.Id,
            cancellationToken);

        AdmissionTicket? ticket = await dbContext.AdmissionTickets
            .SingleOrDefaultAsync(value =>
                value.TenantId == request.TenantId &&
                value.Id == resolved.Id &&
                value.EventId == request.EventId,
                cancellationToken);
        if (ticket is null ||
            ticket.ConcurrencyStamp != resolved.ConcurrencyStamp ||
            ticket.AdmissionTicketStatusId != (int)AdmissionTicketStatusEnum.Active)
        {
            return null;
        }

        await RelationalEntityRowFence.AcquireAsync<AdmissionTarget>(
            dbContext,
            request.TenantId,
            target => target.Id,
            request.TargetId,
            cancellationToken);
        AdmissionTarget? target = await dbContext.AdmissionTargets
            .SingleOrDefaultAsync(value =>
                value.TenantId == request.TenantId &&
                value.EventId == request.EventId &&
                value.Id == request.TargetId,
                cancellationToken);
        if (target is null)
        {
            return null;
        }

        AdmissionScannerCapability? scannerCapability = null;
        if (request.ScannerCapabilityId.HasValue)
        {
            await RelationalEntityRowFence.AcquireAsync<AdmissionScannerCapability>(
                dbContext,
                request.TenantId,
                capability => capability.Id,
                request.ScannerCapabilityId.Value,
                cancellationToken);
            scannerCapability = await dbContext.AdmissionScannerCapabilities
                .SingleOrDefaultAsync(value =>
                    value.TenantId == request.TenantId &&
                    value.Id == request.ScannerCapabilityId.Value,
                    cancellationToken);
            if (scannerCapability is null ||
                scannerCapability.EventId != request.EventId ||
                !scannerCapability.Permits(
                    request.TargetId,
                    ToScannerAction(request.Action),
                    timeProvider.GetUtcNow().UtcDateTime))
            {
                return null;
            }
        }

        AdmissionCheckInPolicy? policy = await dbContext.AdmissionCheckInPolicies
            .SingleOrDefaultAsync(value =>
                value.TenantId == request.TenantId &&
                value.AdmissionTargetId == target.Id,
                cancellationToken);
        TicketTypeEntitlement? entitlement = await dbContext.TicketTypeEntitlements
            .SingleOrDefaultAsync(value =>
                value.TenantId == request.TenantId &&
                value.TicketTypeId == ticket.EventTicketTypeId &&
                value.TargetEventId == request.EventId &&
                value.EntitlementScopeTypeId == target.AdmissionTargetTypeId &&
                value.EventDayId == target.EventDayId &&
                value.EventSessionId == target.EventSessionId,
                cancellationToken);
        if (policy is null || entitlement is null)
        {
            return null;
        }

        AdmissionCheckInState? current = await dbContext.AdmissionCheckInStates
            .SingleOrDefaultAsync(value =>
                value.TenantId == request.TenantId &&
                value.AdmissionTicketId == ticket.Id &&
                value.AdmissionTargetId == target.Id,
                cancellationToken);
        if (current is not null)
        {
            await RelationalEntityRowFence.AcquireAsync<AdmissionCheckInState>(
                dbContext,
                request.TenantId,
                state => state.Id,
                current.Id,
                cancellationToken);
            await dbContext.Entry(current).ReloadAsync(cancellationToken);
        }
        else
        {
            current = AdmissionCheckInState.Create(Guid.CreateVersion7(), ticket, target);
        }

        AdmissionCheckInDecision decision = AdmissionCheckInRules.Decide(
            ticket,
            target,
            entitlement,
            policy,
            current,
            ToDomainAction(request.Action),
            Guid.CreateVersion7(),
            request.StaffActorId,
            scannerCapability?.Id,
            request.ReasonCode,
            request.OccurredAtUtc.UtcDateTime,
            request.CheckInId);
        if (decision.Event is null)
        {
            return decision;
        }

        decision.NextState.ConcurrencyStamp = Guid.CreateVersion7();
        await dbContext.AdmissionCheckInEvents.AddAsync(decision.Event, cancellationToken);
        if (dbContext.Entry(current).State == EntityState.Detached)
        {
            await dbContext.AdmissionCheckInStates.AddAsync(decision.NextState, cancellationToken);
        }
        else
        {
            dbContext.Entry(current).CurrentValues.SetValues(decision.NextState);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        return decision;
    }

    public Task<AdmissionTicket?> ResolveCredentialAsync(
        Guid tenantId,
        IReadOnlyList<(int KeyVersion, string Digest)> candidates,
        CancellationToken cancellationToken)
    {
        AdmissionCheckInCredentialDigestCandidate[] normalized = candidates
            .Select(candidate => new AdmissionCheckInCredentialDigestCandidate(
                candidate.Digest,
                candidate.KeyVersion))
            .ToArray();
        ValidateCandidates(normalized);
        return ResolveCredentialCoreAsync(tenantId, normalized, cancellationToken);
    }

    private async Task<AdmissionTicket?> ResolveCredentialCoreAsync(
        Guid tenantId,
        IReadOnlyList<AdmissionCheckInCredentialDigestCandidate> candidates,
        CancellationToken cancellationToken)
    {
        Expression<Func<AdmissionTicketCredential, bool>> predicate =
            BuildCredentialPredicate(tenantId, candidates);
        return await (
                from credential in dbContext.AdmissionTicketCredentials.AsNoTracking().Where(predicate)
                join ticket in dbContext.AdmissionTickets.AsNoTracking()
                    on new { credential.TenantId, Id = credential.AdmissionTicketId }
                    equals new { ticket.TenantId, ticket.Id }
                select ticket)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static Expression<Func<AdmissionTicketCredential, bool>> BuildCredentialPredicate(
        Guid tenantId,
        IReadOnlyList<AdmissionCheckInCredentialDigestCandidate> candidates)
    {
        ParameterExpression credential = Expression.Parameter(
            typeof(AdmissionTicketCredential),
            "credential");
        Expression candidateMatch = Expression.Constant(false);
        foreach (AdmissionCheckInCredentialDigestCandidate candidate in candidates)
        {
            Expression keyMatch = Expression.Equal(
                Expression.Property(credential, nameof(AdmissionTicketCredential.LookupKeyVersion)),
                Expression.Constant(candidate.KeyVersion));
            Expression digestMatch = Expression.Equal(
                Expression.Property(credential, nameof(AdmissionTicketCredential.LookupDigest)),
                Expression.Constant(candidate.LookupDigest));
            candidateMatch = Expression.OrElse(
                candidateMatch,
                Expression.AndAlso(keyMatch, digestMatch));
        }

        Expression tenantMatch = Expression.Equal(
            Expression.Property(credential, nameof(AdmissionTicketCredential.TenantId)),
            Expression.Constant(tenantId));
        Expression activeMatch = Expression.Equal(
            Expression.Property(
                credential,
                nameof(AdmissionTicketCredential.AdmissionTicketCredentialStatusId)),
            Expression.Constant((int)AdmissionTicketCredentialStatusEnum.Active));
        return Expression.Lambda<Func<AdmissionTicketCredential, bool>>(
            Expression.AndAlso(
                Expression.AndAlso(tenantMatch, activeMatch),
                candidateMatch),
            credential);
    }

    private void RequireTransaction()
    {
        if (dbContext.Database.IsRelational() && dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Admission check-in persistence requires an active unit-of-work transaction.");
        }
    }

    private static void ValidateCandidates(
        IReadOnlyList<AdmissionCheckInCredentialDigestCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count is < 1 or > MaximumDigestCandidates ||
            candidates.Any(candidate =>
                candidate.KeyVersion < 1 ||
                string.IsNullOrWhiteSpace(candidate.LookupDigest) ||
                candidate.LookupDigest.Length > 256) ||
            candidates.Select(candidate => (candidate.KeyVersion, candidate.LookupDigest))
                .Distinct()
                .Count() != candidates.Count)
        {
            throw new ArgumentException(
                "Credential lookup candidates must be unique and bounded.",
                nameof(candidates));
        }
    }

    private static AdmissionCheckInActionEnum ToDomainAction(AdmissionCheckInAction action) => action switch
    {
        AdmissionCheckInAction.CheckIn => AdmissionCheckInActionEnum.CheckIn,
        AdmissionCheckInAction.Undo => AdmissionCheckInActionEnum.Undo,
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    private static AdmissionScannerCapabilityAction ToScannerAction(AdmissionCheckInAction action) => action switch
    {
        AdmissionCheckInAction.CheckIn => AdmissionScannerCapabilityAction.CheckIn,
        AdmissionCheckInAction.Undo => AdmissionScannerCapabilityAction.Undo,
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };
}
