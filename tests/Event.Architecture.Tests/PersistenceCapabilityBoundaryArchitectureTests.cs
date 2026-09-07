using System.Text.RegularExpressions;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace Event.Architecture.Tests;

public sealed partial class PersistenceCapabilityBoundaryArchitectureTests
{
    private static readonly JsonSerializerOptions RegistryJsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Test]
    public async Task RawEfBoundary_ShouldRejectSyntheticFixture()
    {
        string fixturePath = ContextSystemHelpers.RepoPath(
            "tests",
            "Event.Architecture.Tests",
            "Fixtures",
            "Persistence",
            "ForbiddenRawSql.fixture");
        string source = await File.ReadAllTextAsync(fixturePath);
        IReadOnlyList<string> violations = FindRawEfViolations(fixturePath, source);

        await Assert.That(violations).IsEquivalentTo(
            ["tests/Event.Architecture.Tests/Fixtures/Persistence/ForbiddenRawSql.fixture:7:ExecuteSqlRawAsync"])
            .Because("the architecture probe must identify the exact synthetic raw EF violation");
    }

    [Test]
    public async Task RuntimeRawEfApis_ShouldMatchTemporaryRegistry()
    {
        PersistenceViolationRegistry registry = await ReadRegistryAsync();
        var actualCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        string persistenceRoot = ContextSystemHelpers.RepoPath("Explore.Persistence");
        foreach (string sourcePath in Directory.GetFiles(
                     persistenceRoot,
                     "*.cs",
                     SearchOption.AllDirectories))
        {
            string relativePath = RelativePath(sourcePath);
            if (IsGeneratedOrBuildOutput(relativePath) ||
                registry.ApprovedProviderPrimitivePathPrefixes.Any(prefix =>
                    relativePath.StartsWith(prefix, StringComparison.Ordinal)))
            {
                continue;
            }

            string source = await File.ReadAllTextAsync(sourcePath);
            int count = RawEfApiPattern().Count(source);
            if (count > 0)
            {
                actualCounts.Add(relativePath, count);
            }
        }

        var expectedCounts = registry.RawEf.ToDictionary(
            entry => entry.Path,
            entry => entry.Count,
            StringComparer.Ordinal);
        var violations = new List<string>();
        foreach ((string path, int count) in actualCounts)
        {
            if (!expectedCounts.TryGetValue(path, out int expectedCount))
            {
                violations.Add($"{path}: unregistered raw EF owner with {count} call(s)");
            }
            else if (count != expectedCount)
            {
                violations.Add($"{path}: raw EF call count changed from {expectedCount} to {count}");
            }
        }

        foreach (RawEfRegistryEntry entry in registry.RawEf)
        {
            if (!actualCounts.ContainsKey(entry.Path))
            {
                violations.Add($"{entry.Path}: stale raw EF registry entry");
            }
            if (string.IsNullOrWhiteSpace(entry.RemovalTask))
            {
                violations.Add($"{entry.Path}: missing removal task");
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because("raw EF APIs are permitted only in an approved provider primitive or an exact shrinking registry entry");
    }

    [Test]
    public async Task DirectAdoBoundary_ShouldRejectSyntheticFixture()
    {
        string fixturePath = ContextSystemHelpers.RepoPath(
            "tests",
            "Event.Architecture.Tests",
            "Fixtures",
            "Persistence",
            "ForbiddenDirectAdo.fixture");
        string source = await File.ReadAllTextAsync(fixturePath);
        IReadOnlyList<string> violations = FindDirectAdoViolations(fixturePath, source);

        await Assert.That(violations).IsEquivalentTo(
            ["tests/Event.Architecture.Tests/Fixtures/Persistence/ForbiddenDirectAdo.fixture:7:.CreateCommand"])
            .Because("the architecture probe must identify the exact synthetic direct ADO violation");
    }

    [Test]
    public async Task RuntimeDirectAdoApis_ShouldMatchTemporaryRegistry()
    {
        PersistenceViolationRegistry registry = await ReadRegistryAsync();
        var actualCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        string persistenceRoot = ContextSystemHelpers.RepoPath("Explore.Persistence");
        foreach (string sourcePath in Directory.GetFiles(
                     persistenceRoot,
                     "*.cs",
                     SearchOption.AllDirectories))
        {
            string relativePath = RelativePath(sourcePath);
            if (IsGeneratedOrBuildOutput(relativePath) ||
                registry.ApprovedProviderPrimitivePathPrefixes.Any(prefix =>
                    relativePath.StartsWith(prefix, StringComparison.Ordinal)))
            {
                continue;
            }

            string source = await File.ReadAllTextAsync(sourcePath);
            int count = DirectAdoBoundaryPattern()
                .Matches(source)
                .Select(match => source[..match.Index].Count(character => character == '\n'))
                .Distinct()
                .Count();
            if (count > 0)
            {
                actualCounts.Add(relativePath, count);
            }
        }

        var expectedCounts = registry.DirectAdo.ToDictionary(
            entry => entry.Path,
            entry => entry.Count,
            StringComparer.Ordinal);
        var violations = new List<string>();
        foreach ((string path, int count) in actualCounts)
        {
            if (!expectedCounts.TryGetValue(path, out int expectedCount))
            {
                violations.Add($"{path}: unregistered direct ADO owner with {count} matching line(s)");
            }
            else if (count != expectedCount)
            {
                violations.Add($"{path}: direct ADO line count changed from {expectedCount} to {count}");
            }
        }

        foreach (DirectAdoRegistryEntry entry in registry.DirectAdo)
        {
            if (!actualCounts.ContainsKey(entry.Path))
            {
                violations.Add($"{entry.Path}: stale direct ADO registry entry");
            }
            if (string.IsNullOrWhiteSpace(entry.RemovalTask))
            {
                violations.Add($"{entry.Path}: missing removal task");
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because("direct ADO is permitted only in an approved provider primitive or an exact shrinking registry entry");
    }

    [Test]
    public async Task PhysicalNameBoundary_ShouldRejectSyntheticFixture()
    {
        string fixturePath = ContextSystemHelpers.RepoPath(
            "tests",
            "Event.Architecture.Tests",
            "Fixtures",
            "Persistence",
            "ForbiddenPhysicalNames.fixture");
        string source = await File.ReadAllTextAsync(fixturePath);
        IReadOnlyList<string> violations = FindPhysicalNameViolations(fixturePath, source);

        await Assert.That(violations).IsEquivalentTo(
        [
            "tests/Event.Architecture.Tests/Fixtures/Persistence/ForbiddenPhysicalNames.fixture:8:ToTable",
            "tests/Event.Architecture.Tests/Fixtures/Persistence/ForbiddenPhysicalNames.fixture:9:HasColumnName",
            "tests/Event.Architecture.Tests/Fixtures/Persistence/ForbiddenPhysicalNames.fixture:10:HasDatabaseName"
        ]).Because("the architecture probe must identify every synthetic physical-name violation");
    }

    [Test]
    public async Task ConstraintClassifierBoundary_ShouldRejectSyntheticFixture()
    {
        string fixturePath = ContextSystemHelpers.RepoPath(
            "tests",
            "Event.Architecture.Tests",
            "Fixtures",
            "Persistence",
            "ForbiddenConstraintClassifier.fixture");
        string source = await File.ReadAllTextAsync(fixturePath);
        IReadOnlyList<string> violations = FindConstraintIdentifierViolations(fixturePath, source);

        await Assert.That(violations).IsEquivalentTo(
            ["tests/Event.Architecture.Tests/Fixtures/Persistence/ForbiddenConstraintClassifier.fixture:6:ux_example_records_tenant_identity"])
            .Because("the architecture probe must reject duplicated physical identifiers in exception classifiers");
    }

    [Test]
    public async Task RepositoryConstraintClassifiers_ShouldDeriveIdentifiersFromMetadata()
    {
        string repositoriesRoot = ContextSystemHelpers.RepoPath("Explore.Persistence", "Repositories");
        var sourcePaths = Directory.GetFiles(repositoriesRoot, "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(
                    ContextSystemHelpers.RepoPath("Explore.Persistence", "Database"),
                    "*Classifier.cs",
                    SearchOption.AllDirectories))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        var violations = new List<string>();

        foreach (string sourcePath in sourcePaths)
        {
            string source = await File.ReadAllTextAsync(sourcePath);
            violations.AddRange(FindConstraintIdentifierViolations(sourcePath, source));
        }

        await Assert.That(violations).IsEmpty()
            .Because("repository exception classification must use finalized EF metadata instead of duplicated physical names");
    }

    [Test]
    public async Task RowFenceCallers_ShouldPassMappedPropertyExpressions()
    {
        string persistenceRoot =
            ContextSystemHelpers.RepoPath("src", "Explore.Persistence");
        var violations = new List<string>();
        foreach (string path in Directory.GetFiles(
                     persistenceRoot,
                     "*.cs",
                     SearchOption.AllDirectories))
        {
            string source = await File.ReadAllTextAsync(path);
            foreach (Match invocation in RowFenceInvocationPattern().Matches(source))
            {
                if (StringArgumentPattern().IsMatch(invocation.Groups["arguments"].Value))
                {
                    violations.Add(RelativePath(path));
                }
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because(
                "row-fence callers must pass mapped property expressions rather than physical column strings");
    }

    [Test]
    public async Task WebhookRepositories_ShouldDelegateAdvisoryLocksToProviderBoundary()
    {
        string repositoriesRoot = ContextSystemHelpers.RepoPath("Explore.Persistence", "Repositories");
        string[] repositoryNames =
        [
            "IncomingWebhookEffectOutboxRepository.cs",
            "IncomingWebhookMessageRepository.cs",
            "WebhookBulkReplayRepository.cs",
            "WebhookLocalTargetRepository.cs",
            "WebhookProviderPublicationRepository.cs"
        ];
        var violations = new List<string>();

        foreach (string repositoryName in repositoryNames)
        {
            string source = await File.ReadAllTextAsync(Path.Combine(repositoriesRoot, repositoryName));
            if (source.Contains("pg_advisory_xact_lock", StringComparison.Ordinal))
            {
                violations.Add(repositoryName);
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because("webhook claim parity requires the shared provider-neutral named-lock boundary");
    }

    [Test]
    public async Task AtprotoRepositories_ShouldDelegateAdvisoryLocksToProviderBoundary()
    {
        string repositoriesRoot = ContextSystemHelpers.RepoPath("Explore.Persistence", "Repositories");
        string[] repositoryNames =
        [
            "AtprotoJetstreamRepository.cs",
            "PdsSyncOutboxRepository.cs"
        ];
        var violations = new List<string>();

        foreach (string repositoryName in repositoryNames)
        {
            string source = await File.ReadAllTextAsync(Path.Combine(repositoriesRoot, repositoryName));
            if (source.Contains("pg_advisory_xact_lock", StringComparison.Ordinal))
            {
                violations.Add(repositoryName);
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because("ATProtocol claim parity requires the shared provider-neutral named-lock boundary");
    }

    [Test]
    public async Task RuntimePhysicalNames_ShouldMatchTemporaryFingerprints()
    {
        PersistenceViolationRegistry registry = await ReadRegistryAsync();
        var violations = new List<string>();
        foreach (PhysicalNameRegistryEntry entry in registry.PhysicalNames)
        {
            Regex pattern = entry.Category switch
            {
                "table" => TableLiteralPattern(),
                "column" => ColumnLiteralPattern(),
                "index" => IndexLiteralPattern(),
                _ => throw new InvalidOperationException(
                    $"Unknown physical-name registry category '{entry.Category}'.")
            };
            ViolationFingerprint actual = await CaptureFingerprintAsync(pattern);
            if (actual.MatchCount != entry.MatchCount ||
                actual.OwnerCount != entry.OwnerCount ||
                !string.Equals(actual.OwnerSha256, entry.OwnerSha256, StringComparison.Ordinal) ||
                !string.Equals(actual.MatchSha256, entry.MatchSha256, StringComparison.Ordinal))
            {
                violations.Add(
                    $"{entry.Category}: expected {entry.MatchCount}/{entry.OwnerCount} " +
                    $"{entry.MatchSha256}/{entry.OwnerSha256}, observed " +
                    $"{actual.MatchCount}/{actual.OwnerCount} " +
                    $"{actual.MatchSha256}/{actual.OwnerSha256}");
            }
            if (entry.RemovalTasks.Length == 0 ||
                entry.RemovalTasks.Any(string.IsNullOrWhiteSpace))
            {
                violations.Add($"{entry.Category}: missing removal task");
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because("physical-name exceptions must retain exact owner and source fingerprints until their removal phase");
    }

    [Test]
    public async Task ProviderBranchBoundary_ShouldRejectSyntheticFixture()
    {
        string fixturePath = ContextSystemHelpers.RepoPath(
            "tests",
            "Event.Architecture.Tests",
            "Fixtures",
            "Persistence",
            "ForbiddenProviderLiteral.fixture");
        string source = await File.ReadAllTextAsync(fixturePath);
        IReadOnlyList<string> violations = FindViolations(
            fixturePath,
            source,
            ProviderBranchPattern(),
            "provider-branch");

        await Assert.That(violations).IsEquivalentTo(
            ["tests/Event.Architecture.Tests/Fixtures/Persistence/ForbiddenProviderLiteral.fixture:7:provider-branch"])
            .Because("the architecture probe must identify the exact synthetic provider branch");
    }

    [Test]
    public async Task RuntimeRepositories_ShouldContainNoProviderSpecificPersistencePrimitives()
    {
        string[] roots =
        [
            ContextSystemHelpers.RepoPath("src", "Explore.Persistence", "Repositories"),
            ContextSystemHelpers.RepoPath("src", "Explore.Persistence", "Services"),
            ContextSystemHelpers.RepoPath(
                "src",
                "Explore.Persistence",
                "Privacy",
                "ErasureAuthority",
                "Repositories")
        ];
        var violations = new List<string>();
        foreach (string path in roots.SelectMany(root =>
                     Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)))
        {
            string source = await File.ReadAllTextAsync(path);
            violations.AddRange(FindViolations(path, source, RawEfApiPattern(), "raw-ef"));
            violations.AddRange(FindViolations(path, source, DirectAdoApiPattern(), "direct-ado"));
            violations.AddRange(FindViolations(path, source, ProviderBranchPattern(), "provider-branch"));
            violations.AddRange(FindViolations(path, source, PhysicalNamePattern(), "physical-name"));
        }

        await Assert.That(violations).IsEmpty()
            .Because(
                "repositories and services must delegate provider-specific persistence to approved primitive types");
    }

    [Test]
    public async Task InternalApiBoundary_ShouldRejectSyntheticFixture()
    {
        string fixturePath = ContextSystemHelpers.RepoPath(
            "tests",
            "Event.Architecture.Tests",
            "Fixtures",
            "Persistence",
            "ForbiddenInternalImport.fixture");
        string source = await File.ReadAllTextAsync(fixturePath);
        IReadOnlyList<string> violations = FindViolations(
            fixturePath,
            source,
            InternalImportPattern(),
            "internal-import");

        await Assert.That(violations).IsEquivalentTo(
            ["tests/Event.Architecture.Tests/Fixtures/Persistence/ForbiddenInternalImport.fixture:4:internal-import"])
            .Because("the architecture probe must identify the exact synthetic internal import");
    }

    [Test]
    public async Task RuntimeProviderAndInternalSeams_ShouldMatchTemporaryFingerprints()
    {
        PersistenceViolationRegistry registry = await ReadRegistryAsync();
        var violations = new List<string>();
        foreach (SeamRegistryEntry entry in registry.Seams)
        {
            Regex pattern = entry.Category switch
            {
                "provider-branch" => ProviderBranchPattern(),
                "internal-import" => InternalImportPattern(),
                _ => throw new InvalidOperationException(
                    $"Unknown persistence seam registry category '{entry.Category}'.")
            };
            ViolationFingerprint actual = await CaptureFingerprintAsync(
                pattern,
                entry.Roots,
                entry.ExcludeMigrations,
                registry.ApprovedProviderPrimitivePathPrefixes);
            if (actual.MatchCount != entry.MatchCount ||
                actual.OwnerCount != entry.OwnerCount ||
                !string.Equals(actual.OwnerSha256, entry.OwnerSha256, StringComparison.Ordinal) ||
                !string.Equals(actual.MatchSha256, entry.MatchSha256, StringComparison.Ordinal))
            {
                violations.Add(
                    $"{entry.Category}: expected {entry.MatchCount}/{entry.OwnerCount} " +
                    $"{entry.MatchSha256}/{entry.OwnerSha256}, observed " +
                    $"{actual.MatchCount}/{actual.OwnerCount} " +
                    $"{actual.MatchSha256}/{actual.OwnerSha256}");
            }
            if (entry.RemovalTasks.Length == 0 ||
                entry.RemovalTasks.Any(string.IsNullOrWhiteSpace))
            {
                violations.Add($"{entry.Category}: missing removal task");
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because("provider branches and internal imports must retain exact fingerprints until their removal phases");
    }

    private static IReadOnlyList<string> FindRawEfViolations(string path, string source)
    {
        string relativePath = Path.GetRelativePath(ContextSystemHelpers.RepoRoot, path)
            .Replace(Path.DirectorySeparatorChar, '/');
        return RawEfApiPattern()
            .Matches(source)
            .Select(match =>
                $"{relativePath}:{source[..match.Index].Count(character => character == '\n') + 1}:{match.Groups["api"].Value}")
            .ToArray();
    }

    private static IReadOnlyList<string> FindDirectAdoViolations(string path, string source)
    {
        string relativePath = RelativePath(path);
        return DirectAdoApiPattern()
            .Matches(source)
            .Select(match =>
                $"{relativePath}:{source[..match.Index].Count(character => character == '\n') + 1}:{match.Groups["api"].Value}")
            .ToArray();
    }

    private static IReadOnlyList<string> FindPhysicalNameViolations(string path, string source)
    {
        string relativePath = RelativePath(path);
        return PhysicalNamePattern()
            .Matches(source)
            .Select(match =>
                $"{relativePath}:{source[..match.Index].Count(character => character == '\n') + 1}:{match.Groups["api"].Value}")
            .ToArray();
    }

    private static IReadOnlyList<string> FindConstraintIdentifierViolations(string path, string source)
    {
        string relativePath = RelativePath(path);
        return ConstraintIdentifierLiteralPattern()
            .Matches(source)
            .Select(match =>
                $"{relativePath}:{source[..match.Index].Count(character => character == '\n') + 1}:{match.Groups["identifier"].Value}")
            .ToArray();
    }

    private static IReadOnlyList<string> FindViolations(
        string path,
        string source,
        Regex pattern,
        string category)
    {
        string relativePath = RelativePath(path);
        return pattern
            .Matches(source)
            .Select(match =>
                $"{relativePath}:{source[..match.Index].Count(character => character == '\n') + 1}:{category}")
            .ToArray();
    }

    private static async Task<PersistenceViolationRegistry> ReadRegistryAsync()
    {
        string registryPath = ContextSystemHelpers.RepoPath(
            "tests",
            "Event.Architecture.Tests",
            "Fixtures",
            "Persistence",
            "persistence-violation-registry.json");
        await using FileStream stream = File.OpenRead(registryPath);
        return await JsonSerializer.DeserializeAsync<PersistenceViolationRegistry>(
                stream,
                RegistryJsonOptions)
            ?? throw new InvalidOperationException("Persistence violation registry is empty.");
    }

    private static Task<ViolationFingerprint> CaptureFingerprintAsync(Regex pattern) =>
        CaptureFingerprintAsync(
            pattern,
            ["src/Explore.Persistence"],
            excludeMigrations: true,
            approvedPathPrefixes: []);

    private static async Task<ViolationFingerprint> CaptureFingerprintAsync(
        Regex pattern,
        IReadOnlyList<string> relativeRoots,
        bool excludeMigrations,
        IReadOnlyList<string> approvedPathPrefixes)
    {
        var matches = new List<string>();
        var owners = new HashSet<string>(StringComparer.Ordinal);
        foreach (string relativeRoot in relativeRoots)
        {
            string root = Path.Combine(
                ContextSystemHelpers.RepoRoot,
                relativeRoot.Replace('/', Path.DirectorySeparatorChar));
            foreach (string sourcePath in Directory.GetFiles(
                         root,
                         "*.cs",
                         SearchOption.AllDirectories))
            {
                string relativePath = RelativePath(sourcePath);
                if (IsBuildOutput(relativePath) ||
                    approvedPathPrefixes.Any(prefix =>
                        relativePath.StartsWith(prefix, StringComparison.Ordinal)) ||
                    (excludeMigrations &&
                     relativePath.Contains("/Migrations/", StringComparison.Ordinal)))
                {
                    continue;
                }

                string[] lines = await File.ReadAllLinesAsync(sourcePath);
                for (int index = 0; index < lines.Length; index++)
                {
                    if (!pattern.IsMatch(lines[index]))
                    {
                        continue;
                    }

                    owners.Add(relativePath);
                    matches.Add($"{relativePath}:{index + 1}:{lines[index]}");
                }
            }
        }

        return new ViolationFingerprint(
            matches.Count,
            owners.Count,
            Sha256(owners),
            Sha256(matches));
    }

    private static string Sha256(IEnumerable<string> values)
    {
        string normalized = string.Concat(
            values.Order(StringComparer.Ordinal).Select(value => value + "\n"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))
            .ToLowerInvariant();
    }

    private static string RelativePath(string path) =>
        Path.GetRelativePath(ContextSystemHelpers.RepoRoot, path)
            .Replace(Path.DirectorySeparatorChar, '/');

    private static bool IsGeneratedOrBuildOutput(string relativePath) =>
        relativePath.Contains("/Migrations/", StringComparison.Ordinal) ||
        IsBuildOutput(relativePath);

    private static bool IsBuildOutput(string relativePath) =>
        relativePath.Contains("/bin/", StringComparison.Ordinal) ||
        relativePath.Contains("/obj/", StringComparison.Ordinal) ||
        relativePath.EndsWith(".Designer.cs", StringComparison.Ordinal);

    [GeneratedRegex(
        @"(?<api>ExecuteSql(?:Raw|Interpolated)?(?:Async)?|FromSql(?:Raw|Interpolated)?|SqlQuery(?:Raw|Interpolated)?)(?:<[^>]+>)?\s*\(",
        RegexOptions.CultureInvariant)]
    private static partial Regex RawEfApiPattern();

    [GeneratedRegex(
        @"(?<api>\.CreateCommand|new\s+(?:Npgsql|Sql|MySql|MySqlConnector|Sqlite|Db)Command)\s*\(",
        RegexOptions.CultureInvariant)]
    private static partial Regex DirectAdoApiPattern();

    [GeneratedRegex(
        @"(?:CreateCommand\s*\(|CommandText\s*=|DbCommand\b|DbConnection\b)",
        RegexOptions.CultureInvariant)]
    private static partial Regex DirectAdoBoundaryPattern();

    [GeneratedRegex(
        @"(?<api>ToTable|HasColumnName|HasDatabaseName)\s*\(\s*""",
        RegexOptions.CultureInvariant)]
    private static partial Regex PhysicalNamePattern();

    [GeneratedRegex(@"ToTable\(\s*""", RegexOptions.CultureInvariant)]
    private static partial Regex TableLiteralPattern();

    [GeneratedRegex(@"HasColumnName\(\s*""", RegexOptions.CultureInvariant)]
    private static partial Regex ColumnLiteralPattern();

    [GeneratedRegex(@"HasDatabaseName\(\s*""", RegexOptions.CultureInvariant)]
    private static partial Regex IndexLiteralPattern();

    [GeneratedRegex(
        @"""(?<identifier>(?:pk|ak|ix|fk|ux|ex)_[a-z0-9_]+)""",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ConstraintIdentifierLiteralPattern();

    [GeneratedRegex(
        @"(?:ProviderName\s*(?:==|!=)|ProviderName\.(?:Contains|Equals)|IsNpgsql\s*\(|IsSqlite\s*\(|IsSqlServer\s*\(|IsMySql\s*\(|IsMariaDb\s*\()",
        RegexOptions.CultureInvariant)]
    private static partial Regex ProviderBranchPattern();

    [GeneratedRegex(
        @"RelationalEntityRowFence\.AcquireAsync<[^>]+>\s*\((?<arguments>[\s\S]*?)\);",
        RegexOptions.CultureInvariant)]
    private static partial Regex RowFenceInvocationPattern();

    [GeneratedRegex(
        @"(?:^|,)\s*""[^""]+""\s*,",
        RegexOptions.CultureInvariant)]
    private static partial Regex StringArgumentPattern();

    [GeneratedRegex(
        @"^using .*\.Internal(?:;|\.)",
        RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex InternalImportPattern();

    private sealed record PersistenceViolationRegistry(
        string[] Aboutme,
        int SchemaVersion,
        string[] ApprovedProviderPrimitivePathPrefixes,
        RawEfRegistryEntry[] RawEf,
        DirectAdoRegistryEntry[] DirectAdo,
        PhysicalNameRegistryEntry[] PhysicalNames,
        SeamRegistryEntry[] Seams);

    private sealed record RawEfRegistryEntry(
        string Path,
        int Count,
        string RemovalTask);

    private sealed record DirectAdoRegistryEntry(
        string Path,
        int Count,
        string RemovalTask);

    private sealed record PhysicalNameRegistryEntry(
        string Category,
        int MatchCount,
        int OwnerCount,
        string OwnerSha256,
        string MatchSha256,
        string[] RemovalTasks);

    private sealed record SeamRegistryEntry(
        string Category,
        string[] Roots,
        bool ExcludeMigrations,
        int MatchCount,
        int OwnerCount,
        string OwnerSha256,
        string MatchSha256,
        string[] RemovalTasks);

    private sealed record ViolationFingerprint(
        int MatchCount,
        int OwnerCount,
        string OwnerSha256,
        string MatchSha256);
}
