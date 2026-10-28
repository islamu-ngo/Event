using System.Data;
using System.Data.Common;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Features.Moderation.Notices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationNoticeReadRepository : IModerationNoticeCorrectionDisclosure
{
    Task<bool> IModerationNoticeCorrectionDisclosure.DiscloseAsync(ModerationNoticeCorrectionReadRequest request,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> write, CancellationToken cancellationToken) =>
        DiscloseCorrectionAsync(new(request.PendingEffectId, request.ExpectedPendingSnapshotHash,
            request.ExpectedPendingEffectBindingHash, request.ApprovalId, request.GrantId, request.CaseId,
            request.ExpectedCaseRevision, request.OriginalNoticeId), write, cancellationToken);

    /// <summary>
    /// Discloses only approved correction explanation bytes to the current named original Human.
    /// The consumer borrows the buffer inside native authority; output is never replayed or persisted.
    /// </summary>
    internal async Task<bool> DiscloseCorrectionAsync(ModerationNoticeCorrectionPreparationRequest request,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> write, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(write);
        if (context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Correction output requires its own clean native transaction.");
        if (ExecutionStrategy.Current is { RetriesOnFailure: true })
            throw new InvalidOperationException("Correction output cannot run inside a retrying execution strategy.");
        if (!context.Database.IsRelational() || context.ModerationTrustDomainId is not null
            || context.IsTenantFilterBypassed || machine.IsMachineCaller || context.TenantFilterTenantId is not { } tenant
            || tenant == Guid.Empty || request.PendingEffectId == Guid.Empty || request.ApprovalId == Guid.Empty
            || request.OriginalNoticeId == Guid.Empty || !CorrectionHash(request.ExpectedPendingSnapshotHash)
            || !CorrectionHash(request.ExpectedPendingEffectBindingHash)) return false;

        bool entered = false;
        bool deliveryStarted = false;
        byte[]? bytes = null;
        ModerationAdjudicatedCaseReadGrantRepository.ReadAcquisition? permission = null;
        CorrectionPreparation? prepared = null;
        async Task Recheck(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (permission is null || prepared is null || clock.GetUtcNow() >= prepared.Until
                || !await permission.RevalidateAsync(token, prepared.Until) || clock.GetUtcNow() >= prepared.Until
                || await QualifyCorrectionPreparationAsync(request, permission, token, prepared.Until) != prepared
                || !await permission.RevalidateAsync(token, prepared.Until) || clock.GetUtcNow() >= prepared.Until
                || context.ChangeTracker.HasChanges()) throw new CorrectionUnavailable();
            token.ThrowIfCancellationRequested();
        }

        try
        {
            // Establish EF's transaction boundary without allowing provider retries around external output.
            return await new CorrectionDisclosureExecutionStrategy(context).ExecuteAsync(strategyToken =>
                privacy.ExecuteEnrollmentSerializedAsync(async token =>
            {
                if (entered) throw new InvalidOperationException("Correction output cannot replay its native acquisition.");
                entered = true;
                if (context.Database.CurrentTransaction is not null && !context.IdentityFenceOwnsTransaction)
                    throw new CorrectionUnavailable();
                await using IDbContextTransaction? owned = context.IdentityFenceOwnsTransaction ? null
                    : await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                var grants = new ModerationAdjudicatedCaseReadGrantRepository(context, privacy, identity, machine, clock);
                permission = await grants.AcquireCurrentInTransactionAsync(
                    request.GrantId, request.CaseId, request.ExpectedCaseRevision, token) ?? throw new CorrectionUnavailable();
                prepared = await QualifyCorrectionPreparationAsync(request, permission, token, approved: intent =>
                    bytes = JsonSerializer.SerializeToUtf8Bytes(new ModerationCorrectionExplanation(
                        intent.RuleMeaning.Value, intent.ReasonMeaning.Value,
                        intent.Facts.Select(fact => fact.Value).ToImmutableArray(), intent.RemainingActions.Value,
                        intent.Recourse.Value, intent.Automation.Value, intent.Retention.Value)))
                    ?? throw new CorrectionUnavailable();
                if (bytes is null) throw new CorrectionUnavailable();
                await Recheck(token);
                token.ThrowIfCancellationRequested();
                deliveryStarted = true;
                await write(bytes, token);
                await context.FlushDisclosureAsync(token);
                await Recheck(token);
                if (owned is not null) await owned.CommitAsync(token);
                return true;
            }, Recheck, strategyToken), cancellationToken);
        }
        // Once output starts, even settlement failure must propagate: bytes cannot be recalled.
        catch (CorrectionUnavailable) when (!deliveryStarted) { return false; }
        catch (DbException) when (!deliveryStarted) { return false; }
        catch (TimeoutException) when (!deliveryStarted) { return false; }
        catch (JsonException) when (!deliveryStarted) { return false; }
        catch (ArgumentException) when (!deliveryStarted) { return false; }
        catch (InvalidOperationException) when (!deliveryStarted) { return false; }
        finally
        {
            if (bytes is not null) CryptographicOperations.ZeroMemory(bytes);
            context.ChangeTracker.Clear();
            context.ModerationTrustDomainId = null;
        }
    }

    private sealed class CorrectionDisclosureExecutionStrategy(DbContext database)
        : ExecutionStrategy(database, 0, TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => false;
    }

}
