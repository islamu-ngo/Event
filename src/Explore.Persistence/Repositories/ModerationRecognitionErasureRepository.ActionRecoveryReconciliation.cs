using System.Data.Common;
using Explore.Application.Authentication;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationRecognitionErasureRepository
{
    public async Task<IReadOnlyList<ModerationRecognitionActionRecovery>> ReadActionRecoveryAfterAsync(
        Guid? afterId, int limit, CancellationToken token)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        return await Rows<ModerationRecognitionActionRecovery>()
            .Where(row => row.ResolvedAt == null && (afterId == null || row.Id.CompareTo(afterId.Value) > 0))
            .OrderBy(row => row.Id).Take(limit).ToListAsync(token);
    }

    public async Task RetryActionRecoveryAsync(Guid recoveryId, CancellationToken token)
    {
        ProviderAccountKey? account = null;
        Guid subjectId = Guid.Empty;
        Guid bindingId = Guid.Empty;
        DateTimeOffset? currentUntil = null;
        async Task BeforeCommit(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (currentUntil is { } until && clock.GetUtcNow() >= until)
                throw new InvalidOperationException("identity_continuity_expired");
            if (account is null) return;
            var current = await Rows<UserExternalLogin>().SingleOrDefaultAsync(row => row.Id == bindingId, ct);
            if (current is null || current.UserId != subjectId
                || current.AuthenticationProviderId != (int)account.ProviderKind
                || !string.Equals(current.ProviderKey, account.Value, StringComparison.Ordinal))
                throw new InvalidOperationException("identity_continuity_binding_changed");
            if (await identity.IsSubjectFencedAsync(subjectId, ct))
                throw new InvalidOperationException("privacy_identity_erased");
        }
        try
        {
            await identity.ExecuteEnrollmentSerializedAsync(async ct =>
            {
                account = null;
                subjectId = Guid.Empty;
                bindingId = Guid.Empty;
                currentUntil = null;
                await using var transaction = await BeginAsync(ct);
                var recovery = await Rows<ModerationRecognitionActionRecovery>()
                    .SingleOrDefaultAsync(row => row.Id == recoveryId, ct);
                if (recovery is null || recovery.ResolvedAt is not null) return false;
                recovery.ValidatePersistedState();
                var item = await Rows<ModerationRecognitionCaptureItem>()
                    .SingleOrDefaultAsync(row => row.Id == recovery.CaptureItemId, ct);
                if (item is null || item.IntentId != recovery.OriginalIntentId) return false;
                item.ValidatePersistedState();
                if (item.ComputeMaterialReceipt() != recovery.CaptureMaterialReceipt) return false;
                if (clock.GetUtcNow() >= recovery.DispositionBy)
                {
                    // The original bound elapsed; no new identifier, key, or deadline is
                    // needed to stop operational matching. Receipt material is untouched.
                    context.Attach(recovery);
                    recovery.Resolve(clock.GetUtcNow());
                }
                else
                {
                    var login = await Rows<UserExternalLogin>()
                        .SingleOrDefaultAsync(row => row.Id == recovery.VerifiedBindingId, ct);
                    if (login is null || login.UserId != recovery.FreshSubjectId
                        || login.AuthenticationProviderId != (int)recovery.ProviderKind
                        || await identity.IsSubjectFencedAsync(recovery.FreshSubjectId, ct))
                        return false;
                    account = new(recovery.ProviderKind, login.ProviderKey);
                    subjectId = recovery.FreshSubjectId;
                    bindingId = recovery.VerifiedBindingId;
                    var result = await MatchAndBindCoreAsync(
                        new(account, subjectId, null) { VerifiedBindingId = bindingId },
                        includeAdmission: false, ct);
                    currentUntil = result.CurrentUntil;
                    if (result.ActionFailure != ModerationRecognitionContinuityFailure.None
                        && !result.ActionRecoveryStaged) return false;
                }
                await context.SaveChangesAsync(ct);
                if (transaction is not null)
                {
                    await context.FlushDisclosureAsync(ct);
                    await BeforeCommit(ct);
                    await transaction.CommitAsync(ct);
                }
                return true;
            }, BeforeCommit, token);
        }
        catch (ContinuityUnavailable) { context.ResetDisclosureMutations(); }
        catch (Exception exception) when (!token.IsCancellationRequested
            && exception is DbException or IOException or TimeoutException or InvalidOperationException or ArgumentException)
        {
            context.ResetDisclosureMutations();
            // Keep the original operational obligation for the next bounded pass.
            // A provider outage never settles a match or creates a replacement ban.
        }
        finally { context.ChangeTracker.Clear(); }
    }
}
