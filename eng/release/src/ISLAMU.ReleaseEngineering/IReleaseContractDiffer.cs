namespace ISLAMU.ReleaseEngineering;

public static class ContractChangeSeverity
{
    public const string Breaking = "breaking";
    public const string ReviewRequired = "review_required";
    public const string Additive = "additive";

    public static int Rank(string severity) => severity switch
    {
        Breaking => 0,
        ReviewRequired => 1,
        Additive => 2,
        _ => 3,
    };
}

public sealed record ContractDiffChange(
    string Family,
    string Severity,
    string Kind,
    string Target,
    string Summary,
    string? ArtifactPath = null);

public sealed record MigrationContextCoverage(
    string Context,
    string Provider,
    string SnapshotPath,
    IReadOnlyList<string> MigrationIds);

public sealed record MigrationLedgerSummary(
    IReadOnlyList<MigrationContextCoverage> BaseCoverage,
    IReadOnlyList<MigrationContextCoverage> HeadCoverage,
    IReadOnlyList<string> AddedMigrations,
    IReadOnlyList<string> RemovedMigrations,
    IReadOnlyList<string> AsymmetricContexts);

public sealed record ContractDifferResult(
    bool IsValid,
    string Family,
    IReadOnlyList<ContractDiffChange> Changes,
    MigrationLedgerSummary? MigrationLedger = null,
    string? Diagnostic = null);

/// <summary>
/// Conservative contract-comparison strategy for one surface family across two pinned Git commits.
/// Unknown or ambiguous structural changes must emit <see cref="ContractChangeSeverity.ReviewRequired"/>
/// rather than claiming semantic compatibility.
/// </summary>
public interface IReleaseContractDiffer
{
    string Family { get; }

    int Order { get; }

    ContractDifferResult Diff(IGitObjectReader reader, string baseOid, string headOid);
}
