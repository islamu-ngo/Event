using Explore.Domain;
using Explore.Domain.Federation;

namespace Explore.Application.Features.Federation.Atproto.Models;

public sealed record AtprotoPdsSnapshot(
    string Did,
    IReadOnlyList<AtprotoPdsSnapshotIdentity> PresentIdentities,
    IReadOnlyList<AtprotoPdsSnapshotItem> Items);

public sealed record AtprotoPdsSnapshotIdentity(
    string Collection,
    string RecordKey);

public sealed record AtprotoPdsSnapshotItem(
    AtprotoRecord Record,
    AtprotoEventProjection? EventProjection);

public sealed record AtprotoPdsSnapshotFetchResult(
    bool IsComplete,
    AtprotoPdsSnapshot? Snapshot,
    string? FailureCode)
{
    public static AtprotoPdsSnapshotFetchResult Complete(AtprotoPdsSnapshot snapshot) =>
        new(true, snapshot, null);

    public static AtprotoPdsSnapshotFetchResult Failed(string failureCode) =>
        new(false, null, failureCode);
}

public enum AtprotoPdsRecoveryMode
{
    DowntimeOnly = 1,
    Full = 2
}

public sealed record AtprotoPdsRecoveryPolicy(
    bool IsEnabled,
    AtprotoPdsRecoveryMode Mode,
    IReadOnlyList<Guid> EffectiveTenantIds,
    string AudienceFingerprint);

public enum AtprotoPdsRecoveryOutcome
{
    Disabled = 1,
    DowntimeOnly = 2,
    ScopeRejected = 3,
    Unchanged = 4,
    Completed = 5,
    PartialFailure = 6,
    FenceRejected = 7
}

public sealed record AtprotoPdsRecoveryResult(
    AtprotoPdsRecoveryOutcome Outcome,
    string Fingerprint,
    int AppliedDids = 0,
    int FailedDids = 0);
