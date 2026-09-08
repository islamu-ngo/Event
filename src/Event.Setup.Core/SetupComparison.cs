namespace ISLAMU.Event.Setup.Core;

using System.Collections.ObjectModel;

public sealed record SetupDiffInput
{
    public SetupDiffInput(
        IReadOnlyDictionary<PortableSectionKey, ArtifactDigest> baseline,
        IReadOnlyDictionary<PortableSectionKey, ArtifactDigest> candidate)
    {
        Baseline = Snapshot(baseline, nameof(baseline));
        Candidate = Snapshot(candidate, nameof(candidate));
    }

    public IReadOnlyDictionary<PortableSectionKey, ArtifactDigest> Baseline { get; }
    public IReadOnlyDictionary<PortableSectionKey, ArtifactDigest> Candidate { get; }

    private static ReadOnlyDictionary<PortableSectionKey, ArtifactDigest> Snapshot(
        IReadOnlyDictionary<PortableSectionKey, ArtifactDigest> values,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        if (values.Keys.Any(static key => key is null))
            throw new ArgumentException("Section keys cannot be null.", parameterName);
        return new ReadOnlyDictionary<PortableSectionKey, ArtifactDigest>(
            new Dictionary<PortableSectionKey, ArtifactDigest>(values));
    }
}

public sealed record SetupDiffResult
{
    internal SetupDiffResult(
        IReadOnlyList<PortableSectionKey> added,
        IReadOnlyList<PortableSectionKey> removed,
        IReadOnlyList<PortableSectionKey> changed,
        IReadOnlyList<PortableSectionKey> unchanged)
    {
        Added = added;
        Removed = removed;
        Changed = changed;
        Unchanged = unchanged;
    }

    public IReadOnlyList<PortableSectionKey> Added { get; }
    public IReadOnlyList<PortableSectionKey> Removed { get; }
    public IReadOnlyList<PortableSectionKey> Changed { get; }
    public IReadOnlyList<PortableSectionKey> Unchanged { get; }
}

public static class SetupDiff
{
    public static SetupDiffResult Compare(SetupDiffInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        PortableSectionKey[] added = Ordered(input.Candidate.Keys.Except(input.Baseline.Keys));
        PortableSectionKey[] removed = Ordered(input.Baseline.Keys.Except(input.Candidate.Keys));
        PortableSectionKey[] shared = Ordered(input.Baseline.Keys.Intersect(input.Candidate.Keys));
        PortableSectionKey[] changed = shared
            .Where(key => input.Baseline[key] != input.Candidate[key])
            .ToArray();
        PortableSectionKey[] unchanged = shared
            .Where(key => input.Baseline[key] == input.Candidate[key])
            .ToArray();
        return new SetupDiffResult(
            Array.AsReadOnly(added), Array.AsReadOnly(removed),
            Array.AsReadOnly(changed), Array.AsReadOnly(unchanged));
    }

    private static PortableSectionKey[] Ordered(IEnumerable<PortableSectionKey> values) =>
        values.OrderBy(static key => key.Value, StringComparer.Ordinal).ToArray();
}

public sealed record SetupCoverageInput
{
    public SetupCoverageInput(
        IEnumerable<PortableSectionKey> required,
        IEnumerable<PortableSectionKey> present)
    {
        Required = SetupSnapshot.OrderedDistinct(required, static item => item.Value, nameof(required));
        Present = SetupSnapshot.OrderedDistinct(present, static item => item.Value, nameof(present));
    }

    public IReadOnlyList<PortableSectionKey> Required { get; }
    public IReadOnlyList<PortableSectionKey> Present { get; }
}

public sealed record SetupCoverageResult
{
    internal SetupCoverageResult(
        IReadOnlyList<PortableSectionKey> covered,
        IReadOnlyList<PortableSectionKey> missing)
    {
        Covered = covered;
        Missing = missing;
    }

    public IReadOnlyList<PortableSectionKey> Covered { get; }
    public IReadOnlyList<PortableSectionKey> Missing { get; }
    public bool IsComplete => Missing.Count == 0;
}

public static class SetupCoverage
{
    public static SetupCoverageResult Calculate(SetupCoverageInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        HashSet<PortableSectionKey> present = input.Present.ToHashSet();
        PortableSectionKey[] covered = input.Required.Where(present.Contains).ToArray();
        PortableSectionKey[] missing = input.Required.Where(item => !present.Contains(item)).ToArray();
        return new SetupCoverageResult(Array.AsReadOnly(covered), Array.AsReadOnly(missing));
    }
}
