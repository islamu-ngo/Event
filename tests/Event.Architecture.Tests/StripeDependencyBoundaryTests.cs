using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Event.Architecture.Tests;

public sealed class StripeDependencyBoundaryTests
{
    private const string ExpectedVersion = "52.3.0";
    private const string PackageName = "Stripe.net";

    [Test]
    public async Task StripeNet_MustBePinnedCentrallyToExpectedStableVersion()
    {
        var document = XDocument.Load(ContextSystemHelpers.RepoPath("Directory.Packages.props"));
        var version = document.Descendants()
            .Where(element => element.Name.LocalName == "PackageVersion")
            .Where(element => string.Equals(element.Attribute("Include")?.Value, PackageName, StringComparison.Ordinal))
            .Select(element => element.Attribute("Version")?.Value)
            .SingleOrDefault();

        await Assert.That(version).IsEqualTo(ExpectedVersion);
    }

    [Test]
    public async Task StripeNet_MustBeReferencedOnlyByInfrastructureProject()
    {
        var projectFiles = Directory.EnumerateFiles(Path.Combine(ContextSystemHelpers.RepoRoot, "src"), "*.csproj", SearchOption.AllDirectories);
        var owners = new List<string>();

        foreach (var projectFile in projectFiles)
        {
            var projectName = Path.GetFileNameWithoutExtension(projectFile);
            var document = XDocument.Load(projectFile);
            if (document.Descendants()
                .Where(element => element.Name.LocalName == "PackageReference")
                .Any(element => string.Equals(element.Attribute("Include")?.Value, PackageName, StringComparison.Ordinal)))
            {
                owners.Add(projectName);
            }
        }

        await Assert.That(owners).IsEquivalentTo(["Explore.Infrastructure"]);
    }

    [Test]
    public async Task StripeTypes_MustStayInsideInfrastructureSourceFiles()
    {
        var sourceFiles = Directory.EnumerateFiles(Path.Combine(ContextSystemHelpers.RepoRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Explore.Infrastructure{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.EndsWith(".g.cs", StringComparison.Ordinal))
            .ToArray();

        var violations = new List<string>();

        foreach (var file in sourceFiles)
        {
            var source = await File.ReadAllTextAsync(file);
            if (ContainsStripeReference(source))
            {
                violations.Add(Path.GetRelativePath(ContextSystemHelpers.RepoRoot, file).Replace('\\', '/'));
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because("Stripe types and package usage must stay inside Explore.Infrastructure.");
    }

    [Test]
    public async Task StripeReferenceScanner_RejectsUnknownSdkTypesWithoutMatchingCommentsOrStrings()
    {
        await Assert.That(ContainsStripeReference("global::Stripe.FutureNamespace.FutureType value = null!;")).IsTrue();
        await Assert.That(ContainsStripeReference("using Stripe.FutureNamespace;")).IsTrue();
        await Assert.That(ContainsStripeReference("var text = \"Stripe.FutureNamespace.FutureType\"; // Stripe.Other")).IsFalse();
    }

    private static bool ContainsStripeReference(string source)
    {
        string code = RemoveCommentsAndLiterals(source);
        return Regex.IsMatch(
            code,
            @"global::Stripe\.[A-Za-z_]|(?<![A-Za-z0-9_.])Stripe\.[A-Za-z_]|\busing\s+(?:global::)?Stripe(?:\.|;)",
            RegexOptions.CultureInvariant);
    }

    private static string RemoveCommentsAndLiterals(string source)
    {
        var result = new char[source.Length];
        int index = 0;
        while (index < source.Length)
        {
            if (index + 1 < source.Length && source[index] == '/' && source[index + 1] == '/')
            {
                index = SkipUntil(source, index + 2, '\n');
                continue;
            }

            if (index + 1 < source.Length && source[index] == '/' && source[index + 1] == '*')
            {
                index = SkipBlockComment(source, index + 2);
                continue;
            }

            if (source[index] == '@' && index + 1 < source.Length && source[index + 1] == '"')
            {
                index = SkipVerbatimString(source, index + 2);
                continue;
            }

            if (source[index] == '"')
            {
                int quoteCount = CountRun(source, index, '"');
                index = quoteCount >= 3
                    ? SkipRawString(source, index + quoteCount, quoteCount)
                    : SkipEscapedLiteral(source, index + 1, '"');
                continue;
            }

            if (source[index] == '\'')
            {
                index = SkipEscapedLiteral(source, index + 1, '\'');
                continue;
            }

            result[index] = source[index];
            index++;
        }

        return new string(result);
    }

    private static int SkipUntil(string source, int index, char terminator)
    {
        while (index < source.Length && source[index] != terminator)
        {
            index++;
        }

        return index;
    }

    private static int SkipBlockComment(string source, int index)
    {
        while (index + 1 < source.Length && !(source[index] == '*' && source[index + 1] == '/'))
        {
            index++;
        }

        return Math.Min(source.Length, index + 2);
    }

    private static int SkipVerbatimString(string source, int index)
    {
        while (index < source.Length)
        {
            if (source[index] != '"')
            {
                index++;
                continue;
            }

            if (index + 1 < source.Length && source[index + 1] == '"')
            {
                index += 2;
                continue;
            }

            return index + 1;
        }

        return source.Length;
    }

    private static int SkipEscapedLiteral(string source, int index, char terminator)
    {
        while (index < source.Length)
        {
            if (source[index] == '\\')
            {
                index += 2;
                continue;
            }

            if (source[index] == terminator)
            {
                return index + 1;
            }

            index++;
        }

        return source.Length;
    }

    private static int SkipRawString(string source, int index, int quoteCount)
    {
        while (index < source.Length)
        {
            if (source[index] == '"' && CountRun(source, index, '"') >= quoteCount)
            {
                return index + quoteCount;
            }

            index++;
        }

        return source.Length;
    }

    private static int CountRun(string source, int index, char value)
    {
        int start = index;
        while (index < source.Length && source[index] == value)
        {
            index++;
        }

        return index - start;
    }
}
