namespace Event.Setup.Core.Tests;

using System.Reflection;
using System.Text.Json;
using ISLAMU.Event.Setup.Core;
using ISLAMU.Event.Setup.Core.Composition;

public sealed class SetupCompositionScaleTests
{
    [Test]
    public async Task GeneratedProfilesMatchClosedProductRegistryAndCanonicalDefaults()
    {
        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllBytes(GeneratedProfilesPath()));
        JsonElement root = document.RootElement;
        JsonElement defaults = root.GetProperty("canonicalDefault");
        JsonElement[] generated = root.GetProperty("profiles").EnumerateArray().ToArray();
        SetupCompositionScaleProfile[] product = SetupCompositionScaleProfiles.All.ToArray();

        await Assert.That(root.GetProperty("schemaVersion").GetInt32()).IsEqualTo(1);
        await Assert.That(generated.Select(ProfileName))
            .IsEquivalentTo(["small", "medium", "large", "ceiling"]);
        await Assert.That(product.Select(profile => profile.Name))
            .IsEquivalentTo(["small", "medium", "large", "ceiling"]);
        await Assert.That(defaults.GetProperty("aggregateSourceBytes").GetInt32())
            .IsEqualTo(SetupCompositionLimits.DefaultAggregateSourceBytes);
        await Assert.That(defaults.GetProperty("yamlDocuments").GetInt32())
            .IsEqualTo(SetupCompositionLimits.DefaultYamlDocuments);
        await Assert.That(defaults.GetProperty("parserEvents").GetInt32())
            .IsEqualTo(SetupCompositionLimits.DefaultParserEvents);
        await Assert.That(defaults.GetProperty("normalizedNodes").GetInt32())
            .IsEqualTo(SetupCompositionLimits.DefaultNormalizedNodes);
        await Assert.That(defaults.GetProperty("nestingDepth").GetInt32())
            .IsEqualTo(SetupCompositionLimits.DefaultNestingDepth);
        await Assert.That(defaults.GetProperty("mappingEntries").GetInt32())
            .IsEqualTo(SetupCompositionLimits.DefaultMappingEntries);
        await Assert.That(defaults.GetProperty("sequenceEntries").GetInt32())
            .IsEqualTo(SetupCompositionLimits.DefaultSequenceEntries);
        await Assert.That(defaults.GetProperty("scalarCharacters").GetInt32())
            .IsEqualTo(SetupCompositionLimits.DefaultScalarCharacters);
        await Assert.That(defaults.GetProperty("aggregateScalarCharacters").GetInt32())
            .IsEqualTo(SetupCompositionLimits.DefaultAggregateScalarCharacters);
        await Assert.That(defaults.GetProperty("directories").GetInt32())
            .IsEqualTo(SetupCompositionLimits.DefaultDirectories);
        await Assert.That(defaults.GetProperty("files").GetInt32())
            .IsEqualTo(SetupCompositionLimits.DefaultFiles);
        await Assert.That(defaults.GetProperty("entriesPerDirectory").GetInt32())
            .IsEqualTo(SetupCompositionLimits.DefaultEntriesPerDirectory);
        await Assert.That(defaults.GetProperty("relativePathCharacters").GetInt32())
            .IsEqualTo(SetupCompositionLimits.DefaultRelativePathCharacters);
        await Assert.That(defaults.GetProperty("pathDepth").GetInt32())
            .IsEqualTo(SetupCompositionLimits.DefaultPathDepth);
        await Assert.That(defaults.GetProperty("perFileBytes").GetInt32())
            .IsEqualTo(SetupCompositionLimits.DefaultPerFileBytes);
        await Assert.That(defaults.GetProperty("aggregateDirectoryBytes").GetInt32())
            .IsEqualTo(SetupCompositionLimits.DefaultAggregateDirectoryBytes);
        await Assert.That(defaults.GetProperty("aggregateDirectoryNodes").GetInt32())
            .IsEqualTo(SetupCompositionLimits.DefaultAggregateDirectoryNodes);

        foreach (SetupCompositionScaleProfile profile in product)
        {
            JsonElement generatedProfile = generated.Single(item =>
                string.Equals(ProfileName(item), profile.Name, StringComparison.Ordinal));
            await Assert.That(generatedProfile.GetProperty("enabled").GetBoolean()).IsTrue();
            await Assert.That(generatedProfile.GetProperty("targetAccepted").GetBoolean()).IsTrue();
            await Assert.That(generatedProfile.GetProperty("evidenceDigest").GetString())
                .IsEqualTo(profile.EvidenceDigest.ToString());
            await Assert.That(generatedProfile.GetProperty("canonicalArtifactBytes").GetInt32())
                .IsEqualTo(profile.CanonicalArtifactBytes);
        }
    }

    [Test]
    public async Task AdmissionNeverClampsFallsBackOrAcceptsUnboundEvidence()
    {
        SetupCompositionScaleProfile small = SetupCompositionScaleProfiles.All.Single(
            profile => profile.Id == SetupCompositionScaleProfileId.Small);
        SetupCompositionScaleAdmission accepted = SetupCompositionScaleProfiles.Admit(
            small.Name, small.EvidenceDigest, small.CanonicalArtifactBytes);
        SetupCompositionScaleAdmission unknown = SetupCompositionScaleProfiles.Admit(
            "unknown", small.EvidenceDigest, int.MaxValue);
        SetupCompositionScaleAdmission disabled = SetupCompositionScaleProfiles.Admit(
            SetupCompositionScaleProfiles.DisabledExpandedProfileName,
            small.EvidenceDigest, int.MaxValue);
        SetupCompositionScaleAdmission mismatch = SetupCompositionScaleProfiles.Admit(
            small.Name, ArtifactDigest.Compute("mismatch"u8), int.MaxValue);
        SetupCompositionScaleAdmission incompatible = SetupCompositionScaleProfiles.Admit(
            small.Name, small.EvidenceDigest, small.CanonicalArtifactBytes - 1);

        await Assert.That(accepted.Succeeded).IsTrue();
        await Assert.That(accepted.Profile).IsSameReferenceAs(small);
        await Assert.That(unknown.Code).IsEqualTo(SetupCompositionScaleAdmissionCode.UnknownProfile);
        await Assert.That(disabled.Code).IsEqualTo(SetupCompositionScaleAdmissionCode.ProfileDisabled);
        await Assert.That(mismatch.Code).IsEqualTo(SetupCompositionScaleAdmissionCode.EvidenceMismatch);
        await Assert.That(incompatible.Code).IsEqualTo(SetupCompositionScaleAdmissionCode.TargetIncompatible);
        foreach (SetupCompositionScaleAdmission rejected in
                 new[] { unknown, disabled, mismatch, incompatible })
        {
            await Assert.That(rejected.Succeeded).IsFalse();
            await Assert.That(rejected.Profile).IsNull();
        }
    }

    [Test]
    public async Task RuntimeTelemetrySurfaceContainsOnlyClosedAggregateDimensions()
    {
        PropertyInfo[] properties = typeof(SetupCompositionScaleTelemetry).GetProperties(
            BindingFlags.Public | BindingFlags.Instance);

        await Assert.That(properties.Select(property => property.Name)).IsEquivalentTo(
        [
            "SourceKind", "Profile", "Outcome", "AggregateBytes",
            "Nodes", "Files", "DurationMicroseconds"
        ]);
        await Assert.That(properties.All(property =>
            property.PropertyType.IsEnum
            || property.PropertyType == typeof(int)
            || property.PropertyType == typeof(long))).IsTrue();
    }

    [Test]
    public async Task GeneratedEvidenceRecordsCompleteHostMeasurementAndTargetFacts()
    {
        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllBytes(GeneratedProfilesPath()));
        JsonElement root = document.RootElement;
        JsonElement host = root.GetProperty("host");
        string[] requiredHost =
        [
            "os", "architecture", "processorCount", "availableMemoryBytes",
            "totalMemoryBytes", "processLimits", "filesystemSemantics",
            "sdk", "runtime", "commit"
        ];
        await Assert.That(requiredHost.All(name => host.TryGetProperty(name, out _))).IsTrue();

        foreach (JsonElement profile in root.GetProperty("profiles").EnumerateArray())
        {
            string[] required =
            [
                "name", "sourceKind", "enabled", "evidenceDigest",
                "sourceRevision", "coreRevision", "wireRevision", "targetRevision",
                "directories", "files", "entriesPerDirectory", "aggregateSourceBytes",
                "perFileBytes", "depth", "nodes", "parserEvents", "mappingEntries",
                "sequenceEntries", "scalarCharacters", "canonicalArtifactBytes",
                "canonicalArtifactSha256", "warmupCount", "iterationCount",
                "medianElapsedMicroseconds", "p95ElapsedMicroseconds",
                "medianAllocatedBytes", "peakWorkingSetBytes", "gen0Collections",
                "gen1Collections", "gen2Collections", "stackOverflowDisposition",
                "cancellationObserved", "targetAccepted"
            ];
            await Assert.That(required.All(name => profile.TryGetProperty(name, out _))).IsTrue();
            await Assert.That(profile.GetProperty("warmupCount").GetInt32()).IsGreaterThan(0);
            await Assert.That(profile.GetProperty("iterationCount").GetInt32()).IsGreaterThan(0);
            await Assert.That(profile.GetProperty("cancellationObserved").GetBoolean()).IsTrue();
            await Assert.That(profile.GetProperty("targetAccepted").GetBoolean()).IsTrue();
        }
    }

    private static string ProfileName(JsonElement profile) =>
        profile.GetProperty("name").GetString()!;

    private static string GeneratedProfilesPath()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(
                current.FullName, "eng", "setup-assistant", "generated",
                "composition-scale-profiles.json");
            if (File.Exists(candidate))
                return candidate;
            current = current.Parent;
        }
        throw new InvalidOperationException("missing-generated-composition-scale-profiles");
    }
}
