using Explore.Application.Contracts.Persistence;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationQualificationRepository
{
    private static ModerationOriginalEffectAnchor? OriginalMeasuredOperationsAnchor(ModerationQualificationMutation request) =>
        request switch
        {
            RecordModerationMeasuredOperationsSource record => record.OriginalEffect,
            AcceptModerationMeasuredOperationsSource acceptance => acceptance.OriginalEffect,
            _ => null
        };

    /// <summary>
    /// Reacquires the exact retained purpose at the actual primary commit, including an
    /// adopted outer transaction. The dispatcher must serialize original custody first.
    /// </summary>
    private async Task RequireOriginalMeasuredOperationsAtCommitAsync(
        ModerationQualificationMutation request, ModerationSourceAuthority authority, CancellationToken token)
    {
        if (OriginalMeasuredOperationsAnchor(request) is null || context.Database.CurrentTransaction is null)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        using var session = await AcquireMeasuredOperationsWriteAsync(request, authority, clock.GetUtcNow(), token);
        await VerifyMeasuredOperationsWriteAsync(request, session, authority, token);
    }
}
