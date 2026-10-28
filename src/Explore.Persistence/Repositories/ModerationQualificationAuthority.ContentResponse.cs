using ISLAMU.Moderation.Domain;

namespace Explore.Persistence.Repositories;

internal sealed partial class ModerationQualificationAuthority
{
    internal Task<ModerationSourceAuthority?> AcquireContentResponseAsync(
        AuthorityScope tenant, Guid noticeId, ModerationSourceAuthority? acquired, CancellationToken token) =>
        AcquireAsync(tenant, true, false, null, null, token,
            borrowedDisclosureAuthority: acquired, contentResponseNoticeId: noticeId);

    internal Task<ModerationSourceAuthority?> AcquireContentResponseReviewAsync(
        AuthorityScope tenant, Guid caseId, Guid representativeId, CancellationToken token,
        IReadOnlyCollection<Guid>? representatives = null) =>
        AcquireAsync(tenant, true, false, null, null, token,
            responseReview: (caseId, representatives ?? [representativeId]));
}
