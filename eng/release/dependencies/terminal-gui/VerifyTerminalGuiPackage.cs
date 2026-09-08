#:property RestorePackagesWithLockFile=false
#:property NoWarn=IL2026;IL3050
#:property JsonSerializerIsReflectionEnabledByDefault=true

using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Xml.Linq;

const string PackageId = "ISLAMU.Terminal.Gui";
const string PackageVersion = "2.4.17-islamu.1";
const string UpstreamTag = "v2.4.17";
const string UpstreamTagObject = "58f3af1a4afe5d2772be134b2299a0f78f35c93c";
const string UpstreamCommit = "d0a0ed9b150d3fc8aacf4ab07b7f7d91264fe6d6";

if (args is not ["--write"] and not ["--check"])
{
    Console.Error.WriteLine("Usage: VerifyTerminalGuiPackage.cs (--write|--check)");
    return 64;
}

string root = FindRepositoryRoot();
string directory = Path.Combine(root, "eng", "release", "dependencies", "terminal-gui");
string patchPath = Path.Combine(directory, "patches", "0001-remove-textmate-grammars.patch");
string sourcePath = Path.Combine(directory, "source.json");
string approvalPath = Path.Combine(directory, "approval.json");
string packagePath = Path.Combine(directory, "feed", $"{PackageId}.{PackageVersion}.nupkg");
string lockPath = Path.Combine(directory, "probe", "packages.lock.json");
string evidencePath = Path.Combine(directory, "generated", "package-evidence.json");
string sbomPath = Path.Combine(directory, "generated", "terminal-gui.cdx.json");

foreach (string required in new[] { patchPath, sourcePath, approvalPath, packagePath, lockPath })
{
    if (!File.Exists(required))
    {
        Console.Error.WriteLine($"Missing Terminal.Gui package input: {Path.GetRelativePath(root, required)}");
        return 1;
    }
}

PackageFacts package = ReadPackage(packagePath);
LockFacts closure = ReadLock(lockPath);
ApprovalFacts approval = JsonSerializer.Deserialize<ApprovalFacts>(File.ReadAllBytes(approvalPath), JsonOptions())
    ?? throw new InvalidDataException("Terminal.Gui approval ratchet is invalid");
Validate(package, closure, approval, Sha256File(patchPath));

byte[] evidence = SerializeEvidence(package, closure, Sha256File(patchPath));
byte[] sbom = SerializeSbom(package, closure);

if (args[0] == "--write")
{
    Directory.CreateDirectory(Path.GetDirectoryName(evidencePath)!);
    File.WriteAllBytes(evidencePath, evidence);
    File.WriteAllBytes(sbomPath, sbom);
    Console.WriteLine("Generated Terminal.Gui package evidence and CycloneDX SBOM.");
    return 0;
}

string[] stale = new[] { (evidencePath, evidence), (sbomPath, sbom) }
    .Where(output => !File.Exists(output.Item1)
        || !File.ReadAllBytes(output.Item1).AsSpan().SequenceEqual(output.Item2))
    .Select(output => Path.GetRelativePath(root, output.Item1).Replace('\\', '/'))
    .ToArray();
if (stale.Length > 0)
{
    Console.Error.WriteLine("Terminal.Gui evidence is missing or stale: " + string.Join(", ", stale));
    return 1;
}

Console.WriteLine($"Verified {PackageId} {PackageVersion}: {closure.Components.Count} components, no TextMate graph.");
return 0;

static PackageFacts ReadPackage(string path)
{
    using ZipArchive archive = ZipFile.OpenRead(path);
    string[] entries = archive.Entries.Select(entry => entry.FullName).Order(StringComparer.Ordinal).ToArray();
    string[] forbiddenEntries = entries.Where(IsTextMate).ToArray();
    if (forbiddenEntries.Length > 0)
        throw new InvalidDataException("Package contains forbidden TextMate entries: " + string.Join(", ", forbiddenEntries));

    ZipArchiveEntry nuspecEntry = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
    using Stream nuspecStream = nuspecEntry.Open();
    XDocument nuspec = XDocument.Load(nuspecStream, LoadOptions.None);
    XNamespace ns = nuspec.Root!.Name.Namespace;
    XElement metadata = nuspec.Root.Element(ns + "metadata")!;
    string id = metadata.Element(ns + "id")?.Value ?? string.Empty;
    string version = metadata.Element(ns + "version")?.Value ?? string.Empty;
    string license = metadata.Element(ns + "license")?.Value ?? string.Empty;
    string repositoryCommit = metadata.Element(ns + "repository")?.Attribute("commit")?.Value ?? string.Empty;
    string[] dependencies = metadata.Descendants(ns + "dependency")
        .Select(element => element.Attribute("id")?.Value ?? string.Empty)
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    ZipArchiveEntry assemblyEntry = archive.GetEntry("lib/net10.0/Terminal.Gui.dll")
        ?? throw new InvalidDataException("Package is missing lib/net10.0/Terminal.Gui.dll");
    using Stream assemblyStream = assemblyEntry.Open();
    using var assemblyBuffer = new MemoryStream();
    assemblyStream.CopyTo(assemblyBuffer);
    byte[] assemblyBytes = assemblyBuffer.ToArray();
    using var peReader = new PEReader(new MemoryStream(assemblyBytes, writable: false));
    MetadataReader reader = peReader.GetMetadataReader();
    string[] assemblyReferences = reader.AssemblyReferences
        .Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name))
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToArray();
    string[] typeNames = reader.TypeDefinitions
        .Select(handle => reader.GetTypeDefinition(handle))
        .Select(type => $"{reader.GetString(type.Namespace)}.{reader.GetString(type.Name)}")
        .Order(StringComparer.Ordinal)
        .ToArray();

    return new(
        path,
        id,
        version,
        license,
        repositoryCommit,
        dependencies,
        entries,
        assemblyReferences,
        typeNames,
        Sha256File(path),
        Sha256Bytes(assemblyBytes));
}

static LockFacts ReadLock(string path)
{
    JsonObject root = JsonNode.Parse(File.ReadAllText(path))?.AsObject()
        ?? throw new InvalidDataException("Terminal.Gui probe lock is not a JSON object");
    JsonObject frameworks = root["dependencies"]?.AsObject()
        ?? throw new InvalidDataException("Terminal.Gui probe lock has no dependency frameworks");
    if (frameworks.Count != 1)
        throw new InvalidDataException("Terminal.Gui probe lock must contain exactly one framework");
    JsonObject nodes = frameworks.Single().Value?.AsObject()
        ?? throw new InvalidDataException("Terminal.Gui probe lock framework is invalid");

    var components = new List<LockComponent>();
    foreach ((string name, JsonNode? value) in nodes.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
    {
        JsonObject node = value?.AsObject() ?? throw new InvalidDataException($"Invalid lock node: {name}");
        string version = node["resolved"]?.GetValue<string>() ?? throw new InvalidDataException($"Missing resolved version: {name}");
        string type = node["type"]?.GetValue<string>() ?? throw new InvalidDataException($"Missing relationship: {name}");
        string contentHash = node["contentHash"]?.GetValue<string>() ?? string.Empty;
        string requested = node["requested"]?.GetValue<string>() ?? string.Empty;
        string[] dependencies = node["dependencies"] is JsonObject children
            ? children.Select(child => child.Key).Order(StringComparer.OrdinalIgnoreCase).ToArray()
            : [];
        components.Add(new(name, version, type, requested, contentHash, dependencies));
    }

    return new(components);
}

static void Validate(PackageFacts package, LockFacts closure, ApprovalFacts approval, string patchHash)
{
    string closureHash = ClosureSha256(closure);
    if (approval.PackageId != PackageId || approval.PackageVersion != PackageVersion
        || approval.UpstreamCommit != UpstreamCommit || approval.PatchSha256 != patchHash
        || approval.PackageSha256 != package.PackageSha256
        || approval.AssemblySha256 != package.AssemblySha256
        || approval.ClosureSha256 != closureHash)
        throw new InvalidDataException(
            $"Terminal.Gui package exceeds the Project Steward-approved patch or closure: {patchHash}/{package.PackageSha256}/{package.AssemblySha256}/{closureHash}");
    if (package.Id != PackageId || package.Version != PackageVersion || package.License != "MIT"
        || package.RepositoryCommit != UpstreamCommit)
        throw new InvalidDataException("Terminal.Gui package identity, license, or upstream commit is invalid");
    if (!package.Entries.Contains("LICENSE", StringComparer.Ordinal)
        || !package.Entries.Contains("ISLAMU-NOTICE.md", StringComparer.Ordinal))
        throw new InvalidDataException("Terminal.Gui package must contain LICENSE and ISLAMU-NOTICE.md");
    if (package.Dependencies.Any(IsTextMate)
        || package.AssemblyReferences.Any(IsTextMate)
        || package.TypeNames.Any(IsTextMate)
        || closure.Components.Any(component => IsTextMate(component.Name)))
        throw new InvalidDataException("TextMateSharp or its grammar corpus re-entered the package closure");
    LockComponent root = closure.Components.Single(component => component.Name == PackageId);
    if (root.Version != PackageVersion || root.Type != "Direct")
        throw new InvalidDataException("Terminal.Gui probe must reference the exact internal package directly");
    if (closure.Components.Any(component => component.Name.Equals("Terminal.Gui", StringComparison.OrdinalIgnoreCase)))
        throw new InvalidDataException("Official Terminal.Gui package identity re-entered the closure");
}

static string ClosureSha256(LockFacts closure)
{
    string value = string.Join('\n', closure.Components.Select(component =>
        $"{component.Name}|{component.Version}|{component.Type}|{component.Requested}|"
        + $"{(component.Name == PackageId ? string.Empty : component.ContentHash)}|"
        + string.Join(',', component.Dependencies))) + "\n";
    return Sha256Bytes(Encoding.UTF8.GetBytes(value));
}

static byte[] SerializeEvidence(PackageFacts package, LockFacts closure, string patchHash)
{
    var value = new
    {
        _metadata = new
        {
            about = new[]
            {
                "ABOUTME: Generated evidence for the exact ISLAMU Terminal.Gui downstream package.",
                "ABOUTME: Owned by eng/release/dependencies/terminal-gui/VerifyTerminalGuiPackage.cs."
            },
            generatedBy = "eng/release/dependencies/terminal-gui/VerifyTerminalGuiPackage.cs"
        },
        package = new { id = PackageId, version = PackageVersion, sha256 = package.PackageSha256 },
        upstream = new { tag = UpstreamTag, tagObject = UpstreamTagObject, commit = UpstreamCommit },
        patch = new { path = "patches/0001-remove-textmate-grammars.patch", sha256 = patchHash },
        assembly = new { name = "Terminal.Gui.dll", sha256 = package.AssemblySha256 },
        license = "MIT",
        dependencies = closure.Components.Select(component => new
        {
            component.Name,
            component.Version,
            relationship = component.Type,
            requested = component.Requested,
            contentHash = component.ContentHash,
            dependencies = component.Dependencies
        }),
        reproducibility = new
        {
            semanticInputsPinned = true,
            archiveByteReproducible = false,
            limitation = "NuGet pack emits random OPC core-property part names; verify assembly, nuspec, closure, notice, and inventory instead."
        }
    };
    return JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions());
}

static byte[] SerializeSbom(PackageFacts package, LockFacts closure)
{
    var components = closure.Components
        .Where(component => component.Name != PackageId)
        .Select(component => new CycloneDxComponent(
            "library",
            Purl(component.Name, component.Version),
            component.Name,
            component.Version,
            [new("SHA-512", Convert.ToHexString(Convert.FromBase64String(component.ContentHash)).ToLowerInvariant())],
            Purl(component.Name, component.Version),
            null));
    var value = new
    {
        bomFormat = "CycloneDX",
        specVersion = "1.6",
        serialNumber = "urn:uuid:5bb64d1b-5f17-57c5-9c8c-6d8380cbd5fa",
        version = 1,
        metadata = new
        {
            component = new CycloneDxComponent(
                "library",
                Purl(PackageId, PackageVersion),
                PackageId,
                PackageVersion,
                [new("SHA-256", package.PackageSha256)],
                Purl(PackageId, PackageVersion),
                [new(new("MIT"))])
        },
        components,
        dependencies = closure.Components.Select(component => new
        {
            @ref = Purl(component.Name, component.Version),
            dependsOn = component.Dependencies.Select(name =>
            {
                LockComponent target = closure.Components.Single(candidate =>
                    candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                return Purl(target.Name, target.Version);
            })
        })
    };
    return JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions());
}

static JsonSerializerOptions JsonOptions() => new()
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};
static string Purl(string name, string version) => $"pkg:nuget/{Uri.EscapeDataString(name)}@{Uri.EscapeDataString(version)}";
static bool IsTextMate(string value) => value.Contains("TextMate", StringComparison.OrdinalIgnoreCase)
    || value.Contains("tmLanguage", StringComparison.OrdinalIgnoreCase);
static string Sha256File(string path) => Sha256Bytes(File.ReadAllBytes(path));
static string Sha256Bytes(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

static string FindRepositoryRoot()
{
    for (DirectoryInfo? directory = new(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
    {
        if (File.Exists(Path.Combine(directory.FullName, "Explore.slnx"))) return directory.FullName;
    }

    throw new InvalidOperationException("Repository root was not found");
}

internal sealed record PackageFacts(
    string Path,
    string Id,
    string Version,
    string License,
    string RepositoryCommit,
    string[] Dependencies,
    string[] Entries,
    string[] AssemblyReferences,
    string[] TypeNames,
    string PackageSha256,
    string AssemblySha256);

internal sealed record LockFacts(IReadOnlyList<LockComponent> Components);
internal sealed record ApprovalFacts(
    string PackageId,
    string PackageVersion,
    string UpstreamCommit,
    string PatchSha256,
    string PackageSha256,
    string AssemblySha256,
    string ClosureSha256);
internal sealed record LockComponent(
    string Name,
    string Version,
    string Type,
    string Requested,
    string ContentHash,
    string[] Dependencies);

internal sealed record CycloneDxHash(string Alg, string Content);
internal sealed record CycloneDxLicenseChoice(CycloneDxLicense License);
internal sealed record CycloneDxLicense(string Id);
internal sealed record CycloneDxComponent(
    string Type,
    [property: JsonPropertyName("bom-ref")] string BomRef,
    string Name,
    string Version,
    CycloneDxHash[] Hashes,
    string Purl,
    CycloneDxLicenseChoice[]? Licenses);
