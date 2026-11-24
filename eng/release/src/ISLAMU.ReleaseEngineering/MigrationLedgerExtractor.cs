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

    public string Family => "migrations";

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

        var changes = new List<ContractDiffChange>();
        var addedMigrations = new List<string>();
        var removedMigrations = new List<string>();
        var changedContexts = new HashSet<string>(StringComparer.Ordinal);

        foreach (string snapshotPath in baseBySnapshot.Keys.Union(headBySnapshot.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            bool hasBase = baseBySnapshot.TryGetValue(snapshotPath, out MigrationContextCoverage? baseEntry);
            bool hasHead = headBySnapshot.TryGetValue(snapshotPath, out MigrationContextCoverage? headEntry);

            if (hasBase && !hasHead)
            {
                changedContexts.Add(baseEntry!.Context);
                changes.Add(new ContractDiffChange(
                    Family,
                    ContractChangeSeverity.Breaking,
                    "migration_snapshot_removed",
                    $"{baseEntry.Context}:{baseEntry.Provider}",
                    $"Removed EF model snapshot for {baseEntry.Context} ({baseEntry.Provider}).",
                    snapshotPath));
                foreach (string removed in baseEntry.MigrationIds)
                {
                    string scopedId = $"{baseEntry.Context}/{baseEntry.Provider}/{removed}";
                    removedMigrations.Add(scopedId);
                    changes.Add(new ContractDiffChange(
                        Family,
                        ContractChangeSeverity.Breaking,
                        "migration_removed",
                        scopedId,
                        $"Removed EF migration '{removed}' from {baseEntry.Context} ({baseEntry.Provider}).",
                        snapshotPath));
                }

                continue;
            }

            if (!hasBase && hasHead)
            {
                changedContexts.Add(headEntry!.Context);
                changes.Add(new ContractDiffChange(
                    Family,
                    ContractChangeSeverity.ReviewRequired,
                    "migration_snapshot_added",
                    $"{headEntry.Context}:{headEntry.Provider}",
                    $"Added EF model snapshot for {headEntry.Context} ({headEntry.Provider}); verify provider deployment runbooks.",
                    snapshotPath));
                foreach (string added in headEntry.MigrationIds)
                {
                    string scopedId = $"{headEntry.Context}/{headEntry.Provider}/{added}";
                    addedMigrations.Add(scopedId);
                    changes.Add(new ContractDiffChange(
                        Family,
                        ContractChangeSeverity.Additive,
                        "migration_added",
                        scopedId,
                        $"Added EF migration '{added}' for {headEntry.Context} ({headEntry.Provider}).",
                        snapshotPath));
                }

                continue;
            }

            HashSet<string> baseIds = baseEntry!.MigrationIds.ToHashSet(StringComparer.Ordinal);
            HashSet<string> headIds = headEntry!.MigrationIds.ToHashSet(StringComparer.Ordinal);
            string[] addedHere = headIds.Except(baseIds, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            string[] removedHere = baseIds.Except(headIds, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

            foreach (string added in addedHere)
            {
                changedContexts.Add(headEntry.Context);
                string scopedId = $"{headEntry.Context}/{headEntry.Provider}/{added}";
                addedMigrations.Add(scopedId);
                changes.Add(new ContractDiffChange(
                    Family,
                    ContractChangeSeverity.Additive,
                    "migration_added",
                    scopedId,
                    $"Added EF migration '{added}' for {headEntry.Context} ({headEntry.Provider}).",
                    snapshotPath));
            }

            foreach (string removed in removedHere)
            {
                changedContexts.Add(headEntry.Context);
                string scopedId = $"{headEntry.Context}/{headEntry.Provider}/{removed}";
                removedMigrations.Add(scopedId);
                changes.Add(new ContractDiffChange(
                    Family,
                    ContractChangeSeverity.Breaking,
                    "migration_removed",
                    scopedId,
                    $"Removed EF migration '{removed}' from {headEntry.Context} ({headEntry.Provider}).",
                    snapshotPath));
            }

            if (addedHere.Length == 0 && removedHere.Length == 0)
            {
                GitBlobReadResult baseSnap = reader.ReadBlob(baseOid, snapshotPath);
                GitBlobReadResult headSnap = reader.ReadBlob(headOid, snapshotPath);
                if (baseSnap.Diagnostic is not null || headSnap.Diagnostic is not null)
                {
                    return new ContractDifferResult(false, Family, [], Diagnostic: baseSnap.Diagnostic ?? headSnap.Diagnostic);
                }

                if (baseSnap.Bytes is not null && headSnap.Bytes is not null &&
                    !baseSnap.Bytes.AsSpan().SequenceEqual(headSnap.Bytes))
                {
                    changedContexts.Add(headEntry.Context);
                    changes.Add(new ContractDiffChange(
                        Family,
                        ContractChangeSeverity.ReviewRequired,
                        "migration_snapshot_modified_without_migration",
                        $"{headEntry.Context}:{headEntry.Provider}",
                        $"Model snapshot '{snapshotPath}' changed without an added or removed migration file; verify EF model consistency.",
                        snapshotPath));
                }
            }
        }

        // Detect asymmetric provider migration coverage per context (evidence requiring review, never a parity proof).
        var asymmetricContexts = new List<string>();
        foreach (IGrouping<string, MigrationContextCoverage> group in headCoverage.GroupBy(c => c.Context, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            MigrationContextCoverage[] providers = group.OrderBy(c => c.Provider, StringComparer.Ordinal).ToArray();
            if (providers.Length < 2)
            {
                continue;
            }

            HashSet<string>[] logicalSets = providers
                .Select(provider => provider.MigrationIds.Select(ExtractLogicalMigrationName).ToHashSet(StringComparer.Ordinal))
                .ToArray();

            bool symmetric = logicalSets.Skip(1).All(set => set.SetEquals(logicalSets[0]));
            if (!symmetric && changedContexts.Contains(group.Key))
            {
                asymmetricContexts.Add(group.Key);
                string providerSummary = string.Join(
                    ", ",
                    providers.Select(provider => $"{provider.Provider} ({provider.MigrationIds.Count})"));
                changes.Add(new ContractDiffChange(
                    Family,
                    ContractChangeSeverity.ReviewRequired,
                    "migration_provider_asymmetry",
                    group.Key,
                    $"Asymmetric migration coverage detected for {group.Key} across providers [{providerSummary}]; filename inventory requires human and EF provider verification.",
                    providers[0].SnapshotPath));
            }
        }

        var ledger = new MigrationLedgerSummary(
            BaseCoverage: baseCoverage,
            HeadCoverage: headCoverage,
            AddedMigrations: addedMigrations.Order(StringComparer.Ordinal).ToArray(),
            RemovedMigrations: removedMigrations.Order(StringComparer.Ordinal).ToArray(),
            AsymmetricContexts: asymmetricContexts);

        return new ContractDifferResult(true, Family, changes, MigrationLedger: ledger);
    }

    private static IReadOnlyList<MigrationContextCoverage> DiscoverCoverage(IReadOnlyList<string> srcPaths)
    {
        string[] snapshotPaths = srcPaths
            .Where(path => path.EndsWith(SnapshotSuffix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        var results = new List<MigrationContextCoverage>(snapshotPaths.Length);
        foreach (string snapshotPath in snapshotPaths)
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

            results.Add(new MigrationContextCoverage(context, provider, snapshotPath, migrationIds));
        }

        return results
            .OrderBy(item => item.Context, StringComparer.Ordinal)
            .ThenBy(item => item.Provider, StringComparer.Ordinal)
            .ThenBy(item => item.SnapshotPath, StringComparer.Ordinal)
            .ToArray();
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

        if (string.Equals(assembly, "Explore.Persistence", StringComparison.Ordinal))
        {
            return "postgresql";
        }

        return assembly.ToLowerInvariant();
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
