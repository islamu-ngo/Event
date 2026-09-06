// ABOUTME: Identifies only ATProto transient locator and assertion replay uniqueness conflicts.
// ABOUTME: Prevents unrelated persistence failures from being mislabeled as duplicate authentication claims.

using Explore.Domain;
using Microsoft.EntityFrameworkCore;
namespace Explore.Persistence.Database;

internal static class AtprotoTransientUniqueConflictClassifier
{
    internal static bool IsTransientLocatorConflict(ExploreDbContext context, DbUpdateException exception) =>
        IsExpected(context, exception, RelationalConstraintDescriptorResolver.UniqueIndex<AtprotoTransientRecord>(
            context, nameof(AtprotoTransientRecord.Purpose), nameof(AtprotoTransientRecord.TokenDigest)));

    internal static bool IsAssertionReplayConflict(ExploreDbContext context, DbUpdateException exception) =>
        IsExpected(context, exception, RelationalConstraintDescriptorResolver.UniqueIndex<AtprotoTransientAssertionReplay>(
            context, nameof(AtprotoTransientAssertionReplay.AssertionDigest)));

    private static bool IsExpected(ExploreDbContext context, DbUpdateException exception, RelationalConstraintDescriptor expected)
    {
        if (!RegistrationUniqueConflictClassifier.IsProviderUniqueConflict(exception)) return false;
        string message = exception.GetBaseException().Message;
        return RegistrationUniqueConflictClassifier.MatchesQuotedConstraint(message, [expected.Name]) ||
               RegistrationUniqueConflictClassifier.MatchesSqliteColumns(message, [expected.QualifiedColumns]);
    }
}
