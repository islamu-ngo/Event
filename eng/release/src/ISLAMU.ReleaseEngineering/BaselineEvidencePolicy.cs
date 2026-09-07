// ABOUTME: Reads and validates first-release changelog baseline evidence files.
// ABOUTME: Keeps non-SemVer baseline refs separate from governed SemVer release history.

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ISLAMU.ReleaseEngineering;

public sealed record VerifiedBaseline(string Ref, string TargetOid, string TagObjectId);

public static class BaselineEvidencePolicy
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly Regex BaselineRefPattern = new("^changelog-baseline-[0-9]{4}-[0-9]{2}-[0-9]{2}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex FullOidPattern = new("^(?:[0-9a-f]{40}|[0-9a-f]{64})$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static bool IsBaselineRef(string value) => BaselineRefPattern.IsMatch(value);

    public static bool TryRead(string repositoryRoot, string baselineRef, out VerifiedBaseline baseline)
    {
        baseline = default!;
        if (!IsBaselineRef(baselineRef)) return false;

        string baselineFileName = Path.GetFileName(baselineRef + ".v1.json");
        string path = Path.Join(Path.GetFullPath(repositoryRoot), "docs", "internal", "releases", "baselines", baselineFileName);
        if (!File.Exists(path)) return false;

        byte[] bytes = File.ReadAllBytes(path);
        CanonicalArtifactResult canonical = CanonicalArtifactPolicy.CanonicalizeJson(StrictUtf8.GetString(bytes));
        if (!canonical.IsValid || canonical.Bytes is null || !bytes.AsSpan().SequenceEqual(canonical.Bytes)) return false;

        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement root = document.RootElement;
        string schema = root.GetProperty("schemaVersion").GetString() ?? string.Empty;
        string observedRef = root.GetProperty("baselineRef").GetString() ?? string.Empty;
        string targetOid = root.GetProperty("targetOid").GetString() ?? string.Empty;
        string tagObjectId = root.GetProperty("tagObjectId").GetString() ?? string.Empty;
        if (schema != "release-baseline.v1" || observedRef != baselineRef || !FullOidPattern.IsMatch(targetOid) || !FullOidPattern.IsMatch(tagObjectId)) return false;

        baseline = new VerifiedBaseline(observedRef, targetOid, tagObjectId);
        return true;
    }
}
