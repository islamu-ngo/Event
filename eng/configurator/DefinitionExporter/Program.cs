namespace ISLAMU.Event.DefinitionExporter;

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args is ["--write-environment"])
                return EnvironmentCatalogueContract.Run(["--write"]);
            if (args is ["--check-environment"])
                return EnvironmentCatalogueContract.Run(["--check"]);
            string root = Root();
            using JsonDocument schema = JsonDocument.Parse(ReadBounded(Path.Combine(root, "schemas", "configuration-manifest-v1alpha2.schema.json")));
            var host = new HostArtifacts(schema.RootElement);
            if (args is ["--verify", var artifact, var input])
            {
                _ = host.Validate(artifact, ReadBounded(input));
                Console.WriteLine("host-wire-valid:" + artifact);
                return 0;
            }
            if (args is ["--prepare-identity", var documentPath, var manifestPath])
            {
                using JsonDocument document = JsonDocument.Parse(ReadBounded(documentPath));
                byte[] bytes = OperatorIdentityManifestJson.Serialize(OperatorIdentityManifestJson.Create(document.RootElement));
                WriteNew(manifestPath, bytes);
                Console.WriteLine("identity-prepared:host-revision-preserved");
                return 0;
            }
            string inputs;
            string[] destinations;
            bool check;
            if (args is ["--write" or "--check", var configurator])
            {
                inputs = Path.Combine(root, "configurator", "definitions", "islamu-event", "inputs");
                destinations =
                [
                    Path.Combine(root, "configurator", "definitions", "islamu-event"),
                    Path.Combine(Path.GetFullPath(configurator), "examples", "islamu-event")
                ];
                check = args[0] == "--check";
            }
            else if (args is ["--export", var source, var destination])
            {
                inputs = Path.GetFullPath(source);
                destinations = [Path.GetFullPath(destination)];
                if (Directory.Exists(destinations[0]))
                    throw new InvalidDataException("export-destination-must-be-new");
                check = false;
            }
            else
            {
                Console.Error.WriteLine("Usage: DefinitionExporter (--write|--check) CONFIGURATOR_ROOT"
                    + " | --export INPUT_DIRECTORY NEW_OUTPUT_DIRECTORY"
                    + " | --verify ARTIFACT_ID INPUT | --prepare-identity DOCUMENT NEW_MANIFEST");
                return 64;
            }
            using JsonDocument profile = JsonDocument.Parse(ReadBounded(Path.Combine(inputs, "profile.json")));
            long revision = profile.RootElement.GetProperty("revision").GetInt64();
            if (profile.RootElement.GetProperty("familyId").GetString() != DefinitionBuilder.FamilyId)
                throw new InvalidDataException("definition-family-conflict");
            string version = profile.RootElement.GetProperty("applicationVersion").GetString()!;
            int issuerCount = profile.RootElement.TryGetProperty("trustedIssuerCount", out JsonElement count)
                ? count.GetInt32() : 1;
            if (issuerCount is < 1 or > 128)
                throw new InvalidDataException("trusted-issuer-count-invalid");
            if (revision < 1 || string.IsNullOrWhiteSpace(version) || version.Length > 128)
                throw new InvalidDataException("definition-identity-invalid");
            byte[] catalogueBytes = EnvironmentMetadata.Export();
            using JsonDocument catalogue = JsonDocument.Parse(catalogueBytes);
            Dictionary<string, byte[]> sources = DefinitionBuilder.ArtifactIds.Where(id => id != "environment")
                .ToDictionary(id => id, id => ReadBounded(Path.Combine(inputs, id + ".json")), StringComparer.Ordinal);
            var builder = new DefinitionBuilder(host);
            JsonObject definition = builder.Build(catalogue.RootElement, sources, revision, version, issuerCount);
            byte[] canonical = CanonicalJson.Bytes(definition);
            // The browser restricts answer controls, not definition identity.
            // Metadata contains no supplied answers, private defaults or private literals.
            byte[] publicBytes = canonical;
            var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["definition.json"] = canonical,
                ["setup-definition.json"] = canonical,
                ["public-definition.json"] = publicBytes,
                ["catalogue.json"] = catalogueBytes,
                [".env.example"] = ReadBounded(Path.Combine(root, ".env.example"))
            };
            var artifactFacts = new JsonArray();
            foreach ((string id, JsonObject answers) in builder.Answers)
            {
                files["answers/" + id + ".json"] = CanonicalJson.Bytes(answers);
                if (id != "environment")
                {
                    using JsonDocument normalized = JsonDocument.Parse(host.Validate(id, sources[id]));
                    byte[] expected = CanonicalJson.Bytes(normalized.RootElement);
                    files["expected/" + id + ".json"] = expected;
                    artifactFacts.Add(new JsonObject { ["id"] = id, ["wireDigest"] = CanonicalJson.Digest(expected) });
                }
                else
                {
                    // This fixed host vector is independent of Configurator's composer.
                    files["expected/environment.env"] = host.Validate(id,
                        ReadBounded(Path.Combine(inputs, "environment.env")));
                    artifactFacts.Add(new JsonObject { ["id"] = id, ["wireValidation"] = "dotenv-runtime-and-host-issuer-check" });
                }
            }
            files["release.json"] = CanonicalJson.Bytes(new JsonObject
            {
                ["familyId"] = DefinitionBuilder.FamilyId, ["revision"] = revision,
                ["applicationVersion"] = version, ["definitionDigest"] = CanonicalJson.Digest(canonical),
                ["publicDefinitionDigest"] = CanonicalJson.Digest(publicBytes),
                ["generatedBy"] = "eng/configurator/DefinitionExporter",
                ["sourceRepository"] = "https://github.com/ISLAMU/Event",
                ["definitionSchema"] = "Configurator/schemas/setup-definition-v1.schema.json",
                ["hostAcceptance"] = "Event wire parser and backend semantic validation",
                ["rendererEquivalence"] = "not-established-for-specialized-workflows",
                ["dependencyProvenance"] = "Event-owned Domain, Wire and environment metadata; no Configurator runtime dependency",
                ["canonicalProfile"] = "configurator-v1-ordinal-decimal-system-text-json",
                ["environmentCatalogueDigest"] = CanonicalJson.Digest(catalogueBytes),
                ["portableSchemaDigest"] = CanonicalJson.Digest(ReadBounded(Path.Combine(root, "schemas", "configuration-manifest-v1alpha2.schema.json"))),
                ["fieldCount"] = definition["fields"]!.AsArray().Count,
                ["environmentFieldCount"] = catalogue.RootElement.GetProperty("definitions").GetArrayLength(),
                ["trustedIssuerCount"] = issuerCount,
                ["artifactCount"] = artifactFacts.Count, ["artifacts"] = artifactFacts
            });
            if (!check)
            {
                // Validate every identity before publishing any generated answer or receipt.
                foreach (string destination in destinations)
                    foreach (string name in files.Keys.Where(name =>
                                 name is "definition.json" or "setup-definition.json" or "public-definition.json"))
                    {
                        string path = Path.Combine(destination, name);
                        if (File.Exists(path))
                            CheckRevision(path, revision, files[name]);
                    }
                if (args is ["--write", var identityRoot])
                {
                    string browserIdentity = Path.Combine(Path.GetFullPath(identityRoot),
                        "src", "Configurator.Browser", "Definitions", "islamu-event.public.json");
                    if (File.Exists(browserIdentity))
                        CheckRevision(browserIdentity, revision, publicBytes);
                }
                if (args is ["--export", _, _])
                {
                    string releasedIdentity = Path.Combine(root, "configurator", "definitions",
                        "islamu-event", "setup-definition.json");
                    if (File.Exists(releasedIdentity))
                        CheckRevision(releasedIdentity, revision, canonical);
                }
            }
            foreach (string destination in destinations)
                foreach ((string name, byte[] bytes) in files)
                {
                    string path = Path.Combine(destination, name);
                    if (check)
                    {
                        if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
                            throw new InvalidDataException("definition-artifact-stale:" + name);
                    }
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        if (File.Exists(path))
                        {
                            // Only the two declared generated output directories reach this branch.
                            // Do not re-publish a changed document under an existing family/revision.
                            if (name is "definition.json" or "setup-definition.json" or "public-definition.json")
                                CheckRevision(path, revision, bytes);
                            File.WriteAllBytes(path, bytes);
                        }
                        else
                            WriteNew(path, bytes);
                    }
                }
            if (args is ["--write" or "--check", var browserRoot])
            {
                string browserPath = Path.Combine(Path.GetFullPath(browserRoot), "src", "Configurator.Browser",
                    "Definitions", "islamu-event.public.json");
                if (check)
                {
                    if (!File.Exists(browserPath) || !File.ReadAllBytes(browserPath).AsSpan().SequenceEqual(publicBytes))
                        throw new InvalidDataException("definition-artifact-stale:islamu-event.public.json");
                }
                else
                {
                    if (File.Exists(browserPath))
                        CheckRevision(browserPath, revision, publicBytes);
                    Directory.CreateDirectory(Path.GetDirectoryName(browserPath)!);
                    File.WriteAllBytes(browserPath, publicBytes);
                }
            }
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"definition-{(check ? "current" : "exported")}:family={DefinitionBuilder.FamilyId}:revision={revision}"
                + $":digest={CanonicalJson.Digest(canonical)}:fields={definition["fields"]!.AsArray().Count}:artifacts=5"));
            return 0;
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or JsonException
            or ArgumentException or InvalidOperationException or FormatException or OverflowException
            or ConfigurationPortabilityContractException or OperatorIdentityManifestException)
        {
            Console.Error.WriteLine(error is InvalidDataException
                ? error.Message : "definition-export-rejected:" + error.GetType().Name);
            return 1;
        }
    }

    private static void CheckRevision(string existing, long revision, byte[] proposed)
    {
        byte[] bytes = File.ReadAllBytes(existing);
        using JsonDocument previous = JsonDocument.Parse(bytes);
        long previousRevision = previous.RootElement.GetProperty("revision").GetInt64();
        if (revision < previousRevision || revision == previousRevision && !bytes.AsSpan().SequenceEqual(proposed))
            throw new InvalidDataException("definition-revision-conflict");
    }

    private static byte[] ReadBounded(string path)
    {
        using FileStream stream = File.OpenRead(path);
        if (stream.Length > ConfigurationPortabilityContentLimits.MaximumArtifactUtf8Bytes)
            throw new InvalidDataException("source-limit");
        byte[] bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static void WriteNew(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using var stream = new FileStream(path, options);
        stream.Write(bytes);
    }

    private static string Root()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src", "Event.Wire.Contracts", "Event.Wire.Contracts.csproj")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("event-root-unavailable");
    }
}
