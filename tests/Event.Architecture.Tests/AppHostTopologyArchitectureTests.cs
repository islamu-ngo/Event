using System.Xml.Linq;

namespace Event.Architecture.Tests;

public sealed class AppHostTopologyArchitectureTests
{
    private static readonly string RepoRoot = ContextSystemHelpers.RepoRoot;
    private static readonly string AppHostPath = Path.Combine(RepoRoot, "src", "Explore.AppHost", "AppHost.cs");
    private static readonly string AppHostProjectPath = Path.Combine(RepoRoot, "src", "Explore.AppHost", "Explore.AppHost.csproj");
    private static readonly string StandaloneProjectPath = Path.Combine(RepoRoot, "src", "Event.Standalone", "Event.Standalone.csproj");
    private static readonly string[] RequiredSolutionProjects =
    [
        "src/Explore.API/Explore.API.csproj",
        "src/Explore.Blazor/Explore.Blazor.csproj",
        "src/Explore.AppHost/Explore.AppHost.csproj",
        "src/Event.Standalone/Event.Standalone.csproj"
    ];

    [Test]
    public async Task AppHost_MustParseHostingTopologyWithSplitDefaultAndCaseInsensitiveAllowedValues()
    {
        var appHost = await File.ReadAllTextAsync(AppHostPath);

        await Assert.That(appHost).Contains("var hostingTopology = ParseHostingTopology(builder.Configuration[\"Hosting:Topology\"])");
        await Assert.That(appHost).Contains("static HostingTopology ParseHostingTopology(string? rawValue)");
        await Assert.That(appHost).Contains("string.IsNullOrWhiteSpace(rawValue)");
        await Assert.That(appHost).Contains("return HostingTopology.Split;");
        await Assert.That(appHost).Contains("string.Equals(rawValue.Trim(), nameof(HostingTopology.Split), StringComparison.OrdinalIgnoreCase)");
        await Assert.That(appHost).Contains("string.Equals(rawValue.Trim(), nameof(HostingTopology.Standalone), StringComparison.OrdinalIgnoreCase)");
        await Assert.That(appHost).Contains("Hosting:Topology must be Split or Standalone.");
        await Assert.That(CountMatches(appHost, "nameof(HostingTopology.Split)")).IsGreaterThan(0);
        await Assert.That(CountMatches(appHost, "nameof(HostingTopology.Standalone)")).IsGreaterThan(0);
    }

    [Test]
    public async Task AppHost_MustFailFastForUnknownHostingTopologyValue()
    {
        var appHost = await File.ReadAllTextAsync(AppHostPath);
        var parseFunction = ExtractFunctionBody(appHost, "static HostingTopology ParseHostingTopology(string? rawValue)");

        await Assert.That(appHost).Contains("static HostingTopology ParseHostingTopology(string? rawValue)");
        await Assert.That(parseFunction).Contains("if (string.IsNullOrWhiteSpace(rawValue))");
        await Assert.That(parseFunction).Contains("if (string.Equals(rawValue.Trim(), nameof(HostingTopology.Split), StringComparison.OrdinalIgnoreCase))");
        await Assert.That(parseFunction).Contains("if (string.Equals(rawValue.Trim(), nameof(HostingTopology.Standalone), StringComparison.OrdinalIgnoreCase))");
        await Assert.That(parseFunction).Contains("throw new InvalidOperationException(");
        await Assert.That(parseFunction).Contains("Hosting:Topology must be Split or Standalone.");
        await Assert.That(CountMatches(parseFunction, "nameof(HostingTopology.")).IsEqualTo(2);
    }

    [Test]
    public async Task AppHost_MustRunExactlyOneWebTopology_AndTopologyBranchesMustBeMutuallyExclusive()
    {
        var appHost = await File.ReadAllTextAsync(AppHostPath);

        await Assert.That(appHost).Contains("if (hostingTopology == HostingTopology.Split)");

        var splitIndex = appHost.IndexOf("if (hostingTopology == HostingTopology.Split)", StringComparison.Ordinal);
        await Assert.That(splitIndex).IsGreaterThanOrEqualTo(0);
        var firstElseIndex = FindTopLevelElseIndexForSplitBranch(appHost, splitIndex);
        await Assert.That(firstElseIndex).IsGreaterThan(splitIndex);

        var splitApiIndex = appHost.IndexOf("var exploreAPI = WithProfileSecretMode(", splitIndex, StringComparison.Ordinal);
        var splitBlazorIndex = appHost.IndexOf("var exploreBlazor = WithProfileSecretMode(", splitIndex, StringComparison.Ordinal);
        var standaloneIndex = appHost.IndexOf("var eventStandalone = WithProfileSecretMode(", firstElseIndex, StringComparison.Ordinal);
        const string splitApiRegistration = "builder.AddProject<Projects.Explore_API>(";
        const string splitBlazorRegistration = "builder.AddProject<Projects.Explore_Blazor>(";
        const string standaloneRegistration = "builder.AddProject<Projects.Event_Standalone>(";

        await Assert.That(splitApiIndex).IsGreaterThan(splitIndex);
        await Assert.That(splitBlazorIndex).IsGreaterThan(splitIndex);
        await Assert.That(splitApiIndex).IsLessThan(firstElseIndex);
        await Assert.That(splitBlazorIndex).IsLessThan(firstElseIndex);
        await Assert.That(standaloneIndex).IsGreaterThan(firstElseIndex);

        var splitBranch = appHost[splitIndex..firstElseIndex];
        var standaloneBranch = appHost[firstElseIndex..];

        await Assert.That(splitBranch).Contains("var exploreAPI = WithProfileSecretMode(");
        await Assert.That(splitBranch).Contains("var exploreBlazor = WithProfileSecretMode(");
        await Assert.That(splitBranch).DoesNotContain("var eventStandalone = WithProfileSecretMode(");
        await Assert.That(splitBranch).Contains(splitApiRegistration);
        await Assert.That(splitBranch).Contains(splitBlazorRegistration);
        await Assert.That(splitBranch).DoesNotContain(standaloneRegistration);

        await Assert.That(standaloneBranch).Contains("var eventStandalone = WithProfileSecretMode(");
        await Assert.That(standaloneBranch).DoesNotContain("var exploreAPI = WithProfileSecretMode(");
        await Assert.That(standaloneBranch).DoesNotContain("var exploreBlazor = WithProfileSecretMode(");
        await Assert.That(standaloneBranch).Contains(standaloneRegistration);
        await Assert.That(standaloneBranch).DoesNotContain(splitApiRegistration);
        await Assert.That(standaloneBranch).DoesNotContain(splitBlazorRegistration);

        var splitApiCount = CountMatches(appHost, "var exploreAPI = WithProfileSecretMode(");
        var splitBlazorCount = CountMatches(appHost, "var exploreBlazor = WithProfileSecretMode(");
        var standaloneCount = CountMatches(appHost, "var eventStandalone = WithProfileSecretMode(");

        await Assert.That(splitApiCount).IsEqualTo(1);
        await Assert.That(splitBlazorCount).IsEqualTo(1);
        await Assert.That(standaloneCount).IsEqualTo(1);
        await Assert.That(CountMatches(appHost, splitApiRegistration)).IsEqualTo(1);
        await Assert.That(CountMatches(appHost, splitBlazorRegistration)).IsEqualTo(1);
        await Assert.That(CountMatches(appHost, standaloneRegistration)).IsEqualTo(1);
    }

    [Test]
    public async Task StandaloneWebResource_MustForwardEachRequiredSplitWebInputExactlyOnce()
    {
        var appHost = await File.ReadAllTextAsync(AppHostPath);
        var splitIndex = appHost.IndexOf("if (hostingTopology == HostingTopology.Split)", StringComparison.Ordinal);
        var standaloneIndex = FindTopLevelElseIndexForSplitBranch(appHost, splitIndex);
        var standaloneBranch = appHost[standaloneIndex..appHost.IndexOf("await builder.Build().RunAsync();", standaloneIndex, StringComparison.Ordinal)];
        var requiredEnvironmentKeys = new[]
        {
            "HttpsRedirection__Enabled",
            "CONTROL_PLANE_PUBLIC_ORIGIN",
            "Cerbos__PolicyPackagePath",
            "Storage__Local__RootPath",
            "Storage__Local__CreateRootIfMissing",
            "StorageReconciliation__Enabled",
            "StorageReconciliation__DryRun",
            "PrivacyErasure__Authority__Topology",
            "Bff__AdminHosts__0"
        };

        foreach (var key in requiredEnvironmentKeys)
        {
            await Assert.That(CountMatches(standaloneBranch, $".WithEnvironment(\"{key}\"")).IsEqualTo(1);
        }

        await Assert.That(CountMatches(standaloneBranch, ".WithReference(migrations)")).IsEqualTo(1);
        await Assert.That(CountMatches(standaloneBranch, ".WaitForCompletion(migrations)")).IsEqualTo(1);
        await Assert.That(CountMatches(standaloneBranch, "ConfigureLocalKeycloakCallbacks(")).IsEqualTo(1);
        await Assert.That(CountMatches(standaloneBranch, "ConfigureLocalPlatformApi(")).IsEqualTo(1);
        await Assert.That(CountMatches(standaloneBranch, "ConfigureLocalPlatformBlazor(")).IsEqualTo(1);
    }

    [Test]
    public async Task EventStandalone_CompositionRoot_MustNotReferenceLowerLayersOrExpandBlazorClientDependencies()
    {
        var projectXml = await File.ReadAllTextAsync(StandaloneProjectPath);

        var standaloneReferences = XDocument.Load(StandaloneProjectPath)
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
            .ToArray();

        await Assert.That(standaloneReferences).IsEquivalentTo([
            "..\\Explore.API\\Explore.API.csproj",
            "..\\Explore.Blazor\\Explore.Blazor.csproj"
        ]);
        await Assert.That(projectXml).Contains("<ProjectReference Include=\"..\\Explore.API\\Explore.API.csproj\" />");
        await Assert.That(projectXml).Contains("<ProjectReference Include=\"..\\Explore.Blazor\\Explore.Blazor.csproj\" />");

        var blazorClientProject = await File.ReadAllTextAsync(Path.Combine(RepoRoot, "src", "Explore.Blazor.Client", "Explore.Blazor.Client.csproj"));
        var blazorProject = await File.ReadAllTextAsync(Path.Combine(RepoRoot, "src", "Explore.Blazor", "Explore.Blazor.csproj"));
        var standaloneCsProj = await File.ReadAllTextAsync(StandaloneProjectPath);

        await Assert.That(blazorClientProject).DoesNotContain("Event.Standalone.csproj");
        await Assert.That(blazorProject).DoesNotContain("Event.Standalone.csproj");
        await Assert.That(standaloneCsProj).Contains("ProjectReference Include=\"..\\Explore.API\\Explore.API.csproj\"");
        await Assert.That(standaloneCsProj).Contains("ProjectReference Include=\"..\\Explore.Blazor\\Explore.Blazor.csproj\"");

        var appHostReferences = XDocument.Load(AppHostProjectPath)
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
            .ToArray();

        await Assert.That(appHostReferences).Contains("..\\Event.Standalone\\Event.Standalone.csproj");
        await Assert.That(appHostReferences).Contains("..\\Explore.API\\Explore.API.csproj");
        await Assert.That(appHostReferences).Contains("..\\Explore.Blazor\\Explore.Blazor.csproj");
    }

    [Test]
    public async Task AppHost_ParseFunctionMustRejectMalformedTopologyAndWarnAboutAllowedModes()
    {
        var appHost = await File.ReadAllTextAsync(AppHostPath);
        var parseFunction = ExtractFunctionBody(appHost, "static HostingTopology ParseHostingTopology(string? rawValue)");

        await Assert.That(appHost).Contains("static HostingTopology ParseHostingTopology(string? rawValue)");
        await Assert.That(parseFunction).Contains("rawValue.Trim()");
        await Assert.That(parseFunction).Contains("StringComparison.OrdinalIgnoreCase)");
        await Assert.That(parseFunction).Contains("Hosting:Topology must be Split or Standalone.");
        await Assert.That(parseFunction).Contains("throw new InvalidOperationException");
        await Assert.That(CountMatches(parseFunction, "if (string.Equals(rawValue.Trim(), nameof(HostingTopology.")).IsEqualTo(2);
        await Assert.That(CountMatches(parseFunction, "throw new InvalidOperationException(")).IsEqualTo(1);
    }

    [Test]
    public async Task CompositionRoots_MustBeInSolutionManifest()
    {
        var solutionManifest = await File.ReadAllTextAsync(Path.Combine(RepoRoot, "Explore.slnx"));

        foreach (var requiredProject in RequiredSolutionProjects)
        {
            await Assert.That(solutionManifest).Contains(requiredProject);
            await Assert.That(CountMatches(solutionManifest, requiredProject)).IsEqualTo(1);
        }
    }

    private static int CountMatches(string input, string value) =>
        CountMatches(input, value, 0, input.Length, StringComparison.Ordinal);

    private static int CountMatches(string input, string value, int startIndex, int count, StringComparison comparison)
    {
        var matched = 0;
        var remainingText = input.AsSpan(startIndex, count);
        var search = value.AsSpan();
        var cursor = 0;

        while (cursor < remainingText.Length)
        {
            var found = remainingText.Slice(cursor).IndexOf(search, comparison);
            if (found < 0)
            {
                break;
            }

            matched++;
            cursor += found + search.Length;
        }

        return matched;
    }

    private static string ExtractFunctionBody(string source, string functionSignature)
    {
        var startOfFunction = source.IndexOf(functionSignature, StringComparison.Ordinal);
        if (startOfFunction < 0)
        {
            throw new InvalidOperationException($"Could not locate function signature: {functionSignature}");
        }

        var signatureEnd = source.IndexOf(')', startOfFunction);
        if (signatureEnd <= startOfFunction)
        {
            throw new InvalidOperationException("Could not locate function signature close paren.");
        }

        var bodyOpen = source.IndexOf('{', signatureEnd);
        if (bodyOpen <= signatureEnd)
        {
            throw new InvalidOperationException("Could not locate function body.");
        }

        var depth = 0;
        for (var i = bodyOpen; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source[bodyOpen..(i + 1)];
                }
            }
        }

        return string.Empty;
    }

    private static int FindTopLevelElseIndexForSplitBranch(string appHost, int ifIndex)
    {
        var braceOpenIndex = appHost.IndexOf('{', ifIndex);
        if (braceOpenIndex < 0)
        {
            return -1;
        }

        var depth = 0;
        var inString = false;
        var inChar = false;
        var inLineComment = false;
        var inBlockComment = false;

        for (var i = braceOpenIndex; i < appHost.Length; i++)
        {
            if (inLineComment)
            {
                if (appHost[i] == '\n')
                {
                    inLineComment = false;
                }

                continue;
            }

            if (inBlockComment)
            {
                if (appHost[i] == '*' && i + 1 < appHost.Length && appHost[i + 1] == '/')
                {
                    inBlockComment = false;
                    i++;
                }

                continue;
            }

            if (inString)
            {
                if (appHost[i] == '\\')
                {
                    i++;
                    continue;
                }

                if (appHost[i] == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (inChar)
            {
                if (appHost[i] == '\\')
                {
                    i++;
                    continue;
                }

                if (appHost[i] == '\'')
                {
                    inChar = false;
                }

                continue;
            }

            if (appHost[i] == '/' && i + 1 < appHost.Length)
            {
                if (appHost[i + 1] == '/')
                {
                    inLineComment = true;
                    i++;
                    continue;
                }

                if (appHost[i + 1] == '*')
                {
                    inBlockComment = true;
                    i++;
                    continue;
                }
            }

            if (appHost[i] == '"')
            {
                inString = true;
                continue;
            }

            if (appHost[i] == '\'')
            {
                inChar = true;
                continue;
            }

            if (appHost[i] == '{')
            {
                depth++;
                continue;
            }

            if (appHost[i] != '}')
            {
                continue;
            }

            depth--;
            if (depth != 0)
            {
                continue;
            }

            var cursor = i + 1;
            while (cursor < appHost.Length && char.IsWhiteSpace(appHost[cursor]))
            {
                cursor++;
            }

            if (cursor + 3 < appHost.Length &&
                appHost[cursor] == 'e' &&
                appHost[cursor + 1] == 'l' &&
                appHost[cursor + 2] == 's' &&
                appHost[cursor + 3] == 'e')
            {
                return cursor;
            }
        }

        return -1;
    }
}
