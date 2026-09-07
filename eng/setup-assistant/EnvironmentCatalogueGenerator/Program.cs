// ABOUTME: Generates and checks value-safe environment catalogue, template, Compose parity, and docs anchors.
// ABOUTME: Consumes compiled Core and Domain authorities and never reads local dotenv or C# source text.

using System.Text;
using System.Text.Json;
using Explore.Domain.Secrets;
using ISLAMU.Event.Setup.Core.Environment;

internal static class Program
{
    private const int UsageError = 64;
    private const string DocumentationBegin = "<!-- BEGIN GENERATED ENVIRONMENT CATALOGUE -->";
    private const string DocumentationEnd = "<!-- END GENERATED ENVIRONMENT CATALOGUE -->";

    public static int Main(string[] args)
    {
        if (args.Length != 1 || args[0] is not ("--write" or "--check"))
        {
            Console.Error.WriteLine("Usage: environment-catalogue-generator (--write|--check)");
            return UsageError;
        }

        try
        {
            return Execute(args[0] == "--write");
        }
        catch (InvalidDataException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
        catch (IOException)
        {
            Console.Error.WriteLine("environment-catalogue-io-failed");
            return 1;
        }
        catch (UnauthorizedAccessException)
        {
            Console.Error.WriteLine("environment-catalogue-access-denied");
            return 1;
        }
    }

    private static int Execute(bool write)
    {
        string repositoryRoot = FindRepositoryRoot();
        EnvironmentCatalogue catalogue = CanonicalEnvironmentCatalogue.Catalogue;
        SecretDefinition[] registry = SecretDefinitionRegistry.All.Values
            .OrderBy(item => item.DefaultEnvironmentVariableName, StringComparer.Ordinal).ToArray();
        ValidateRegistry(catalogue, registry);
        string composePath = Path.Combine(repositoryRoot, "docker-compose.yml");
        ComposeProjection compose = ParseCompose(ReadStrictUtf8(composePath));
        ValidateCompose(compose);
        ValidateEnvironmentTemplate(ParseEnvironmentKeys(
            File.ReadAllBytes(Path.Combine(repositoryRoot, ".env.example"))));

        byte[] machineBytes = GenerateMachineCatalogue(catalogue, registry);
        string documentationPath = File.Exists(Path.Combine(repositoryRoot, "docs", "internal", "CONFIGURATION.md"))
            ? Path.Combine(repositoryRoot, "docs", "internal", "CONFIGURATION.md")
            : Path.Combine(repositoryRoot, "docs", "CONFIGURATION.md");
        byte[] documentationBytes = GenerateDocumentation(
            ReadStrictUtf8(documentationPath), catalogue);
        var outputs = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [Path.Combine(repositoryRoot, "eng", "setup-assistant", "generated", "environment-catalogue.json")] = machineBytes,
            [documentationPath] = documentationBytes,
        };

        if (!write)
        {
            string[] stale = outputs.Where(item => !File.Exists(item.Key)
                    || !File.ReadAllBytes(item.Key).AsSpan().SequenceEqual(item.Value))
                .Select(item => Path.GetRelativePath(repositoryRoot, item.Key).Replace('\\', '/'))
                .Order(StringComparer.Ordinal).ToArray();
            if (stale.Length != 0)
            {
                Console.Error.WriteLine("environment-catalogue-stale:" + string.Join(',', stale));
                return 1;
            }

            Console.WriteLine($"Environment catalogue is current ({catalogue.Definitions.Count} definitions).");
            return 0;
        }

        foreach ((string path, byte[] bytes) in outputs)
            AtomicWrite(path, bytes);
        Console.WriteLine($"Generated environment catalogue ({catalogue.Definitions.Count} definitions).");
        return 0;
    }

    private static void ValidateRegistry(
        EnvironmentCatalogue catalogue,
        IReadOnlyList<SecretDefinition> registry)
    {
        string[] names = registry.Select(item => item.DefaultEnvironmentVariableName).ToArray();
        if (names.Length == 0
            || names.Distinct(StringComparer.Ordinal).Count() != names.Length
            || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length)
            throw new InvalidDataException("environment-registry-duplicate-key");
        if (names.Any(name => !IsCanonicalKey(name)))
            throw new InvalidDataException("environment-registry-noncanonical-key");

        string[] compiled = CanonicalEnvironmentCatalogue.SecretBindingEnvironmentKeys
            .Order(StringComparer.Ordinal).ToArray();
        if (!compiled.SequenceEqual(names.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidDataException("environment-registry-core-parity-drift");
        foreach (string name in names)
        {
            EnvironmentVariableDefinition? definition = catalogue.Lookup(name);
            if (definition is null || definition.Sensitivity != EnvironmentVariableSensitivity.Secret)
                throw new InvalidDataException("environment-registry-classification-drift");
        }
    }

    private static void ValidateEnvironmentTemplate(IReadOnlyList<string> actual)
    {
        if (!actual.SequenceEqual(CanonicalEnvironmentCatalogue.DotenvEnvironmentKeys, StringComparer.Ordinal))
            throw new InvalidDataException("environment-template-key-order-drift");
    }

    private static void ValidateCompose(ComposeProjection actual)
    {
        string[] expected = CanonicalEnvironmentCatalogue.ComposeEnvironmentKeys.ToArray();
        if (!actual.Keys.SequenceEqual(expected, StringComparer.Ordinal))
            throw new InvalidDataException("environment-compose-key-order-drift");
        string[] expectedRequired = CanonicalEnvironmentCatalogue.Catalogue.Definitions
            .Where(item => item.Generation.ComposeRequired)
            .OrderBy(item => item.Generation.ComposeOrder)
            .Select(item => item.Key).ToArray();
        if (!actual.RequiredKeys.Order(StringComparer.Ordinal)
            .SequenceEqual(expectedRequired.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidDataException("environment-compose-requiredness-drift");
    }

    private static byte[] GenerateMachineCatalogue(
        EnvironmentCatalogue catalogue,
        IReadOnlyList<SecretDefinition> registry)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteStartObject("_metadata");
            WriteStrings(writer, "about",
            [
                "ABOUTME: Generated value-safe environment catalogue; do not edit by hand.",
                "ABOUTME: Owned by eng/setup-assistant/EnvironmentCatalogueGenerator.",
            ]);
            writer.WriteString("generatedBy", "eng/setup-assistant/EnvironmentCatalogueGenerator");
            writer.WriteEndObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteStartArray("definitions");
            foreach (EnvironmentVariableDefinition item in catalogue.Definitions)
            {
                writer.WriteStartObject();
                writer.WriteString("key", item.Key);
                writer.WriteString("category", EnumName(item.Category));
                writer.WriteString("sensitivity", EnumName(item.Sensitivity));
                writer.WriteString("requirement", EnumName(item.Requirement));
                writer.WriteNumber("order", item.Order);
                writer.WriteBoolean("hasSafeDefault", item.SafeDefault is not null);
                writer.WriteString("validatorId", item.ValidatorId);
                writer.WriteString("restartBehavior", EnumName(item.RestartBehavior));
                writer.WriteStartObject("generation");
                writer.WriteNumber("surfaces", (int)item.Generation.Surfaces);
                if (item.Generation.DotenvOrder.HasValue)
                    writer.WriteNumber("dotenvOrder", item.Generation.DotenvOrder.Value);
                if (item.Generation.ComposeOrder.HasValue)
                    writer.WriteNumber("composeOrder", item.Generation.ComposeOrder.Value);
                writer.WriteBoolean("composeRequired", item.Generation.ComposeRequired);
                writer.WriteEndObject();
                writer.WriteStartObject("documentation");
                writer.WriteString("localizationKey", item.Documentation.LocalizationKey);
                writer.WriteString("helpKey", item.Documentation.HelpKey);
                writer.WriteString("anchor", item.Documentation.Anchor);
                writer.WriteEndObject();
                WriteActivation(writer, item.Activation);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            WriteStrings(writer, "secretBindingEnvironmentKeys", registry
                .Select(item => item.DefaultEnvironmentVariableName).Order(StringComparer.Ordinal));
            WriteStrings(writer, "dotenvEnvironmentKeys", CanonicalEnvironmentCatalogue.DotenvEnvironmentKeys);
            WriteStrings(writer, "composeEnvironmentKeys", CanonicalEnvironmentCatalogue.ComposeEnvironmentKeys);
            WriteStrings(writer, "composeRequiredEnvironmentKeys", catalogue.Definitions
                .Where(item => item.Generation.ComposeRequired)
                .OrderBy(item => item.Generation.ComposeOrder).Select(item => item.Key));
            WriteStrings(writer, "startupEnvironmentKeys", catalogue.Definitions
                .Where(item => item.Generation.Surfaces.HasFlag(EnvironmentGenerationSurface.Startup))
                .OrderBy(item => item.Order).Select(item => item.Key));
            writer.WriteEndObject();
        }
        return FinalNewline(stream.ToArray());
    }

    private static byte[] GenerateDocumentation(string current, EnvironmentCatalogue catalogue)
    {
        var generated = new StringBuilder();
        generated.AppendLine(DocumentationBegin);
        generated.AppendLine("## Generated Environment Catalogue");
        generated.AppendLine();
        generated.AppendLine("This bounded section is generated from the package-free Core catalogue. Runtime secret binding remains authoritative in `SecretDefinitionRegistry`; Compose topology remains owned by `docker-compose.yml`.");
        generated.AppendLine();
        generated.AppendLine("Source anchors: `src/Event.Setup.Core/Environment/`, `src/Explore.Domain/Secrets/SecretDefinitionRegistry.cs`, `.env.example`, and `docker-compose.yml`.");
        generated.AppendLine();
        generated.AppendLine("```bash");
        generated.AppendLine("dotnet run --project eng/setup-assistant/EnvironmentCatalogueGenerator/EnvironmentCatalogueGenerator.csproj --configuration Release -- --write");
        generated.AppendLine("dotnet run --project eng/setup-assistant/EnvironmentCatalogueGenerator/EnvironmentCatalogueGenerator.csproj --configuration Release -- --check");
        generated.AppendLine("```");
        generated.AppendLine();
        generated.AppendLine("| Key | Category | Sensitivity | Requirement | Restart | Surfaces |");
        generated.AppendLine("|---|---|---|---|---|---|");
        foreach (EnvironmentVariableDefinition item in catalogue.Definitions)
        {
            generated.Append("| `").Append(item.Key).Append("` | ")
                .Append(EnumName(item.Category)).Append(" | ")
                .Append(EnumName(item.Sensitivity)).Append(" | ")
                .Append(EnumName(item.Requirement)).Append(" | ")
                .Append(EnumName(item.RestartBehavior)).Append(" | ")
                .Append(SurfaceNames(item.Generation.Surfaces)).AppendLine(" |");
        }
        generated.AppendLine(DocumentationEnd);
        string block = generated.ToString().Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');
        int begin = current.IndexOf(DocumentationBegin, StringComparison.Ordinal);
        int end = current.IndexOf(DocumentationEnd, StringComparison.Ordinal);
        string result;
        if (begin >= 0 && end > begin)
        {
            int after = end + DocumentationEnd.Length;
            result = current[..begin] + block + current[after..];
        }
        else
        {
            const string insertion = "## Runtime Configuration Sources";
            int index = current.IndexOf(insertion, StringComparison.Ordinal);
            if (index < 0) throw new InvalidDataException("environment-doc-anchor-missing");
            result = current[..index] + block + "\n\n" + current[index..];
        }
        result = result.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n') + "\n";
        return Encoding.UTF8.GetBytes(result);
    }

    private static System.Collections.ObjectModel.ReadOnlyCollection<string> ParseEnvironmentKeys(byte[] bytes)
    {
        var keys = new List<string>();
        int cursor = 0;
        while (cursor < bytes.Length)
        {
            int lineStart = cursor;
            while (cursor < bytes.Length && bytes[cursor] != (byte)'\n') cursor++;
            int lineEnd = cursor;
            cursor++;
            if (lineEnd == lineStart || bytes[lineStart] == (byte)'#') continue;
            int equals = Array.IndexOf(bytes, (byte)'=', lineStart, lineEnd - lineStart);
            if (equals <= lineStart) throw new InvalidDataException("environment-template-shape-invalid");
            string key;
            try
            {
                key = new UTF8Encoding(false, true).GetString(bytes, lineStart, equals - lineStart);
            }
            catch (DecoderFallbackException)
            {
                throw new InvalidDataException("environment-artifact-utf8-invalid");
            }
            if (!IsCanonicalKey(key)) throw new InvalidDataException("environment-template-key-invalid");
            keys.Add(key);
        }
        return keys.AsReadOnly();
    }

    private static ComposeProjection ParseCompose(string text)
    {
        var keys = new List<string>();
        var required = new HashSet<string>(StringComparer.Ordinal);
        for (int start = 0; start < text.Length - 2; start++)
        {
            if (text[start] != '$' || text[start + 1] != '{') continue;
            int keyStart = start + 2;
            int cursor = keyStart;
            while (cursor < text.Length && (text[cursor] is >= 'A' and <= 'Z'
                or >= 'a' and <= 'z' or >= '0' and <= '9' or '_')) cursor++;
            if (cursor == keyStart) continue;
            string key = text[keyStart..cursor];
            if (!keys.Contains(key, StringComparer.Ordinal)) keys.Add(key);
            if ((cursor < text.Length && text[cursor] == '?')
                || (cursor + 1 < text.Length && text[cursor] == ':' && text[cursor + 1] == '?'))
                required.Add(key);
        }
        return new ComposeProjection(keys, required);
    }

    private static void WriteActivation(Utf8JsonWriter writer, EnvironmentActivationExpression expression)
    {
        writer.WritePropertyName("activation");
        WriteActivationNode(writer, expression);
    }

    private static void WriteActivationNode(
        Utf8JsonWriter writer,
        EnvironmentActivationExpression expression)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", EnumName(expression.Kind));
        if (expression.Identifier is not null) writer.WriteString("identifier", expression.Identifier);
        writer.WriteStartArray("operands");
        foreach (EnvironmentActivationExpression operand in expression.Operands)
            WriteActivationNode(writer, operand);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteStrings(Utf8JsonWriter writer, string name, IEnumerable<string> values)
    {
        writer.WriteStartArray(name);
        foreach (string value in values) writer.WriteStringValue(value);
        writer.WriteEndArray();
    }

    private static string SurfaceNames(EnvironmentGenerationSurface surfaces) => string.Join(", ",
        Enum.GetValues<EnvironmentGenerationSurface>()
            .Where(value => value != EnvironmentGenerationSurface.None && surfaces.HasFlag(value))
            .Select(EnumName));

    private static string EnumName<T>(T value) where T : struct, Enum =>
        value.ToString().ToLowerInvariant();

    private static string ReadStrictUtf8(string path)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(File.ReadAllBytes(path));
        }
        catch (DecoderFallbackException)
        {
            throw new InvalidDataException("environment-artifact-utf8-invalid");
        }
    }

    private static void AtomicWrite(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".sa320.tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static byte[] FinalNewline(byte[] bytes)
    {
        byte[] result = new byte[bytes.Length + 1];
        bytes.CopyTo(result, 0);
        result[^1] = (byte)'\n';
        return result;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md"))
                && File.Exists(Path.Combine(current.FullName, "docker-compose.yml"))) return current.FullName;
            current = current.Parent;
        }
        throw new InvalidDataException("environment-repository-root-not-found");
    }

    private static bool IsCanonicalKey(string key) => key.Length is > 0 and <= 128
        && key[0] is >= 'A' and <= 'Z' && key[^1] != '_'
        && !key.Contains("___", StringComparison.Ordinal)
        && key.All(character => character is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_');

    private sealed record ComposeProjection(IReadOnlyList<string> Keys, IReadOnlySet<string> RequiredKeys);
}
