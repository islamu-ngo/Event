using System.Text.RegularExpressions;

namespace ISLAMU.ReleaseEngineering;

/// <summary>
/// Dynamically discovers EF Core <c>*ModelSnapshot.cs</c> files and sibling migration files at
/// both pinned revisions (excluding <c>*.Designer.cs</c> and snapshots from migration IDs).
/// Reports additions, removals, snapshot-only edits, and asymmetric provider coverage as review
/// evidence without claiming provider parity or runtime migration safety.
/// </summary>
public sealed class MigrationLedgerExtractor : IReleaseContractDiffer
{
    private const string SnapshotSuffix = "ModelSnapshot.cs";
    private const string DesignerSuffix = ".Designer.cs";
    private static readonly Regex TimestampPrefixPattern = new(
        "^[0-9]{14}_(?<name>.+)$",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromMilliseconds(100));

    private sealed class LedgerAccumulation
    {
        public List<ContractDiffChange> Changes { get; } = [];
        public List<string> AddedMigrations { get; } = [];
        public List<string> RemovedMigrations { get; } = [];
        public HashSet<string> ChangedContexts { get; } = new(StringComparer.Ordinal);
    }

    public string Family => ContractFamilies.Migrations;

    public int Order => 30;

    public ContractDifferResult Diff(IGitObjectReader reader, string baseOid, string headOid)
    {
        ArgumentNullException.ThrowIfNull(reader);

        GitTreeListResult baseTree = reader.ListFiles(baseOid, "src");
        if (!baseTree.IsValid)
        {
            return new ContractDifferResult(false, Family, [], Diagnostic: baseTree.Diagnostic);
        }

        GitTreeListResult headTree = reader.ListFiles(headOid, "src");
        if (!headTree.IsValid)
        {
            return new ContractDifferResult(false, Family, [], Diagnostic: headTree.Diagnostic);
        }

        IReadOnlyList<MigrationContextCoverage> baseCoverage = DiscoverCoverage(baseTree.Paths);
        IReadOnlyList<MigrationContextCoverage> headCoverage = DiscoverCoverage(headTree.Paths);
        Dictionary<string, MigrationContextCoverage> baseBySnapshot = baseCoverage.ToDictionary(c => c.SnapshotPath, StringComparer.Ordinal);
        Dictionary<string, MigrationContextCoverage> headBySnapshot = headCoverage.ToDictionary(c => c.SnapshotPath, StringComparer.Ordinal);

        var state = new LedgerAccumulation();
        foreach (string snapshotPath in baseBySnapshot.Keys.Union(headBySnapshot.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            baseBySnapshot.TryGetValue(snapshotPath, out MigrationContextCoverage? baseEntry);
            headBySnapshot.TryGetValue(snapshotPath, out MigrationContextCoverage? headEntry);
            string? diagnostic = DiffSnapshotCoverage(reader, baseOid, headOid, snapshotPath, baseEntry, headEntry, state);
            if (diagnostic is not null)
            {
                return new ContractDifferResult(false, Family, [], Diagnostic: diagnostic);
            }
        }

        List<string> asymmetricContexts = DetectAsymmetricProviders(headCoverage, state);
        var ledger = new MigrationLedgerSummary(
            BaseCoverage: baseCoverage,
            HeadCoverage: headCoverage,
            AddedMigrations: state.AddedMigrations.Order(StringComparer.Ordinal).ToArray(),
            RemovedMigrations: state.RemovedMigrations.Order(StringComparer.Ordinal).ToArray(),
            AsymmetricContexts: asymmetricContexts);

        return new ContractDifferResult(true, Family, state.Changes, MigrationLedger: ledger);
    }

    private string? DiffSnapshotCoverage(
        IGitObjectReader reader,
        string baseOid,
        string headOid,
        string snapshotPath,
        MigrationContextCoverage? baseEntry,
        MigrationContextCoverage? headEntry,
        LedgerAccumulation state)
    {
        var collector = new ContractChangeCollector(Family, snapshotPath, state.Changes);
        if (baseEntry is not null && headEntry is null)
        {
            RecordRemovedSnapshot(baseEntry, collector, state);
            return null;
        }

        if (baseEntry is null && headEntry is not null)
        {
            RecordAddedSnapshot(headEntry, collector, state);
            return null;
        }

        return DiffMatchedSnapshot(reader, baseOid, headOid, snapshotPath, baseEntry!, headEntry!, collector, state);
    }

    private static void RecordRemovedSnapshot(
        MigrationContextCoverage baseEntry,
        ContractChangeCollector collector,
        LedgerAccumulation state)
    {
        state.ChangedContexts.Add(baseEntry.Context);
        collector.Breaking(
            ContractChangeKinds.MigrationSnapshotRemoved,
            $"{baseEntry.Context}:{baseEntry.Provider}",
            $"Removed EF model snapshot for {baseEntry.Context} ({baseEntry.Provider}).");
        RecordRemovedMigrations(baseEntry, baseEntry.MigrationIds, collector, state);
    }

    private static void RecordAddedSnapshot(
        MigrationContextCoverage headEntry,
        ContractChangeCollector collector,
        LedgerAccumulation state)
    {
        state.ChangedContexts.Add(headEntry.Context);
        collector.ReviewRequired(
            ContractChangeKinds.MigrationSnapshotAdded,
            $"{headEntry.Context}:{headEntry.Provider}",
            $"Added EF model snapshot for {headEntry.Context} ({headEntry.Provider}); verify provider deployment runbooks.");
        RecordAddedMigrations(headEntry, headEntry.MigrationIds, collector, state);
    }

    private static string? DiffMatchedSnapshot(
        IGitObjectReader reader,
        string baseOid,
        string headOid,
        string snapshotPath,
        MigrationContextCoverage baseEntry,
        MigrationContextCoverage headEntry,
        ContractChangeCollector collector,
        LedgerAccumulation state)
    {
        HashSet<string> baseIds = baseEntry.MigrationIds.ToHashSet(StringComparer.Ordinal);
        HashSet<string> headIds = headEntry.MigrationIds.ToHashSet(StringComparer.Ordinal);
        string[] addedHere = headIds.Except(baseIds, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        string[] removedHere = baseIds.Except(headIds, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

        if (addedHere.Length > 0)
        {
            state.ChangedContexts.Add(headEntry.Context);
            RecordAddedMigrations(headEntry, addedHere, collector, state);
        }

        if (removedHere.Length > 0)
        {
            state.ChangedContexts.Add(headEntry.Context);
            RecordRemovedMigrations(headEntry, removedHere, collector, state);
        }

        if (addedHere.Length > 0 || removedHere.Length > 0)
        {
            return null;
        }

        return DetectSnapshotOnlyMutation(reader, baseOid, headOid, snapshotPath, headEntry, collector, state);
    }

    private static string? DetectSnapshotOnlyMutation(
        IGitObjectReader reader,
        string baseOid,
        string headOid,
        string snapshotPath,
        MigrationContextCoverage headEntry,
        ContractChangeCollector collector,
        LedgerAccumulation state)
    {
        GitBlobReadResult baseSnap = reader.ReadBlob(baseOid, snapshotPath);
        GitBlobReadResult headSnap = reader.ReadBlob(headOid, snapshotPath);
        if (baseSnap.Diagnostic is not null || headSnap.Diagnostic is not null)
        {
            return baseSnap.Diagnostic ?? headSnap.Diagnostic;
        }

        if (baseSnap.Bytes is not null && headSnap.Bytes is not null &&
            !baseSnap.Bytes.AsSpan().SequenceEqual(headSnap.Bytes))
        {
            state.ChangedContexts.Add(headEntry.Context);
            collector.ReviewRequired(
                ContractChangeKinds.MigrationSnapshotModifiedWithoutMigration,
                $"{headEntry.Context}:{headEntry.Provider}",
                $"Model snapshot '{snapshotPath}' changed without an added or removed migration file; verify EF model consistency.");
        }

        return null;
    }

    private static void RecordAddedMigrations(
        MigrationContextCoverage entry,
        IEnumerable<string> migrationIds,
        ContractChangeCollector collector,
        LedgerAccumulation state)
    {
        foreach (string added in migrationIds)
        {
            string scopedId = $"{entry.Context}/{entry.Provider}/{added}";
            state.AddedMigrations.Add(scopedId);
            collector.Additive(
                ContractChangeKinds.MigrationAdded,
                scopedId,
                $"Added EF migration '{added}' for {entry.Context} ({entry.Provider}).");
        }
    }

    private static void RecordRemovedMigrations(
        MigrationContextCoverage entry,
        IEnumerable<string> migrationIds,
        ContractChangeCollector collector,
        LedgerAccumulation state)
    {
        foreach (string removed in migrationIds)
        {
            string scopedId = $"{entry.Context}/{entry.Provider}/{removed}";
            state.RemovedMigrations.Add(scopedId);
            collector.Breaking(
                ContractChangeKinds.MigrationRemoved,
                scopedId,
                $"Removed EF migration '{removed}' from {entry.Context} ({entry.Provider}).");
        }
    }

    private List<string> DetectAsymmetricProviders(
        IReadOnlyList<MigrationContextCoverage> headCoverage,
        LedgerAccumulation state)
    {
        var asymmetricContexts = new List<string>();
        foreach (IGrouping<string, MigrationContextCoverage> group in headCoverage
            .GroupBy(c => c.Context, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            MigrationContextCoverage[] providers = group.OrderBy(c => c.Provider, StringComparer.Ordinal).ToArray();
            if (providers.Length < 2 || !state.ChangedContexts.Contains(group.Key))
            {
                continue;
            }

            HashSet<string>[] logicalSets = providers
                .Select(provider => provider.MigrationIds.Select(ExtractLogicalMigrationName).ToHashSet(StringComparer.Ordinal))
                .ToArray();
            if (logicalSets.Skip(1).All(set => set.SetEquals(logicalSets[0])))
            {
                continue;
            }

            asymmetricContexts.Add(group.Key);
            string providerSummary = string.Join(", ", providers.Select(provider => $"{provider.Provider} ({provider.MigrationIds.Count})"));
            var collector = new ContractChangeCollector(Family, providers[0].SnapshotPath, state.Changes);
            collector.ReviewRequired(
                ContractChangeKinds.MigrationProviderAsymmetry,
                group.Key,
                $"Asymmetric migration coverage detected for {group.Key} across providers [{providerSummary}]; filename inventory requires human and EF provider verification.");
        }

        return asymmetricContexts;
    }

    private static IReadOnlyList<MigrationContextCoverage> DiscoverCoverage(IReadOnlyList<string> srcPaths)
    {
        string[] snapshotPaths = srcPaths
            .Where(path => path.EndsWith(SnapshotSuffix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        return snapshotPaths
            .Select(snapshotPath => BuildCoverageEntry(snapshotPath, srcPaths))
            .OrderBy(item => item.Context, StringComparer.Ordinal)
            .ThenBy(item => item.Provider, StringComparer.Ordinal)
            .ThenBy(item => item.SnapshotPath, StringComparer.Ordinal)
            .ToArray();
    }

    private static MigrationContextCoverage BuildCoverageEntry(string snapshotPath, IReadOnlyList<string> srcPaths)
    {
        string directory = GetDirectory(snapshotPath);
        string fileName = GetFileName(snapshotPath);
        string context = fileName[..^SnapshotSuffix.Length];
        string provider = InferProvider(snapshotPath);

        string prefix = directory.Length == 0 ? string.Empty : directory + "/";
        string[] migrationIds = srcPaths
            .Where(path =>
                path.StartsWith(prefix, StringComparison.Ordinal) &&
                path.IndexOf('/', prefix.Length) < 0 &&
                path.EndsWith(".cs", StringComparison.Ordinal) &&
                !path.EndsWith(DesignerSuffix, StringComparison.Ordinal) &&
                !path.EndsWith(SnapshotSuffix, StringComparison.Ordinal))
            .Select(path => GetFileName(path)[..^3])
            .Order(StringComparer.Ordinal)
            .ToArray();

        return new MigrationContextCoverage(context, provider, snapshotPath, migrationIds);
    }

    private static string InferProvider(string snapshotPath)
    {
        string[] segments = snapshotPath.Split('/');
        string assembly = segments.Length >= 2 ? segments[1] : snapshotPath;
        if (assembly.EndsWith(".Migrations.MySql", StringComparison.OrdinalIgnoreCase))
        {
            return "mysql";
        }

        if (assembly.EndsWith(".Migrations.SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            return "sqlserver";
        }

        if (assembly.EndsWith(".Migrations.Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            return "sqlite";
        }

        return string.Equals(assembly, "Explore.Persistence", StringComparison.Ordinal)
            ? "postgresql"
            : assembly.ToLowerInvariant();
    }

    private static string ExtractLogicalMigrationName(string migrationId)
    {
        Match match = TimestampPrefixPattern.Match(migrationId);
        return match.Success ? match.Groups["name"].Value : migrationId;
    }

    private static string GetDirectory(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash < 0 ? string.Empty : path[..slash];
    }

    private static string GetFileName(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash < 0 ? path : path[(slash + 1)..];
    }
}
