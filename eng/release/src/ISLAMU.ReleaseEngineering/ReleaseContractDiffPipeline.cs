using System.Text.Json;
using System.Text.Json.Serialization;

namespace ISLAMU.ReleaseEngineering;

public sealed record ContractDiffCounts(
    int Total,
    int Breaking,
    int ReviewRequired,
    int Additive);

public sealed record ReleaseContractDiffArtifact(
    int SchemaVersion,
    string BaseOid,
    string HeadOid,
    IReadOnlyList<string> EvaluatedFamilies,
    ContractDiffCounts Summary,
    IReadOnlyList<ContractDiffChange> Changes,
    MigrationLedgerSummary? MigrationLedger);

public sealed record ReleaseContractDiffResult(
    bool IsValid,
    ReleaseContractDiffArtifact? Artifact,
    string? Json,
    string? Diagnostic);

/// <summary>
/// Composite runner that executes ordered <see cref="IReleaseContractDiffer"/> strategies
/// against pinned Git revisions and serializes canonical, repeatable <c>contract-diff.v1.json</c>.
/// </summary>
public sealed class ReleaseContractDiffPipeline
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    private readonly IReadOnlyList<IReleaseContractDiffer> differs;

    public ReleaseContractDiffPipeline(IEnumerable<IReleaseContractDiffer> differs)
    {
        ArgumentNullException.ThrowIfNull(differs);
        this.differs = differs
            .OrderBy(differ => differ.Order)
            .ThenBy(differ => differ.Family, StringComparer.Ordinal)
            .ToArray();
    }

    public static ReleaseContractDiffPipeline Default { get; } = new(
    [
        new OpenApiContractDiffer(),
        new ConfigurationManifestDiffer(),
        new MigrationLedgerExtractor(),
    ]);

    public ReleaseContractDiffResult Execute(IGitObjectReader reader, string baseOid, string headOid)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (!GitProcessObjectReader.IsFullOid(baseOid) || !GitProcessObjectReader.IsFullOid(headOid))
        {
            return new ReleaseContractDiffResult(false, null, null, "contract_diff_invalid_oid");
        }

        if (!reader.CommitExists(baseOid))
        {
            return new ReleaseContractDiffResult(false, null, null, "contract_diff_base_commit_missing");
        }

        if (!reader.CommitExists(headOid))
        {
            return new ReleaseContractDiffResult(false, null, null, "contract_diff_head_commit_missing");
        }

        var allChanges = new List<ContractDiffChange>();
        var evaluatedFamilies = new List<string>(differs.Count);
        MigrationLedgerSummary? migrationLedger = null;

        foreach (IReleaseContractDiffer differ in differs)
        {
            ContractDifferResult partial = differ.Diff(reader, baseOid, headOid);
            if (!partial.IsValid)
            {
                return new ReleaseContractDiffResult(
                    false,
                    null,
                    null,
                    partial.Diagnostic ?? $"contract_diff_failed:{differ.Family}");
            }

            evaluatedFamilies.Add(partial.Family);
            allChanges.AddRange(partial.Changes);
            if (partial.MigrationLedger is not null)
            {
                migrationLedger = partial.MigrationLedger;
            }
        }

        ContractDiffChange[] orderedChanges = allChanges
            .OrderBy(change => change.Family, StringComparer.Ordinal)
            .ThenBy(change => ContractChangeSeverity.Rank(change.Severity))
            .ThenBy(change => change.Kind, StringComparer.Ordinal)
            .ThenBy(change => change.Target, StringComparer.Ordinal)
            .ThenBy(change => change.ArtifactPath ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(change => change.Summary, StringComparer.Ordinal)
            .ToArray();

        var counts = new ContractDiffCounts(
            Total: orderedChanges.Length,
            Breaking: orderedChanges.Count(change => string.Equals(change.Severity, ContractChangeSeverity.Breaking, StringComparison.Ordinal)),
            ReviewRequired: orderedChanges.Count(change => string.Equals(change.Severity, ContractChangeSeverity.ReviewRequired, StringComparison.Ordinal)),
            Additive: orderedChanges.Count(change => string.Equals(change.Severity, ContractChangeSeverity.Additive, StringComparison.Ordinal)));

        var artifact = new ReleaseContractDiffArtifact(
            SchemaVersion: 1,
            BaseOid: baseOid,
            HeadOid: headOid,
            EvaluatedFamilies: evaluatedFamilies,
            Summary: counts,
            Changes: orderedChanges,
            MigrationLedger: migrationLedger);

        string json = JsonSerializer.Serialize(artifact, JsonOptions) + "\n";
        return new ReleaseContractDiffResult(true, artifact, json, null);
    }
}
