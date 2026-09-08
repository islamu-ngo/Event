namespace Explore.Application.Contracts.Infrastructure;

/// <summary>
/// Result of an archive change probe.
/// <para>
/// <see cref="IsConclusive"/> is the important half. The probe answers "which of these repositories have
/// sealed evidence of calendar activity after this cursor", and it is only allowed to say so when the
/// archive genuinely covers the requested range. Whenever it cannot know — archive unavailable, cursor
/// newer than the sealed tip, or too much data to scan within budget — it reports inconclusive and
/// recovery falls back to its full scope. Callers must never read an inconclusive result as "no changes".
/// </para>
/// </summary>
public sealed record AtprotoArchiveChangeScope(bool IsConclusive, IReadOnlyList<string> ChangedDids)
{
    /// <summary>The archive could not answer; the caller must assume every repository may have changed.</summary>
    public static AtprotoArchiveChangeScope Inconclusive { get; } = new(false, []);

    /// <summary>The archive covers the range and holds no matching activity for any requested repository.</summary>
    public static AtprotoArchiveChangeScope NoChanges { get; } = new(true, []);
}

public interface IAtprotoFederationArchiveProbe
{
    Task<AtprotoArchiveChangeScope> ResolveChangedDidsAsync(
        long afterSeq,
        IReadOnlyList<string> dids,
        CancellationToken cancellationToken);
}
