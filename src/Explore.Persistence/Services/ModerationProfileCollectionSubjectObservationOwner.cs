using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Services;

/// <summary>
/// Persists original actual-self nomination under the real privacy-owned serializable identity
/// transaction. Parent authors remain qualified administrators. Observation does not determine
/// applicability or authorize any profile body, consent or independent assessment.
/// </summary>
public sealed class ModerationProfileCollectionSubjectObservationOwner(
    ExploreDbContext context, IAdminContext admin, IMachinePrincipalAccessor machine,
    IHttpContextAccessor http, IPrivacyIdentityFenceAuthority privacy,
    ModerationCapabilityRegistry inventory, TimeProvider clock)
    : IModerationProfileCollectionSubjectObservationOwner
{
    public async Task<ModerationProfileCollectionSubjectObservation?> ObserveAsync(
        NominateModerationProfileCollectionSubjectObservation nomination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nomination);
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Subject observation owns its complete clean identity unit of work.");
        if (!context.Database.IsRelational() || context.IsTenantFilterBypassed
            || context.TenantContext is null || context.TenantFilterTenantId is null
            || machine.IsMachineCaller || http.HttpContext?.User.Identity?.IsAuthenticated != true
            || nomination.ProfileId == Guid.Empty || !Enum.IsDefined(nomination.Mode))
            return null;

        var originalTrust = context.ModerationTrustDomainId;
        ModerationProfileCollectionIndividualAcquisition? acquisition = null;
        ModerationProfileCollectionSubjectObservation? original = null;
        try
        {
            return await privacy.ExecuteEnrollmentSerializedAsync(async token =>
            {
                token.ThrowIfCancellationRequested();
                context.ChangeTracker.Clear();
                acquisition = await ModerationProfileCollectionIndividualAcquisition.AcquireAsync(
                    context, admin, machine, http, privacy, inventory, clock,
                    nomination.ProfileId, nomination.Mode, token);
                if (acquisition is null) throw new ObservationUnavailable();
                await RelationalEntityRowFence.AcquireGlobalAsync<UserPii>(context, acquisition.SubjectId, token);
                if (!await context.Set<UserPii>().AsNoTracking()
                    .AnyAsync(row => row.UserId == acquisition.SubjectId, token))
                    throw new ObservationUnavailable();
                await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context,
                    Slot(acquisition.HostBindingId, acquisition.TenantId, acquisition.SubjectId,
                        acquisition.ProfileId, (int)acquisition.Mode), token);
                var last = await SlotRows(acquisition.HostBindingId, acquisition.TenantId,
                        acquisition.SubjectId, acquisition.ProfileId, (int)acquisition.Mode)
                    .OrderByDescending(row => row.ObservationSequence)
                    .Select(row => (long?)row.ObservationSequence).FirstOrDefaultAsync(token) ?? 0;
                if (last < 0 || last >= long.MaxValue - 1) throw new ObservationUnavailable();
                original = await acquisition.ObserveSubjectAsync(last + 1, token);
                if (original is null) throw new ObservationUnavailable();
                // The immutable procedure was read AsNoTracking. Attach only that existing
                // reference, so adding the observation cannot insert its historical graph.
                context.Attach(original.ProcedureSource);
                context.Set<ModerationProfileCollectionSubjectObservation>().Add(original);
                await context.SaveChangesAsync(token);
                if (!await OriginalCurrentAsync(acquisition, original, token)) throw new ObservationUnavailable();
                return original;
            }, async token =>
            {
                // This is the actual outer beforePrimaryCommit; acquisition is not disposed
                // when the operation callback returns. A nested unit of work is never used.
                token.ThrowIfCancellationRequested();
                if (acquisition is null || original is null
                    || !await OriginalCurrentAsync(acquisition, original, token))
                    throw new ObservationUnavailable();
                token.ThrowIfCancellationRequested();
            }, cancellationToken);
        }
        catch (ObservationUnavailable) { return null; }
        catch (DbUpdateConcurrencyException) { return null; }
        finally
        {
            acquisition?.Dispose();
            context.ChangeTracker.Clear();
            context.ModerationTrustDomainId = originalTrust;
        }
    }

    /// <summary>
    /// Internal metadata read on a real live native acquisition, including a later actor's.
    /// Rereads the original subject, PII, login row, latest slot and current original parents.
    /// It never aliases the actor to the subject or renews the original observation. The
    /// future assessment owner still qualifies its nominated independent actor and must
    /// repeat this read at its real outer commit while retaining that acquisition.
    /// </summary>
    internal async Task<ModerationProfileCollectionSubjectObservation?> ReadCurrentAsync(
        ModerationProfileCollectionIndividualAcquisition acquisition, Guid observationId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!acquisition.OwnsContext(context) || !await acquisition.RevalidateAsync(token)) return null;
        var discovered = await context.Set<ModerationProfileCollectionSubjectObservation>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == observationId, token);
        if (discovered is null || discovered.HostBindingId != acquisition.HostBindingId
            || discovered.TenantId != acquisition.TenantId || discovered.ProfileId != acquisition.ProfileId
            || discovered.ModeCode != (int)acquisition.Mode
            || acquisition.SubjectId != discovered.SubjectHumanId
                && acquisition.SubjectId != discovered.ProcedureSource.OwnerHumanId
                && acquisition.SubjectId != discovered.ProcedureSource.ReviewerHumanId)
            return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<User>(context, discovered.SubjectHumanId, token);
        await RelationalEntityRowFence.AcquireGlobalAsync<UserPii>(context, discovered.SubjectHumanId, token);
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context,
            Slot(discovered.HostBindingId, discovered.TenantId, discovered.SubjectHumanId,
                discovered.ProfileId, discovered.ModeCode), token);
        // Select the latest slot identity before qualification. Never search for the latest
        // valid row, which would resurrect an older observation after invalidation.
        var latest = await SlotRows(discovered.HostBindingId, discovered.TenantId, discovered.SubjectHumanId,
                discovered.ProfileId, discovered.ModeCode).OrderByDescending(row => row.ObservationSequence)
            .Select(row => (Guid?)row.Id).FirstOrDefaultAsync(token);
        if (latest != observationId) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationProfileCollectionSubjectObservation>(
            context, observationId, token);
        var original = await context.Set<ModerationProfileCollectionSubjectObservation>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == observationId, token);
        if (original is null) return null;
        try
        {
            original.ValidatePersistedState();
            if (!original.Matches(discovered) || !original.Covers(clock.GetUtcNow())
                || await privacy.IsSubjectFencedAsync(original.SubjectHumanId, token)
                || !await context.Users.AsNoTracking()
                    .AnyAsync(row => row.Id == original.SubjectHumanId && !row.IsDeleted, token)
                || !await context.Set<UserPii>().AsNoTracking()
                    .AnyAsync(row => row.UserId == original.SubjectHumanId, token)
                || original.SubjectLoginId is { } loginId && !await context.UserExternalLogins.AsNoTracking()
                    .AnyAsync(row => row.Id == loginId && row.UserId == original.SubjectHumanId, token)
                || !await acquisition.MatchesSubjectObservationAsync(original, token))
                return null;
        }
        catch (ArgumentException) { return null; }
        token.ThrowIfCancellationRequested();
        return original.Covers(clock.GetUtcNow()) && await acquisition.RevalidateAsync(token) ? original : null;
    }

    private async Task<bool> OriginalCurrentAsync(ModerationProfileCollectionIndividualAcquisition acquisition,
        ModerationProfileCollectionSubjectObservation original, CancellationToken token)
    {
        var current = await ReadCurrentAsync(acquisition, original.Id, token);
        return current is not null && current.Matches(original);
    }

    private IQueryable<ModerationProfileCollectionSubjectObservation> SlotRows(
        Guid host, Guid tenant, Guid human, Guid profile, int mode) =>
        context.Set<ModerationProfileCollectionSubjectObservation>().AsNoTracking().Where(row =>
            row.HostBindingId == host && row.TenantId == tenant && row.SubjectHumanId == human
            && row.ProfileId == profile && row.ModeCode == mode);

    private static string Slot(Guid host, Guid tenant, Guid human, Guid profile, int mode) =>
        $"moderation.profile-subject-observation:{host:N}:{tenant:N}:{human:N}:{profile:N}:{mode}";

    private sealed class ObservationUnavailable : Exception;
}
