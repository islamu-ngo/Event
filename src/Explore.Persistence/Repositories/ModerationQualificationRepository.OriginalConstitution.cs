using Explore.Application.Contracts.Persistence;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationQualificationRepository
{
    private static ModerationOriginalEffectAnchor? OriginalConstitutionAnchor(ModerationQualificationMutation request) =>
        request switch
        {
            RecordModerationConstitutionSource record => record.OriginalEffect,
            AdoptModerationConstitutionSource adoption => adoption.OriginalEffect,
            _ => null
        };

    /// <summary>
    /// Invoked by the retained-custody dispatcher at the actual primary commit, including
    /// an adopted outer transaction. Reacquires the same purpose, never a selected replacement.
    /// </summary>
    private async Task RequireOriginalConstitutionAtCommitAsync(
        ModerationQualificationMutation request, ModerationSourceAuthority authority, CancellationToken token)
    {
        if (OriginalConstitutionAnchor(request) is null || context.Database.CurrentTransaction is null)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        using var session = await AcquireConstitutionWriteAsync(request, authority, clock.GetUtcNow(), token);
        await VerifyConstitutionWriteAsync(request, session, authority, token);
    }
}
