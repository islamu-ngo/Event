#:project ../../src/Event.Setup.Core/Event.Setup.Core.csproj
#:property RestorePackagesWithLockFile=false

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ISLAMU.Event.Setup.Core.Composition;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using YamlDotNet.Core;

const int WarmupCount = 2;
const int IterationCount = 7;
const string GeneratedRelativePath =
    "eng/setup-assistant/generated/composition-scale-profiles.json";
const string EvidenceRelativePath =
    ".omo/evidence/20260831-setup-assistant-security-and-portability/phase8-scale-results.md";

if (args is not ["--measure", _] and not ["--check"])
{
    Console.Error.WriteLine(
        "Usage: GenerateSetupCompositionScaleProfiles.cs --measure <output-directory> | --check");
    return 64;
}

string repositoryRoot = FindRepositoryRoot();
string sourceRevision = HashFiles(repositoryRoot,
[
    "eng/setup-assistant/GenerateSetupCompositionScaleProfiles.cs"
]);
string coreRevision = HashFiles(repositoryRoot,
[
    "src/Event.Setup.Core/Composition/SetupCompositionContracts.cs",
    "src/Event.Setup.Core/Composition/SetupCompositionLimits.cs",
    "src/Event.Setup.Core/Composition/SetupCompositionCompiler.cs",
    "src/Event.Setup.Core/Composition/SetupCompositionYamlParser.cs",
    "src/Event.Setup.Core/Composition/SetupCompositionDirectoryReader.cs",
    "src/Event.Setup.Core/Composition/SetupCompositionNormalizer.cs"
]);
string wireRevision = HashFiles(repositoryRoot,
[
    "src/Event.Wire.Contracts/ConfigurationPortability/ConfigurationManifestV1Alpha2.cs",
    "src/Event.Wire.Contracts/ConfigurationPortability/ConfigurationPortabilityJsonCodec.cs",
    "src/Event.Wire.Contracts/ConfigurationPortability/ConfigurationPortabilityJsonContext.cs"
]);
string targetRevision = HashFiles(repositoryRoot,
[
    "schemas/configuration-manifest-v1alpha2.schema.json",
    "src/Event.Wire.Contracts/ConfigurationPortability/ConfigurationPortabilityJsonCodec.cs"
]);

ProfileSpec[] specs =
[
    new("small", SetupCompositionSourceKind.Json, 8, 24, 0),
    new("medium", SetupCompositionSourceKind.Yaml, 128, 48, 0),
    new("large", SetupCompositionSourceKind.Directory, 1_024, 64, 34),
    new("ceiling", SetupCompositionSourceKind.Json,
        SetupCompositionLimits.DefaultMappingEntries, 32, 0)
];

if (args[0] == "--check")
{
    string generatedPath = Path.Combine(repositoryRoot, GeneratedRelativePath);
    string evidencePath = Path.Combine(repositoryRoot, EvidenceRelativePath);
    ValidateGeneratedEvidence(
        generatedPath, evidencePath, sourceRevision, coreRevision, wireRevision, targetRevision);
    await VerifyCanonicalOutputsAsync(generatedPath, specs);
    Console.WriteLine("Setup composition scale profiles are current and canonical (4/4).");
    return 0;
}

string outputDirectory = Path.GetFullPath(args[1]);
Directory.CreateDirectory(outputDirectory);
HostEvidence host = ReadHostEvidence(repositoryRoot);
var measurements = new List<ProfileMeasurement>(specs.Length);
foreach (ProfileSpec spec in specs)
{
    measurements.Add(await MeasureAsync(
        spec, host, sourceRevision, coreRevision, wireRevision, targetRevision));
}

byte[] generated = WriteGenerated(host, measurements);
string generatedOutput = Path.Combine(outputDirectory, "composition-scale-profiles.json");
string evidenceOutput = Path.Combine(outputDirectory, "phase8-scale-results.md");
File.WriteAllBytes(generatedOutput, generated);
File.WriteAllText(evidenceOutput, WriteEvidenceMarkdown(host, measurements), new UTF8Encoding(false));
Console.WriteLine($"Measured Setup composition scale profiles (4/4) into {outputDirectory}.");
return 0;

static async Task<ProfileMeasurement> MeasureAsync(
    ProfileSpec spec,
    HostEvidence host,
    string sourceRevision,
    string coreRevision,
    string wireRevision,
    string targetRevision)
{
    ProfileInput input = CreateInput(spec);
    try
    {
        return await MeasureInputAsync(
            spec, input, host, sourceRevision, coreRevision, wireRevision, targetRevision);
    }
    finally
    {
        input.Cleanup();
    }
}

static async Task<ProfileMeasurement> MeasureInputAsync(
    ProfileSpec spec,
    ProfileInput input,
    HostEvidence host,
    string sourceRevision,
    string coreRevision,
    string wireRevision,
    string targetRevision)
{
    var compiler = new SetupCompositionCompiler();
    for (int index = 0; index < WarmupCount; index++)
    {
        SetupCompositionResult warmup = await compiler.CompileAsync(input.Source);
        RequireSuccess(spec.Name, warmup);
    }

    var elapsed = new long[IterationCount];
    var allocated = new long[IterationCount];
    int gen0 = 0;
    int gen1 = 0;
    int gen2 = 0;
    long peakWorkingSet = 0;
    SetupCompositionResult? final = null;
    for (int index = 0; index < IterationCount; index++)
    {
        int beforeGen0 = GC.CollectionCount(0);
        int beforeGen1 = GC.CollectionCount(1);
        int beforeGen2 = GC.CollectionCount(2);
        long beforeAllocated = GC.GetTotalAllocatedBytes(precise: true);
        long start = Stopwatch.GetTimestamp();
        SetupCompositionResult result = await compiler.CompileAsync(input.Source);
        long stop = Stopwatch.GetTimestamp();
        long afterAllocated = GC.GetTotalAllocatedBytes(precise: true);
        RequireSuccess(spec.Name, result);
        if (final is not null
            && !final.CanonicalBytes.Span.SequenceEqual(result.CanonicalBytes.Span))
            throw new InvalidOperationException($"non-deterministic-canonical-output:{spec.Name}");
        final = result;
        elapsed[index] = Stopwatch.GetElapsedTime(start, stop).Ticks / 10;
        allocated[index] = checked(afterAllocated - beforeAllocated);
        gen0 = checked(gen0 + GC.CollectionCount(0) - beforeGen0);
        gen1 = checked(gen1 + GC.CollectionCount(1) - beforeGen1);
        gen2 = checked(gen2 + GC.CollectionCount(2) - beforeGen2);
        peakWorkingSet = Math.Max(peakWorkingSet, Process.GetCurrentProcess().PeakWorkingSet64);
    }

    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    SetupCompositionResult cancelled =
        await compiler.CompileAsync(input.Source, cancellation.Token);
    bool cancellationObserved =
        cancelled.Failure.Code == SetupCompositionFailureCode.Cancelled
        && cancelled.CanonicalBytes.IsEmpty
        && cancelled.Artifact is null;

    byte[] canonical = final!.CanonicalBytes.ToArray();
    bool targetAccepted = AcceptByTargetContract(final.ArtifactKind, canonical);
    var measurement = new ProfileMeasurement(
        spec.Name,
        spec.SourceKind,
        Enabled: targetAccepted && cancellationObserved,
        EvidenceDigest: string.Empty,
        HostRevision: host.Revision,
        SourceRevision: sourceRevision,
        CoreRevision: coreRevision,
        WireRevision: wireRevision,
        TargetRevision: targetRevision,
        input.Directories,
        input.Files,
        input.EntriesPerDirectory,
        input.AggregateSourceBytes,
        input.PerFileBytes,
        input.Depth,
        input.Nodes,
        input.ParserEvents,
        input.MappingEntries,
        input.SequenceEntries,
        input.ScalarCharacters,
        canonical.Length,
        Convert.ToHexStringLower(SHA256.HashData(canonical)),
        WarmupCount,
        IterationCount,
        Median(elapsed),
        Percentile95(elapsed),
        Median(allocated),
        peakWorkingSet,
        gen0,
        gen1,
        gen2,
        "bounded-depth-32-no-overflow-observed",
        cancellationObserved,
        targetAccepted);
    return measurement with { EvidenceDigest = EvidenceDigest(measurement) };
}

static void RequireSuccess(string profile, SetupCompositionResult result)
{
    if (!result.Succeeded)
        throw new InvalidOperationException(
            $"profile-compilation-failed:{profile}:{result.Failure.Code}");
}

static bool AcceptByTargetContract(
    SetupCompositionArtifactKind artifactKind, byte[] canonical)
{
    if (canonical.Length > ConfigurationPortabilityContentLimits.MaximumArtifactUtf8Bytes)
        return false;
    try
    {
        switch (artifactKind)
        {
            case SetupCompositionArtifactKind.ConfigurationManifest:
                _ = ConfigurationPortabilityJsonCodec.ParseConfigurationManifest(canonical);
                break;
            case SetupCompositionArtifactKind.TenantConfigurationPackage:
                _ = ConfigurationPortabilityJsonCodec.ParseTenantConfigurationPackage(canonical);
                break;
            default:
                throw new InvalidOperationException("unknown-artifact-kind");
        }
        return true;
    }
    catch (ConfigurationPortabilityContractException)
    {
        return false;
    }
}

static ProfileInput CreateInput(ProfileSpec spec) => spec.SourceKind switch
{
    SetupCompositionSourceKind.Json => CreateJsonInput(spec),
    SetupCompositionSourceKind.Yaml => CreateYamlInput(spec),
    SetupCompositionSourceKind.Directory => CreateDirectoryInput(spec),
    _ => throw new InvalidOperationException("unsupported-source-kind")
};

static ProfileInput CreateJsonInput(ProfileSpec spec)
{
    byte[] bytes = CreateManifestJson(spec.Name, spec.SettingCount, spec.ValueCharacters);
    Shape shape = MeasureJson(bytes);
    return new ProfileInput(
        new SetupCompositionJsonSource(bytes), null, 0, 0, 0,
        bytes.Length, bytes.Length, shape.Depth, shape.Nodes, 0,
        shape.MappingEntries, shape.SequenceEntries, shape.ScalarCharacters);
}

static ProfileInput CreateYamlInput(ProfileSpec spec)
{
    byte[] bytes = CreateManifestYaml(spec.Name, spec.SettingCount, spec.ValueCharacters);
    Shape shape = MeasureYaml(bytes);
    return new ProfileInput(
        new SetupCompositionYamlSource(bytes), null, 0, 0, 0,
        bytes.Length, bytes.Length, shape.Depth, shape.Nodes, shape.ParserEvents,
        shape.MappingEntries, shape.SequenceEntries, shape.ScalarCharacters);
}

static ProfileInput CreateDirectoryInput(ProfileSpec spec)
{
    string directory = Path.Combine(
        Path.GetTempPath(), $"islamu-composition-scale-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    var files = new List<byte[]>(spec.DirectoryFiles);
    byte[] header = WriteJson(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("$schema", ConfigurationManifestContractMetadata.SchemaId);
        writer.WriteString("apiVersion", ConfigurationManifestContractMetadata.ApiVersion);
        writer.WriteString("kind", ConfigurationManifestContractMetadata.Kind);
        writer.WriteStartObject("metadata");
        writer.WriteString("name", $"profile-{spec.Name}");
        writer.WriteEndObject();
        writer.WriteEndObject();
    });
    byte[] body = """{"spec":{"instance":{"documents":{},"legalDocuments":{}},"tenants":[]}}"""u8.ToArray();
    files.Add(header);
    files.Add(body);
    int fragmentCount = spec.DirectoryFiles - 2;
    int settingIndex = 0;
    for (int fragment = 0; fragment < fragmentCount; fragment++)
    {
        int remaining = spec.SettingCount - settingIndex;
        int count = remaining / (fragmentCount - fragment);
        var settings = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
        for (int index = 0; index < count; index++)
        {
            settings.Add(
                $"setting-{settingIndex:D5}",
                StringElement(
                    $"{settingIndex:D5}-{new string('x', spec.ValueCharacters)}"));
            settingIndex++;
        }
        byte[] fragmentBytes = WriteJson(writer =>
        {
            writer.WriteStartObject();
            writer.WriteStartObject("spec");
            writer.WriteStartObject("instance");
            writer.WriteStartObject("settings");
            foreach ((string key, JsonElement value) in settings)
            {
                writer.WritePropertyName(key);
                value.WriteTo(writer);
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndObject();
        });
        files.Add(fragmentBytes);
    }

    int aggregateBytes = 0;
    int maxBytes = 0;
    int nodes = 0;
    int depth = 0;
    int mappingEntries = 0;
    int sequenceEntries = 0;
    int scalarCharacters = 0;
    for (int index = 0; index < files.Count; index++)
    {
        string path = Path.Combine(directory, $"{index:D3}.json");
        File.WriteAllBytes(path, files[index]);
        aggregateBytes = checked(aggregateBytes + files[index].Length);
        maxBytes = Math.Max(maxBytes, files[index].Length);
        Shape shape = MeasureJson(files[index]);
        nodes = checked(nodes + shape.Nodes);
        depth = Math.Max(depth, shape.Depth);
        mappingEntries = Math.Max(mappingEntries, shape.MappingEntries);
        sequenceEntries = Math.Max(sequenceEntries, shape.SequenceEntries);
        scalarCharacters = checked(scalarCharacters + shape.ScalarCharacters);
    }
    return new ProfileInput(
        new SetupCompositionDirectorySource(directory), directory, 1, files.Count,
        files.Count, aggregateBytes, maxBytes, depth, nodes, 0,
        mappingEntries, sequenceEntries, scalarCharacters);
}

static byte[] CreateManifestJson(string name, int settingCount, int valueCharacters)
{
    var settings = CreateSettings(settingCount, valueCharacters);
    var manifest = new ConfigurationManifestV1Alpha2
    {
        Schema = ConfigurationManifestContractMetadata.SchemaId,
        ApiVersion = ConfigurationManifestContractMetadata.ApiVersion,
        Kind = ConfigurationManifestContractMetadata.Kind,
        Metadata = new ConfigurationManifestMetadataV1Alpha2
        {
            Name = $"profile-{name}"
        },
        Spec = new ConfigurationManifestSpecV1Alpha2
        {
            Instance = new ConfigurationManifestInstanceV1Alpha2
            {
                Settings = settings,
                Documents = new Dictionary<string, ConfigurationManifestDocumentV1Alpha2>(),
                LegalDocuments =
                    new Dictionary<string, ConfigurationManifestLegalDocumentV1Alpha2>()
            },
            Tenants = Array.Empty<ConfigurationManifestTenantV1Alpha2>()
        }
    };
    return ConfigurationPortabilityJsonCodec.SerializeConfigurationManifest(manifest);
}

static byte[] CreateManifestYaml(string name, int settingCount, int valueCharacters)
{
    var builder = new StringBuilder();
    builder.AppendLine(FormattableString.Invariant(
        $"$schema: \"{ConfigurationManifestContractMetadata.SchemaId}\""));
    builder.AppendLine(FormattableString.Invariant(
        $"apiVersion: \"{ConfigurationManifestContractMetadata.ApiVersion}\""));
    builder.AppendLine(FormattableString.Invariant(
        $"kind: \"{ConfigurationManifestContractMetadata.Kind}\""));
    builder.AppendLine("metadata:");
    builder.AppendLine(FormattableString.Invariant($"  name: \"profile-{name}\""));
    builder.AppendLine("spec:");
    builder.AppendLine("  instance:");
    builder.AppendLine("    settings:");
    for (int index = 0; index < settingCount; index++)
        builder.AppendLine(FormattableString.Invariant(
            $"      setting-{index:D5}: \"{index:D5}-{new string('x', valueCharacters)}\""));
    builder.AppendLine("    documents: {}");
    builder.AppendLine("    legalDocuments: {}");
    builder.AppendLine("  tenants: []");
    return Encoding.UTF8.GetBytes(builder.ToString());
}

static SortedDictionary<string, JsonElement> CreateSettings(
    int count, int valueCharacters)
{
    var settings = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
    for (int index = 0; index < count; index++)
    {
        settings.Add(
            $"setting-{index:D5}",
            StringElement(
                $"{index:D5}-{new string('x', valueCharacters)}"));
    }
    return settings;
}

static JsonElement StringElement(string value)
{
    byte[] bytes = WriteJson(writer => writer.WriteStringValue(value));
    using JsonDocument document = JsonDocument.Parse(bytes);
    return document.RootElement.Clone();
}

static byte[] WriteJson(Action<Utf8JsonWriter> write)
{
    using var stream = new MemoryStream();
    using (var writer = new Utf8JsonWriter(stream))
        write(writer);
    return stream.ToArray();
}

static Shape MeasureJson(byte[] bytes)
{
    using JsonDocument document = JsonDocument.Parse(bytes);
    int nodes = 0;
    int depth = 0;
    int mappingEntries = 0;
    int sequenceEntries = 0;
    int scalarCharacters = 0;
    Visit(document.RootElement, 1);
    return new Shape(
        depth, nodes, 0, mappingEntries, sequenceEntries, scalarCharacters);

    void Visit(JsonElement element, int currentDepth)
    {
        nodes = checked(nodes + 1);
        depth = Math.Max(depth, currentDepth);
        if (element.ValueKind == JsonValueKind.Object)
        {
            JsonProperty[] properties = element.EnumerateObject().ToArray();
            mappingEntries = Math.Max(mappingEntries, properties.Length);
            foreach (JsonProperty property in properties)
            {
                scalarCharacters = checked(scalarCharacters + property.Name.Length);
                Visit(property.Value, checked(currentDepth + 1));
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            JsonElement[] entries = element.EnumerateArray().ToArray();
            sequenceEntries = Math.Max(sequenceEntries, entries.Length);
            foreach (JsonElement entry in entries)
                Visit(entry, checked(currentDepth + 1));
        }
        else if (element.ValueKind is JsonValueKind.String or JsonValueKind.Number
                 or JsonValueKind.True or JsonValueKind.False)
        {
            scalarCharacters = checked(
                scalarCharacters + (element.ValueKind == JsonValueKind.String
                    ? element.GetString()!.Length
                    : element.GetRawText().Length));
        }
    }
}

static Shape MeasureYaml(byte[] bytes)
{
    string text = new UTF8Encoding(false, true).GetString(bytes);
    var parser = new Parser(new StringReader(text));
    int events = 0;
    int nodes = 0;
    int depth = 0;
    int currentDepth = 0;
    int mappingEntries = 0;
    int currentMappingEntries = 0;
    int sequenceEntries = 0;
    int scalarCharacters = 0;
    while (parser.MoveNext())
    {
        events = checked(events + 1);
        switch (parser.Current)
        {
            case YamlDotNet.Core.Events.MappingStart:
                nodes = checked(nodes + 1);
                currentDepth = checked(currentDepth + 1);
                depth = Math.Max(depth, currentDepth);
                currentMappingEntries = 0;
                break;
            case YamlDotNet.Core.Events.MappingEnd:
                mappingEntries = Math.Max(mappingEntries, currentMappingEntries / 2);
                currentDepth--;
                break;
            case YamlDotNet.Core.Events.SequenceStart:
                nodes = checked(nodes + 1);
                currentDepth = checked(currentDepth + 1);
                depth = Math.Max(depth, currentDepth);
                break;
            case YamlDotNet.Core.Events.SequenceEnd:
                currentDepth--;
                break;
            case YamlDotNet.Core.Events.Scalar scalar:
                nodes = checked(nodes + 1);
                currentMappingEntries = checked(currentMappingEntries + 1);
                scalarCharacters = checked(scalarCharacters + scalar.Value.Length);
                break;
        }
    }
    return new Shape(
        depth, nodes, events, mappingEntries, sequenceEntries, scalarCharacters);
}

static long Median(long[] values)
{
    long[] ordered = values.Order().ToArray();
    return ordered[ordered.Length / 2];
}

static long Percentile95(long[] values)
{
    long[] ordered = values.Order().ToArray();
    int index = (int)Math.Ceiling(ordered.Length * 0.95) - 1;
    return ordered[Math.Clamp(index, 0, ordered.Length - 1)];
}

static byte[] WriteGenerated(
    HostEvidence host, IReadOnlyList<ProfileMeasurement> measurements)
{
    using var stream = new MemoryStream();
    using (var writer = new Utf8JsonWriter(
               stream, new JsonWriterOptions { Indented = true }))
    {
        writer.WriteStartObject();
        writer.WriteStartObject("_metadata");
        writer.WriteStartArray("about");
        writer.WriteStringValue(
            "ABOUTME: Generated measured Setup composition profile evidence; do not edit by hand.");
        writer.WriteStringValue(
            "ABOUTME: Owned by eng/setup-assistant/GenerateSetupCompositionScaleProfiles.cs.");
        writer.WriteEndArray();
        writer.WriteString(
            "generatedBy",
            "eng/setup-assistant/GenerateSetupCompositionScaleProfiles.cs");
        writer.WriteEndObject();
        writer.WriteNumber("schemaVersion", 1);
        WriteHost(writer, host);
        WriteCanonicalDefaults(writer);
        writer.WriteStartArray("disabledProfiles");
        writer.WriteStringValue("expanded");
        writer.WriteEndArray();
        writer.WriteStartArray("profiles");
        foreach (ProfileMeasurement measurement in measurements)
            WriteProfile(writer, measurement, includeEvidenceDigest: true);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }
    return WithFinalNewline(stream);
}

static void WriteHost(Utf8JsonWriter writer, HostEvidence host)
{
    writer.WriteStartObject("host");
    writer.WriteString("revision", host.Revision);
    writer.WriteString("os", host.Os);
    writer.WriteString("architecture", host.Architecture);
    writer.WriteNumber("processorCount", host.ProcessorCount);
    writer.WriteNumber("availableMemoryBytes", host.AvailableMemoryBytes);
    writer.WriteNumber("totalMemoryBytes", host.TotalMemoryBytes);
    writer.WriteString("processLimits", host.ProcessLimits);
    writer.WriteString("filesystemSemantics", host.FilesystemSemantics);
    writer.WriteString("sdk", host.Sdk);
    writer.WriteString("runtime", host.Runtime);
    writer.WriteString("commit", host.Commit);
    writer.WriteEndObject();
}

static void WriteCanonicalDefaults(Utf8JsonWriter writer)
{
    writer.WriteStartObject("canonicalDefault");
    writer.WriteNumber("aggregateSourceBytes", SetupCompositionLimits.DefaultAggregateSourceBytes);
    writer.WriteNumber("yamlDocuments", SetupCompositionLimits.DefaultYamlDocuments);
    writer.WriteNumber("parserEvents", SetupCompositionLimits.DefaultParserEvents);
    writer.WriteNumber("normalizedNodes", SetupCompositionLimits.DefaultNormalizedNodes);
    writer.WriteNumber("nestingDepth", SetupCompositionLimits.DefaultNestingDepth);
    writer.WriteNumber("mappingEntries", SetupCompositionLimits.DefaultMappingEntries);
    writer.WriteNumber("sequenceEntries", SetupCompositionLimits.DefaultSequenceEntries);
    writer.WriteNumber("scalarCharacters", SetupCompositionLimits.DefaultScalarCharacters);
    writer.WriteNumber(
        "aggregateScalarCharacters",
        SetupCompositionLimits.DefaultAggregateScalarCharacters);
    writer.WriteNumber("directories", SetupCompositionLimits.DefaultDirectories);
    writer.WriteNumber("files", SetupCompositionLimits.DefaultFiles);
    writer.WriteNumber("entriesPerDirectory", SetupCompositionLimits.DefaultEntriesPerDirectory);
    writer.WriteNumber(
        "relativePathCharacters",
        SetupCompositionLimits.DefaultRelativePathCharacters);
    writer.WriteNumber("pathDepth", SetupCompositionLimits.DefaultPathDepth);
    writer.WriteNumber("perFileBytes", SetupCompositionLimits.DefaultPerFileBytes);
    writer.WriteNumber(
        "aggregateDirectoryBytes",
        SetupCompositionLimits.DefaultAggregateDirectoryBytes);
    writer.WriteNumber(
        "aggregateDirectoryNodes",
        SetupCompositionLimits.DefaultAggregateDirectoryNodes);
    writer.WriteEndObject();
}

static void WriteProfile(
    Utf8JsonWriter writer, ProfileMeasurement measurement, bool includeEvidenceDigest)
{
    writer.WriteStartObject();
    writer.WriteString("name", measurement.Name);
    writer.WriteString("sourceKind", measurement.SourceKind.ToString().ToLowerInvariant());
    writer.WriteBoolean("enabled", measurement.Enabled);
    writer.WriteString("hostRevision", measurement.HostRevision);
    writer.WriteString("sourceRevision", measurement.SourceRevision);
    writer.WriteString("coreRevision", measurement.CoreRevision);
    writer.WriteString("wireRevision", measurement.WireRevision);
    writer.WriteString("targetRevision", measurement.TargetRevision);
    writer.WriteNumber("directories", measurement.Directories);
    writer.WriteNumber("files", measurement.Files);
    writer.WriteNumber("entriesPerDirectory", measurement.EntriesPerDirectory);
    writer.WriteNumber("aggregateSourceBytes", measurement.AggregateSourceBytes);
    writer.WriteNumber("perFileBytes", measurement.PerFileBytes);
    writer.WriteNumber("depth", measurement.Depth);
    writer.WriteNumber("nodes", measurement.Nodes);
    writer.WriteNumber("parserEvents", measurement.ParserEvents);
    writer.WriteNumber("mappingEntries", measurement.MappingEntries);
    writer.WriteNumber("sequenceEntries", measurement.SequenceEntries);
    writer.WriteNumber("scalarCharacters", measurement.ScalarCharacters);
    writer.WriteNumber("canonicalArtifactBytes", measurement.CanonicalArtifactBytes);
    writer.WriteString("canonicalArtifactSha256", measurement.CanonicalArtifactSha256);
    writer.WriteNumber("warmupCount", measurement.WarmupCount);
    writer.WriteNumber("iterationCount", measurement.IterationCount);
    writer.WriteNumber("medianElapsedMicroseconds", measurement.MedianElapsedMicroseconds);
    writer.WriteNumber("p95ElapsedMicroseconds", measurement.P95ElapsedMicroseconds);
    writer.WriteNumber("medianAllocatedBytes", measurement.MedianAllocatedBytes);
    writer.WriteNumber("peakWorkingSetBytes", measurement.PeakWorkingSetBytes);
    writer.WriteNumber("gen0Collections", measurement.Gen0Collections);
    writer.WriteNumber("gen1Collections", measurement.Gen1Collections);
    writer.WriteNumber("gen2Collections", measurement.Gen2Collections);
    writer.WriteString("stackOverflowDisposition", measurement.StackOverflowDisposition);
    writer.WriteBoolean("cancellationObserved", measurement.CancellationObserved);
    writer.WriteBoolean("targetAccepted", measurement.TargetAccepted);
    if (includeEvidenceDigest)
        writer.WriteString("evidenceDigest", measurement.EvidenceDigest);
    writer.WriteEndObject();
}

static string EvidenceDigest(ProfileMeasurement measurement)
{
    using var stream = new MemoryStream();
    using (var writer = new Utf8JsonWriter(stream))
        WriteProfile(writer, measurement, includeEvidenceDigest: false);
    return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
}

static string WriteEvidenceMarkdown(
    HostEvidence host, IReadOnlyList<ProfileMeasurement> measurements)
{
    var builder = new StringBuilder();
    builder.AppendLine("<!-- ABOUTME: Records controlled Setup composition scale measurements and admission evidence. -->");
    builder.AppendLine("<!-- ABOUTME: Contains synthetic aggregate facts only; canonical defaults remain unchanged. -->");
    builder.AppendLine();
    builder.AppendLine("# Setup Composition Scale Results");
    builder.AppendLine();
    builder.AppendLine("## Host And Revision Evidence");
    builder.AppendLine();
    builder.AppendLine(FormattableString.Invariant($"- OS: `{host.Os}`"));
    builder.AppendLine(FormattableString.Invariant(
        $"- Architecture: `{host.Architecture}`"));
    builder.AppendLine(FormattableString.Invariant($"- CPU count: `{host.ProcessorCount}`"));
    builder.AppendLine(FormattableString.Invariant(
        $"- Available memory bytes: `{host.AvailableMemoryBytes}`"));
    builder.AppendLine(FormattableString.Invariant(
        $"- Total memory bytes: `{host.TotalMemoryBytes}`"));
    builder.AppendLine(FormattableString.Invariant(
        $"- Filesystem semantics: `{host.FilesystemSemantics}`"));
    builder.AppendLine(FormattableString.Invariant($"- SDK: `{host.Sdk}`"));
    builder.AppendLine(FormattableString.Invariant($"- Runtime: `{host.Runtime}`"));
    builder.AppendLine(FormattableString.Invariant($"- Commit: `{host.Commit}`"));
    builder.AppendLine(FormattableString.Invariant(
        $"- Host revision: `{host.Revision}`"));
    builder.AppendLine();
    builder.AppendLine("Process limits:");
    builder.AppendLine();
    builder.AppendLine("```text");
    builder.AppendLine(host.ProcessLimits);
    builder.AppendLine("```");
    builder.AppendLine();
    builder.AppendLine("## Measurements");
    builder.AppendLine();
    builder.AppendLine("| Profile | Source | Bytes | Files | Nodes | Events | Canonical bytes | Median us | p95 us | Median allocation | Peak working set | Target | Evidence |");
    builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---|");
    foreach (ProfileMeasurement item in measurements)
    {
        builder.AppendLine(FormattableString.Invariant(
            $"| {item.Name} | {item.SourceKind} | {item.AggregateSourceBytes} | {item.Files} | {item.Nodes} | {item.ParserEvents} | {item.CanonicalArtifactBytes} | {item.MedianElapsedMicroseconds} | {item.P95ElapsedMicroseconds} | {item.MedianAllocatedBytes} | {item.PeakWorkingSetBytes} | {(item.TargetAccepted ? "accepted" : "rejected")} | `{item.EvidenceDigest}` |"));
    }
    builder.AppendLine();
    builder.AppendLine("All four profiles use synthetic non-secret settings. Each successful result was");
    builder.AppendLine("strictly reparsed by the target Wire codec, remained byte-identical across");
    builder.AppendLine(FormattableString.Invariant(
        $"{IterationCount} measured iterations after {WarmupCount} warmups, and returned"));
    builder.AppendLine("the closed `Cancelled` outcome with no artifact for a pre-cancelled run.");
    builder.AppendLine("The `ceiling` shape reaches exactly 4,096 entries in one mapping.");
    builder.AppendLine();
    builder.AppendLine("## Admission Decision");
    builder.AppendLine();
    builder.AppendLine("`small`, `medium`, `large`, and `ceiling` are enabled only for their exact");
    builder.AppendLine("generated evidence digest and when the target advertises at least the measured");
    builder.AppendLine("canonical artifact byte capacity. `expanded` is a known disabled profile.");
    builder.AppendLine("Unknown, disabled, evidence-mismatched, and target-incompatible requests return");
    builder.AppendLine("distinct closed failures with no profile, clamp, fallback, or default replacement.");
    builder.AppendLine("All effective parser limits remain `SetupCompositionLimits.Default`.");
    return builder.ToString();
}

static void ValidateGeneratedEvidence(
    string generatedPath,
    string evidencePath,
    string sourceRevision,
    string coreRevision,
    string wireRevision,
    string targetRevision)
{
    if (!File.Exists(generatedPath) || !File.Exists(evidencePath))
        throw new InvalidOperationException("missing-generated-scale-evidence");
    using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(generatedPath));
    JsonElement root = document.RootElement;
    if (root.GetProperty("schemaVersion").GetInt32() != 1)
        throw new InvalidOperationException("unsupported-scale-schema");
    JsonElement[] profiles = root.GetProperty("profiles").EnumerateArray().ToArray();
    string[] expected = ["small", "medium", "large", "ceiling"];
    if (!profiles.Select(item => item.GetProperty("name").GetString())
        .SequenceEqual(expected, StringComparer.Ordinal))
        throw new InvalidOperationException("scale-profile-set-drifted");
    foreach (JsonElement profile in profiles)
    {
        if (!profile.GetProperty("enabled").GetBoolean()
            || !profile.GetProperty("targetAccepted").GetBoolean()
            || !profile.GetProperty("cancellationObserved").GetBoolean()
            || profile.GetProperty("sourceRevision").GetString() != sourceRevision
            || profile.GetProperty("coreRevision").GetString() != coreRevision
            || profile.GetProperty("wireRevision").GetString() != wireRevision
            || profile.GetProperty("targetRevision").GetString() != targetRevision
            || profile.GetProperty("evidenceDigest").GetString()
                != EvidenceDigestFromJson(profile))
            throw new InvalidOperationException(
                $"invalid-scale-profile:{profile.GetProperty("name").GetString()}");
    }
    if (!File.ReadAllText(evidencePath).Contains(
            "# Setup Composition Scale Results", StringComparison.Ordinal))
        throw new InvalidOperationException("invalid-scale-evidence-document");
}

static string EvidenceDigestFromJson(JsonElement profile)
{
    using var stream = new MemoryStream();
    using (var writer = new Utf8JsonWriter(stream))
    {
        writer.WriteStartObject();
        foreach (JsonProperty property in profile.EnumerateObject())
        {
            if (property.NameEquals("evidenceDigest"))
                continue;
            property.WriteTo(writer);
        }
        writer.WriteEndObject();
    }
    return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
}

static async Task VerifyCanonicalOutputsAsync(
    string generatedPath, IReadOnlyList<ProfileSpec> specs)
{
    using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(generatedPath));
    Dictionary<string, JsonElement> generated = document.RootElement
        .GetProperty("profiles").EnumerateArray()
        .ToDictionary(
            item => item.GetProperty("name").GetString()!,
            item => item.Clone(),
            StringComparer.Ordinal);
    var compiler = new SetupCompositionCompiler();
    foreach (ProfileSpec spec in specs)
    {
        ProfileInput input = CreateInput(spec);
        try
        {
            SetupCompositionResult result = await compiler.CompileAsync(input.Source);
            RequireSuccess(spec.Name, result);
            string actual = Convert.ToHexStringLower(
                SHA256.HashData(result.CanonicalBytes.Span));
            JsonElement expected = generated[spec.Name];
            if (actual != expected.GetProperty("canonicalArtifactSha256").GetString()
                || result.CanonicalBytes.Length
                    != expected.GetProperty("canonicalArtifactBytes").GetInt32())
                throw new InvalidOperationException($"canonical-profile-drifted:{spec.Name}");
        }
        finally
        {
            input.Cleanup();
        }
    }
}

static HostEvidence ReadHostEvidence(string repositoryRoot)
{
    (long total, long available) = ReadLinuxMemory();
    string processLimits = OperatingSystem.IsLinux() && File.Exists("/proc/self/limits")
        ? File.ReadAllText("/proc/self/limits").Trim()
        : "unavailable";
    var preliminary = new HostEvidence(
        string.Empty,
        RuntimeInformation.OSDescription.Trim(),
        RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
        Environment.ProcessorCount,
        available > 0 ? available : GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
        total > 0 ? total : GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
        processLimits,
        OperatingSystem.IsLinux()
            ? "linux-openat2-beneath-no-links"
            : "directory-profile-disabled",
        Run(repositoryRoot, "dotnet", "--version"),
        RuntimeInformation.FrameworkDescription,
        Run(repositoryRoot, "git", "rev-parse HEAD"));
    string revision = HashUtf8(string.Join(
        '\n',
        preliminary.Os,
        preliminary.Architecture,
        preliminary.ProcessorCount.ToString(CultureInfo.InvariantCulture),
        preliminary.AvailableMemoryBytes.ToString(CultureInfo.InvariantCulture),
        preliminary.TotalMemoryBytes.ToString(CultureInfo.InvariantCulture),
        preliminary.ProcessLimits,
        preliminary.FilesystemSemantics,
        preliminary.Sdk,
        preliminary.Runtime,
        preliminary.Commit));
    return preliminary with { Revision = revision };
}

static (long Total, long Available) ReadLinuxMemory()
{
    if (!OperatingSystem.IsLinux() || !File.Exists("/proc/meminfo"))
        return (0, 0);
    long total = 0;
    long available = 0;
    foreach (string line in File.ReadLines("/proc/meminfo"))
    {
        if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
            total = ParseKilobytes(line);
        else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
            available = ParseKilobytes(line);
    }
    return (total, available);
}

static long ParseKilobytes(string line)
{
    string[] parts = line.Split(
        ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    return checked(long.Parse(parts[1], CultureInfo.InvariantCulture) * 1024);
}

static string Run(string workingDirectory, string fileName, string argument)
{
    using var process = new Process
    {
        StartInfo = new ProcessStartInfo(fileName, argument)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        }
    };
    process.Start();
    string output = process.StandardOutput.ReadToEnd();
    string error = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0)
        throw new InvalidOperationException(
            $"command-failed:{fileName}:{process.ExitCode}:{error.Trim()}");
    return output.Trim();
}

static string HashFiles(string root, IReadOnlyList<string> relativePaths)
{
    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    foreach (string relative in relativePaths.Order(StringComparer.Ordinal))
    {
        hash.AppendData(Encoding.UTF8.GetBytes(relative));
        hash.AppendData([0]);
        hash.AppendData(File.ReadAllBytes(Path.Combine(root, relative)));
        hash.AppendData([0]);
    }
    return Convert.ToHexStringLower(hash.GetHashAndReset());
}

static string HashUtf8(string value) =>
    Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

static byte[] WithFinalNewline(MemoryStream stream)
{
    byte[] content = stream.ToArray();
    byte[] result = new byte[content.Length + 1];
    content.CopyTo(result, 0);
    result[^1] = (byte)'\n';
    return result;
}

static string FindRepositoryRoot()
{
    DirectoryInfo? current = new(Directory.GetCurrentDirectory());
    while (current is not null)
    {
        if (File.Exists(Path.Combine(current.FullName, "AGENTS.md"))
            && Directory.Exists(Path.Combine(current.FullName, "schemas")))
            return current.FullName;
        current = current.Parent;
    }
    throw new InvalidOperationException("repository-root-not-found");
}

internal sealed record ProfileSpec(
    string Name,
    SetupCompositionSourceKind SourceKind,
    int SettingCount,
    int ValueCharacters,
    int DirectoryFiles);

internal sealed record Shape(
    int Depth,
    int Nodes,
    int ParserEvents,
    int MappingEntries,
    int SequenceEntries,
    int ScalarCharacters);

internal sealed class ProfileInput
{
    internal ProfileInput(
        SetupCompositionSource source,
        string? temporaryDirectory,
        int directories,
        int files,
        int entriesPerDirectory,
        int aggregateSourceBytes,
        int perFileBytes,
        int depth,
        int nodes,
        int parserEvents,
        int mappingEntries,
        int sequenceEntries,
        int scalarCharacters)
    {
        Source = source;
        TemporaryDirectory = temporaryDirectory;
        Directories = directories;
        Files = files;
        EntriesPerDirectory = entriesPerDirectory;
        AggregateSourceBytes = aggregateSourceBytes;
        PerFileBytes = perFileBytes;
        Depth = depth;
        Nodes = nodes;
        ParserEvents = parserEvents;
        MappingEntries = mappingEntries;
        SequenceEntries = sequenceEntries;
        ScalarCharacters = scalarCharacters;
    }

    internal SetupCompositionSource Source { get; }
    internal string? TemporaryDirectory { get; }
    internal int Directories { get; }
    internal int Files { get; }
    internal int EntriesPerDirectory { get; }
    internal int AggregateSourceBytes { get; }
    internal int PerFileBytes { get; }
    internal int Depth { get; }
    internal int Nodes { get; }
    internal int ParserEvents { get; }
    internal int MappingEntries { get; }
    internal int SequenceEntries { get; }
    internal int ScalarCharacters { get; }

    internal void Cleanup()
    {
        if (TemporaryDirectory is not null && Directory.Exists(TemporaryDirectory))
            Directory.Delete(TemporaryDirectory, recursive: true);
    }
}

internal sealed record HostEvidence(
    string Revision,
    string Os,
    string Architecture,
    int ProcessorCount,
    long AvailableMemoryBytes,
    long TotalMemoryBytes,
    string ProcessLimits,
    string FilesystemSemantics,
    string Sdk,
    string Runtime,
    string Commit);

internal sealed record ProfileMeasurement(
    string Name,
    SetupCompositionSourceKind SourceKind,
    bool Enabled,
    string EvidenceDigest,
    string HostRevision,
    string SourceRevision,
    string CoreRevision,
    string WireRevision,
    string TargetRevision,
    int Directories,
    int Files,
    int EntriesPerDirectory,
    int AggregateSourceBytes,
    int PerFileBytes,
    int Depth,
    int Nodes,
    int ParserEvents,
    int MappingEntries,
    int SequenceEntries,
    int ScalarCharacters,
    int CanonicalArtifactBytes,
    string CanonicalArtifactSha256,
    int WarmupCount,
    int IterationCount,
    long MedianElapsedMicroseconds,
    long P95ElapsedMicroseconds,
    long MedianAllocatedBytes,
    long PeakWorkingSetBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    string StackOverflowDisposition,
    bool CancellationObserved,
    bool TargetAccepted);
