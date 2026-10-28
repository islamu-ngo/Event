using ISLAMU.Moderation.Domain;

namespace Explore.Persistence.Repositories;

internal sealed partial class ModerationQualificationAuthority
{
    /// <summary>
    /// Existing-Human ProfileCompletion evidence only. The stored selected publication
    /// discovers its preparer, parent owners and controllers before subordinate fences.
    /// Administrative source writers retain the public administrator-only acquisition.
    /// </summary>
    internal Task<ModerationSourceAuthority?> AcquireProfileCollectionIndividualAsync(
        AuthorityScope jurisdiction, Guid publicationId, CancellationToken token) =>
        !context.IdentityFenceOwnsTransaction || publicationId == Guid.Empty
            ? Task.FromResult<ModerationSourceAuthority?>(null)
            : AcquireAsync(jurisdiction, false, false, null, null, token, publicationId);
}
