using Explore.Application.Contracts.Notifications;
using Explore.Domain;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationNoticeCaptureRepository
{
    private async Task<ModerationNoticeSource?> ReadPendingCorrectionAsync(ModerationNoticeSource source,
        ModerationNoticeCaptureSnapshot capture, ModerationNoticeCorrectionObligations? original, CancellationToken token)
    {
        if (!ModerationNoticeCorrectionAudience.Same(capture.PendingCorrection, original)
            || source.TenantId != context.TenantFilterTenantId || clock.GetUtcNow() >= original!.RetainUntil)
            return null;
        return await ModerationNoticeCorrectionAudience.PendingOccurrencesMatchAsync(context, source, original, clock, token)
            ? source : null;
    }
}
