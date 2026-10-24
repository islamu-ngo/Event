using ISLAMU.Moderation.Domain;

namespace Explore.Persistence.Repositories;

internal sealed partial class ModerationQualificationAuthority
{
    internal Task<ModerationSourceAuthority?> AcquireAdjudicatedReadAsync(
        AuthorityScope tenant, IReadOnlyList<Guid> humans, CancellationToken token) =>
        AcquireAsync(tenant, false, false, null, null, token, adjudicatedReadHumans: humans);

    internal Task<ModerationSourceAuthority?> AcquireAdjudicatedReadAdministrationAsync(
        AuthorityScope tenant, IReadOnlyList<Guid> humans, CancellationToken token) =>
        AcquireAsync(tenant, false, true, null, null, token, adjudicatedReadHumans: humans);
}
