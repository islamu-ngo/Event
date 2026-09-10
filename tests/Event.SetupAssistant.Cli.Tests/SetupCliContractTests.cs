using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace ISLAMU.SetupAssistant.Cli.Tests;

public sealed class SetupCliContractTests
{
    private static readonly string[] InvocationPropertyNames = ["Arguments", "Mode", "Io", "Environment"];

    [Test]
    public async Task CheckedMachineSchemaIsCanonicalClosedBoundedAndFutureGeneratorOwned()
    {
        byte[] bytes = await File.ReadAllBytesAsync(RepositoryPath("schemas", "event-setup-command-v1.schema.json"));
        JsonObject schema = SetupCliMachineContractVerifier.ParseSchema(bytes);
        IReadOnlyList<string> errors = SetupCliMachineContractVerifier.InspectSchema(schema);

        await Assert.That(errors).IsEmpty();
        await Assert.That(schema["$defs"]?["invocation"]?["properties"]?["commandFamily"]?["enum"]!.AsArray().Select(item => item!.GetValue<string>()))
            .IsEquivalentTo(SetupCliContractSpecification.Operations.Keys);
        await Assert.That(schema["properties"]?["exitCode"]?["enum"]!.AsArray().Select(item => item!.GetValue<int>()))
            .IsEquivalentTo(SetupCliContractSpecification.ExitCodes.Values);
        await Assert.That(schema["_metadata"]?["commandOptions"]!.AsArray().Select(item => item!.GetValue<string>()))
            .IsEquivalentTo(["--help", "--machine", "--text", "--dry-run", "--input", "--baseline", "--output", "--key", "--topology", "--capability", "--provider"]);
    }

    [Test]
    public async Task MachineFixtureIsExactlyOneCanonicalValueSafeObject()
    {
        byte[] fixture = SetupCliMachineContractVerifier.GoodFixture();

        await Assert.That(SetupCliMachineContractVerifier.Validate(fixture)).IsEmpty();
        await Assert.That(fixture[0]).IsNotEqualTo((byte)0xEF);
        await Assert.That(fixture[^1]).IsEqualTo((byte)'\n');
        await Assert.That(fixture.Contains((byte)'\r')).IsFalse();
    }

    [Test]
    public async Task MachineVerifierRejectsFramingControlsAndOpenValueBearingObjects()
    {
        byte[] good = SetupCliMachineContractVerifier.GoodFixture();
        var vectors = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["second-object"] = [.. good.AsSpan(0, good.Length - 1), .. "{}\n"u8],
            ["bom"] = [0xEF, 0xBB, 0xBF, .. good],
            ["ansi"] = [0x1B, .. good],
            ["localized-message"] = SetupCliMachineContractVerifier.Mutate(root => root["message"] = "localized"),
            ["diagnostic-value"] = SetupCliMachineContractVerifier.Mutate(root => root["diagnostics"] = new JsonArray(new JsonObject
            {
                ["code"] = "invalid-artifact",
                ["path"] = "$.input",
                ["severity"] = "error",
                ["value"] = "present"
            })),
            ["artifact-body"] = SetupCliMachineContractVerifier.Mutate(root => root["artifacts"]![0]!["body"] = "present"),
            ["artifact-bytes"] = SetupCliMachineContractVerifier.Mutate(root => root["artifacts"]![0]!["bytes"] = 1)
        };

        foreach ((string name, byte[] fixture) in vectors)
        {
            await Assert.That(SetupCliMachineContractVerifier.Validate(fixture).Count)
                .IsGreaterThan(0).Because(name);
        }
    }

    [Test]
    public async Task MachineVerifierRejectsDigestExitOrderingReadinessAndBoundsBreakers()
    {
        var vectors = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["digest"] = SetupCliMachineContractVerifier.Mutate(root => root["artifacts"]![0]!["digest"] = "ABC"),
            ["status"] = SetupCliMachineContractVerifier.Mutate(root => root["status"] = "unknown"),
            ["exit-mismatch"] = SetupCliMachineContractVerifier.Mutate(root => root["exitCode"] = 4),
            ["unsorted-coverage"] = SetupCliMachineContractVerifier.Mutate(root => root["coverage"]!["coveredKeys"] = new JsonArray("z", "a")),
            ["duplicate-coverage"] = SetupCliMachineContractVerifier.Mutate(root => root["coverage"]!["coveredKeys"] = new JsonArray("a", "a")),
            ["readiness"] = SetupCliMachineContractVerifier.Mutate(root => root["readiness"] = new JsonObject
            {
                ["state"] = "ready",
                ["missingKeys"] = new JsonArray("missing"),
                ["blockedKeys"] = new JsonArray()
            }),
            ["oversized"] = SetupCliMachineContractVerifier.Mutate(root => root["diagnostics"] = new JsonArray(new JsonObject
            {
                ["code"] = new string('a', 97),
                ["path"] = "$.input",
                ["severity"] = "error"
            }))
        };

        foreach ((string name, byte[] fixture) in vectors)
        {
            await Assert.That(SetupCliMachineContractVerifier.Validate(fixture).Count)
                .IsGreaterThan(0).Because(name);
        }
    }

    [Test]
    public async Task ExitCategoriesAreClosedUniqueAndWithinOperatingSystemRange()
    {
        int[] codes = SetupCliContractSpecification.ExitCodes.Values.ToArray();

        await Assert.That(codes.Distinct().Count()).IsEqualTo(codes.Length);
        await Assert.That(codes.All(code => code is >= 0 and <= 255)).IsTrue();
        await Assert.That(SetupCliContractSpecification.ExitCodes["success"]).IsEqualTo(0);
        await Assert.That(SetupCliContractSpecification.ExitCodes["usage"]).IsEqualTo(64);
        await Assert.That(SetupCliContractSpecification.ExitCodes.Keys)
            .IsEquivalentTo(["success", "validation", "incomplete", "blocked", "usage", "data", "io", "internal"]);
    }

    [Test]
    public async Task CommandGrammarPinsFamiliesOperationsModesHelpDryRunAndExplicitIo()
    {
        var accepted = new[]
        {
            Vector(["catalogue", "list", "--machine", "--dry-run"]),
            Vector(["catalogue", "describe", "--key", "API_HTTP_PORT", "--help", "--text", "--dry-run"]),
            Vector(["manifest", "validate", "--input", "artifact.json", "--machine"]),
            Vector(["manifest", "diff", "--baseline", "before.json", "--input", "artifact.json", "--machine"]),
            Vector(["tenant-package", "export", "--input", "artifact.json", "--output", "result.json", "--dry-run"]),
            Vector(["env", "render", "--output", "-", "--text", "--dry-run"]),
            Vector(["legal", "preview", "--input", "-", "--text"], "artifact"),
            Vector(["doctor", "--machine"])
        };
        foreach (CliVector vector in accepted)
        {
            await Assert.That(SetupCliContractSpecification.Validate(vector)).IsEmpty();
        }

        var rejected = new[]
        {
            Vector(["catalog", "list"]),
            Vector(["manifest", "show"]),
            Vector(["manifest", "validate", "tail"]),
            Vector(["manifest", "validate", "--unknown"]),
            Vector(["manifest", "validate", "--machine", "--text"]),
            Vector(["catalogue", "list", "--input", "artifact.json"]),
            Vector(["catalogue", "list", "--machine"]),
            Vector(["catalogue", "show", "--dry-run"]),
            Vector(["manifest", "diff", "--input", "artifact.json", "--machine"]),
            Vector(["manifest", "validate", "--baseline", "before.json", "--input", "artifact.json"]),
            Vector(["manifest", "validate", "--output", "artifact.json"]),
            Vector(["manifest", "validate", "--dry-run"]),
            Vector(["env", "render", "--machine", "--output", "-"])
        };
        foreach (CliVector vector in rejected)
        {
            await Assert.That(SetupCliContractSpecification.Validate(vector).Count).IsGreaterThan(0);
        }
    }

    [Test]
    public async Task ProcessSurfacesRejectSecretBearingNamesCapturedInputAndUnsafeOutputWithoutEcho()
    {
        var rejected = new[]
        {
            Vector(["manifest", "validate", "--password", "present"]),
            Vector(["manifest", "validate", "--input", "credential.txt"]),
            Vector(["doctor", "argument-tail"]),
            Vector(["doctor"], "password="),
            Vector(["manifest", "validate"], "artifact"),
            Vector(["manifest", "validate", "--input", "-"], "token="),
            Vector(["doctor"], environmentNames: ["SERVICE_TOKEN"]),
            Vector(["manifest", "validate", "--machine"], environmentNames: ["DATABASE_CONNECTION_STRING"])
        };
        foreach (CliVector vector in rejected)
        {
            await Assert.That(SetupCliContractSpecification.Validate(vector).Count).IsGreaterThan(0);
        }

        string[] unsafeProjections = ["--password tail", "password=", "SERVICE_TOKEN", "person@example.invalid", "https://example.invalid", "Server=machine", "\u001b[31m", "\u001b]0;terminal-title\u0007"];
        foreach (string projection in unsafeProjections)
        {
            await Assert.That(SetupCliContractSpecification.ProjectionIsSafe(projection)).IsFalse();
        }
        await Assert.That(SetupCliContractSpecification.ProjectionIsSafe("validation-error $.input\n")).IsTrue();
    }

    [Test]
    public async Task CliAssemblyIsExecutableAndPackageFreeWithOnlyCoreProjectDependency()
    {
        System.Reflection.Assembly assembly = System.Reflection.Assembly.Load("Event.SetupAssistant.Cli");
        XDocument project = XDocument.Load(RepositoryPath("src", "Event.SetupAssistant.Cli", "Event.SetupAssistant.Cli.csproj"));
        string[] packageReferences = project.Descendants("PackageReference").Select(element => (string?)element.Attribute("Include") ?? string.Empty).ToArray();
        string[] projectReferences = project.Descendants("ProjectReference")
            .Select(element => ((string?)element.Attribute("Include") ?? string.Empty).Replace('\\', '/'))
            .Select(include => Path.GetFileNameWithoutExtension(include))
            .ToArray();
        string[] forbiddenReferences = assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => name.StartsWith("Microsoft.Extensions.DependencyInjection", StringComparison.Ordinal) ||
                           name.StartsWith("System.CommandLine", StringComparison.Ordinal) ||
                           name.Contains("Provider", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        await Assert.That(assembly.EntryPoint).IsNotNull();
        await Assert.That(project.Root?.Element("PropertyGroup")?.Element("OutputType")?.Value).IsEqualTo("Exe");
        await Assert.That(packageReferences).IsEmpty();
        await Assert.That(projectReferences).IsEquivalentTo(["Event.Setup.Core"]);
        await Assert.That(forbiddenReferences).IsEmpty();
    }

    [Test]
    public async Task FinalExecutableCommandOwnersArePresentWithExplicitImmutableBoundaries()
    {
        System.Reflection.Assembly assembly = System.Reflection.Assembly.Load("Event.SetupAssistant.Cli");
        string prefix = "ISLAMU.Event.SetupAssistant.Cli.";
        string[] owners =
        [
            "SetupCliApplication", "SetupCliInvocation", "SetupCliIo", "ISetupCliInput", "ISetupCliWriter",
            "SetupCliEnvironmentPresence", "SetupCliExitCode",
            "SetupCliMachineEnvelope", "SetupCliJsonContext", "SetupCliCommandSchemaMetadata", "SetupCliExecutableMarker"
        ];
        var missing = owners.Where(name => assembly.GetType(prefix + name, throwOnError: false) is null).ToList();
        Type? application = assembly.GetType(prefix + "SetupCliApplication", throwOnError: false);
        Type? invocation = assembly.GetType(prefix + "SetupCliInvocation", throwOnError: false);
        Type? io = assembly.GetType(prefix + "SetupCliIo", throwOnError: false);
        Type? environment = assembly.GetType(prefix + "SetupCliEnvironmentPresence", throwOnError: false);
        Type? exitCode = assembly.GetType(prefix + "SetupCliExitCode", throwOnError: false);
        Type? context = assembly.GetType(prefix + "SetupCliJsonContext", throwOnError: false);
        if (application is not null && !application.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Any(method => method.Name == "Run" && method.GetParameters() is [{ ParameterType: var parameter }] && parameter == invocation))
        {
            missing.Add("SetupCliApplication.Run(SetupCliInvocation)");
        }
        if (context is not null && !typeof(JsonSerializerContext).IsAssignableFrom(context))
        {
            missing.Add("SetupCliJsonContext:JsonSerializerContext");
        }
        if (invocation is not null)
        {
            string[] properties = invocation.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(property => property.Name).ToArray();
            foreach (string property in InvocationPropertyNames.Except(properties, StringComparer.Ordinal))
            {
                missing.Add("SetupCliInvocation." + property);
            }
            if (invocation.GetProperties().Any(property => property.SetMethod is { } setter &&
                    !setter.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(System.Runtime.CompilerServices.IsExternalInit))))
            {
                missing.Add("SetupCliInvocation:mutable");
            }
            Type? argumentsType = invocation.GetProperty("Arguments")?.PropertyType;
            if (argumentsType is not null && !typeof(IReadOnlyList<string>).IsAssignableFrom(argumentsType))
            {
                missing.Add("SetupCliInvocation.Arguments:IReadOnlyList<string>");
            }
        }
        RequireProperties(io, ["Input", "Output", "Error", "MaximumCharacters", "MaximumBytes"], missing);
        RequireProperties(environment, ["Names"], missing);
        if (exitCode is not null)
        {
            if (!exitCode.IsEnum)
            {
                missing.Add("SetupCliExitCode:enum");
            }
            else
            {
                var actualExits = Enum.GetNames(exitCode).Zip(Enum.GetValues(exitCode).Cast<object>().Select(Convert.ToInt32));
                var expectedExits = new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["Success"] = 0,
                    ["Validation"] = 2,
                    ["Incomplete"] = 3,
                    ["Blocked"] = 4,
                    ["Usage"] = 64,
                    ["Data"] = 65,
                    ["Internal"] = 70,
                    ["Io"] = 74
                };
                if (!actualExits.OrderBy(pair => pair.First, StringComparer.Ordinal)
                        .SequenceEqual(expectedExits.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                            .Select(pair => (pair.Key, pair.Value))))
                {
                    missing.Add("SetupCliExitCode:closed-values");
                }
            }
        }

        await Assert.That(missing).IsEmpty().Because("SA-420 must replace the exit-64 stub with the final explicit-I/O command owners and marker");
    }

    private static void RequireProperties(Type? owner, IReadOnlyCollection<string> expected, List<string> missing)
    {
        if (owner is null)
        {
            return;
        }
        string[] actual = owner.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(property => property.Name).ToArray();
        foreach (string property in expected.Except(actual, StringComparer.Ordinal))
        {
            missing.Add(owner.Name + "." + property);
        }
    }

    private static CliVector Vector(
        IReadOnlyList<string> arguments,
        string capturedInput = "",
        IReadOnlyCollection<string>? environmentNames = null) =>
        new(arguments, capturedInput, environmentNames ?? Array.Empty<string>());

    private static string RepositoryPath(params string[] parts)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Explore.slnx")))
        {
            directory = directory.Parent;
        }
        if (directory is null)
        {
            throw new DirectoryNotFoundException("repository-root-not-found");
        }
        return Path.Combine([directory.FullName, .. parts]);
    }
}
