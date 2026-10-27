using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationStore
{
    private async Task RequireContentCaptureAtCommitAsync(
        Session session, ModerationOperation operation, CancellationToken token)
    {
        var captured = session.OriginalCapture
            ?? throw new AbortOperation(ModerationOperationResult.Unavailable());
        var staged = session.CapturedSource
            ?? throw new AbortOperation(ModerationOperationResult.Unavailable());
        var effect = await context.ModerationEffectIntents.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == operation.EffectId && row.OperationId == operation.OperationId, token);
        var source = await context.ModerationNoticeSources.AsNoTracking()
            .SingleOrDefaultAsync(row => row.EffectId == staged.EffectId, token);
        if (effect is null || source is null || effect.CompletedAt is not null
            || !captured.Matches(effect) || !source.Matches(effect)
            || source.SnapshotHash != staged.SnapshotHash || source.SnapshotJson != staged.SnapshotJson
            || source.RetainUntil != staged.RetainUntil
            || captured.WriterTenantId != context.TenantFilterTenantId
            || !session.CaptureIsCurrentAt(clock.GetUtcNow()))
            throw new AbortOperation(ModerationOperationResult.Unavailable());
        if (captured.HasNoSubstantiveWork) return;

        var grant = session.Grant ?? throw new AbortOperation(ModerationOperationResult.Unavailable());
        var current = await noticeCapture.CaptureInCurrentTransactionAsync(session, effect.Id, grant, token);
        if (current is null || current.SnapshotHash != staged.SnapshotHash || current.SnapshotJson != staged.SnapshotJson
            || current.RetainUntil != staged.RetainUntil
            || !session.CaptureIsCurrentAt(clock.GetUtcNow()))
            throw new AbortOperation(ModerationOperationResult.Unavailable());
    }
}
