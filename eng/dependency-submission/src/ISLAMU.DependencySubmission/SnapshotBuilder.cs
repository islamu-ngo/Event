using System.Collections.Immutable;
using System.Text.Json;

namespace ISLAMU.DependencySubmission;

public static class SnapshotBuilder
{
    public static DependencySnapshot Build(
        SnapshotContext context, ImmutableSortedDictionary<string, SnapshotManifest> manifests)
    {
        if (context.Sha.Length is not (40 or 64) || !context.Sha.All(Uri.IsHexDigit) ||
            !context.Ref.StartsWith("refs/heads/", StringComparison.Ordinal) ||
            context.Job.Id <= 0 || string.IsNullOrWhiteSpace(context.Job.Correlator) ||
            !context.Job.HtmlUrl.IsAbsoluteUri || context.Job.HtmlUrl.Scheme != Uri.UriSchemeHttps)
            throw new SnapshotValidationException(SnapshotValidationFailure.Context);
        return new DependencySnapshot(0, context.Sha, context.Ref, context.Job,
            new SnapshotDetector("islamu-nuget-lock", "1.0.0",
                new Uri("https://github.com/islamu-ngo/Event/tree/develop/eng/dependency-submission")),
            context.Scanned.ToUniversalTime(), manifests);
    }

    public static ImmutableSortedDictionary<string, SnapshotManifest> BuildManifests(IEnumerable<LockInput> inputs)
    {
        var manifests = ImmutableSortedDictionary.CreateBuilder<string, SnapshotManifest>(StringComparer.Ordinal);
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var input in inputs)
        {
            ValidatePath(input.Path);
            if (!paths.Add(input.Path))
                throw new SnapshotValidationException(SnapshotValidationFailure.DuplicatePath);
            try
            {
                using var document = JsonDocument.Parse(input.Json);
                RejectDuplicateProperties(document.RootElement);
                if (document.RootElement.GetProperty("version").GetInt32() is not (1 or 2))
                    throw new SnapshotValidationException(SnapshotValidationFailure.LockVersion);
                var groups = document.RootElement.GetProperty("dependencies");
                if (groups.ValueKind != JsonValueKind.Object)
                    throw new SnapshotValidationException(SnapshotValidationFailure.FrameworkGroup);
                foreach (var group in groups.EnumerateObject())
                {
                    if (string.IsNullOrWhiteSpace(group.Name) || group.Value.ValueKind != JsonValueKind.Object)
                        throw new SnapshotValidationException(SnapshotValidationFailure.FrameworkGroup);
                    var entries = new Dictionary<string, LockEntry>(StringComparer.OrdinalIgnoreCase);
                    foreach (var package in group.Value.EnumerateObject())
                    {
                        if (string.IsNullOrWhiteSpace(package.Name) ||
                            !WireTokens.LockKinds.TryGetValue(package.Value.GetProperty("type").GetString() ?? "", out var kind))
                            throw new SnapshotValidationException(SnapshotValidationFailure.PackageKind);
                        Uri? purl = null;
                        if (kind != LockDependencyKind.Project)
                        {
                            var version = package.Value.GetProperty("resolved").GetString();
                            if (string.IsNullOrWhiteSpace(version))
                                throw new SnapshotValidationException(SnapshotValidationFailure.ResolvedVersion);
                            purl = new Uri($"pkg:nuget/{Uri.EscapeDataString(package.Name.ToLowerInvariant())}@{Uri.EscapeDataString(version)}");
                        }
                        var children = kind != LockDependencyKind.Project &&
                            package.Value.TryGetProperty("dependencies", out var dependencies)
                            ? dependencies.EnumerateObject().Select(child => child.Name).ToImmutableArray()
                            : ImmutableArray<string>.Empty;
                        if (!entries.TryAdd(package.Name, new LockEntry(kind, purl, children)))
                            throw new SnapshotValidationException(SnapshotValidationFailure.DuplicatePackage);
                    }

                    var resolved = ImmutableSortedDictionary.CreateBuilder<string, SnapshotDependency>(StringComparer.Ordinal);
                    foreach (var entry in entries.Values)
                    {
                        var children = ImmutableArray.CreateBuilder<Uri>();
                        foreach (var child in entry.Children)
                        {
                            // NuGet retains declarations excluded by PrivateAssets or pruning.
                            // Only resolved entries in this framework can become graph edges.
                            if (entries.TryGetValue(child, out var target) && target.PackageUrl is not null)
                                children.Add(target.PackageUrl);
                        }
                        if (entry.PackageUrl is null)
                            continue;
                        resolved.Add(entry.PackageUrl.OriginalString, new SnapshotDependency(
                            entry.PackageUrl,
                            entry.Kind == LockDependencyKind.Direct ? DependencyRelationship.Direct : DependencyRelationship.Indirect,
                            ScopeFor(input.Path),
                            children.Distinct().OrderBy(uri => uri.OriginalString, StringComparer.Ordinal).ToImmutableArray()));
                    }
                    var key = $"{input.Path}::{group.Name}";
                    manifests.Add(key, new SnapshotManifest(key, new ManifestFile(input.Path),
                        new ManifestMetadata(group.Name), resolved.ToImmutable()));
                }
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException or
                InvalidOperationException or FormatException or ArgumentException)
            {
                // Parsing details can contain input bodies. Keep the boundary diagnostic source-free.
                throw new SnapshotValidationException(SnapshotValidationFailure.LockDocument);
            }
        }
        if (paths.Count == 0 || manifests.Count == 0)
            throw new SnapshotValidationException(SnapshotValidationFailure.EmptyRepository);
        return manifests.ToImmutable();
    }

    private static DependencyScope ScopeFor(string path) =>
        path.StartsWith("tests/", StringComparison.Ordinal) ||
        path.StartsWith("eng/", StringComparison.Ordinal) ||
        path.StartsWith(".agents/", StringComparison.Ordinal) ||
        path.StartsWith(".ci/", StringComparison.Ordinal)
            ? DependencyScope.Development : DependencyScope.Runtime;

    private static void ValidatePath(string path)
    {
        if (Path.IsPathRooted(path) || path.Contains('\\', StringComparison.Ordinal) ||
            path.Split('/').Any(segment => segment is "" or "." or "..") ||
            Path.GetFileName(path) != "packages.lock.json")
            throw new SnapshotValidationException(SnapshotValidationFailure.RepositoryPath);
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Add(property.Name))
                throw new SnapshotValidationException(SnapshotValidationFailure.DuplicateProperty);
            RejectDuplicateProperties(property.Value);
        }
    }

    private sealed record LockEntry(LockDependencyKind Kind, Uri? PackageUrl, ImmutableArray<string> Children);
}
