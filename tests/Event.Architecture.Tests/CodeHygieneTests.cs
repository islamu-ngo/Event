namespace Event.Architecture.Tests;

using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using NetArchTest.Rules;

public class CodeHygieneTests
{
    private static readonly Assembly ApiAssembly = typeof(Program).Assembly;
    private static readonly Regex CommentedTestAttributePattern = new(@"^\s*//\s*\[Test\]", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex SkipAttributePattern = new(@"\[Skip\((?<reason>.*?)\)\]", RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly string[] TestProjectDirectories =
    [
        "Event.Domain.UnitTests",
        "Event.Application.UnitTests",
        "Event.Persistence.IntegrationTests",
        "Event.API.IntegrationTests",
        "Event.Architecture.Tests",
        "Explore.Blazor.Client.Tests",
        "Explore.Blazor.IntegrationTests",
        "Explore.Secrets.UnitTests"
    ];

    #region Controller Base Class Conventions

    [Test]
    [DisplayName("Controllers that access user identity must inherit from EventControllerBase")]
    public async Task ControllersAccessingIdentity_ShouldInherit_EventControllerBase()
    {
        var controllerTypes = Types.InAssembly(ApiAssembly)
            .That()
            .Inherit(typeof(ControllerBase))
            .And()
            .AreNotAbstract()
            .GetTypes();

        var exploreBaseType = ApiAssembly.GetTypes()
            .Single(t => t.Name == "EventControllerBase");

        var violations = new List<string>();

        foreach (var controller in controllerTypes)
        {
            var declaredMethods = controller.GetMethods(
                BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);

            var accessesIdentity = declaredMethods.Any(m =>
                m.Name is "GetCurrentUserId" or "GetUserId");

            if (accessesIdentity && !exploreBaseType.IsAssignableFrom(controller))
            {
                violations.Add(controller.Name);
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because("controllers accessing user identity must inherit from EventControllerBase instead of defining local methods");
    }

    [Test]
    [DisplayName("No controller should define a local GetCurrentUserId method")]
    public async Task NoController_ShouldDefine_GetCurrentUserId()
    {
        var controllerTypes = Types.InAssembly(ApiAssembly)
            .That()
            .Inherit(typeof(ControllerBase))
            .And()
            .AreNotAbstract()
            .GetTypes();

        var violations = new List<string>();

        foreach (var controller in controllerTypes)
        {
            var methods = controller.GetMethods(
                BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);

            if (methods.Any(m => m.Name == "GetCurrentUserId"))
            {
                violations.Add(controller.Name);
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because("controllers must use EventControllerBase.CurrentUserId instead of local GetCurrentUserId methods");
    }

    #endregion

    #region Test Suite Governance

    [Test]
    public async Task TestFiles_ShouldNotDisableTests_ByCommentingOutTestAttribute()
    {
        var offenders = new List<string>();

        foreach (var file in EnumerateTestSourceFiles())
        {
            var content = await File.ReadAllTextAsync(file);
            foreach (Match match in CommentedTestAttributePattern.Matches(content))
            {
                offenders.Add($"{GetRelativePath(file)}:{GetLineNumber(content, match.Index)} contains commented-out [Test]; use [Skip(\"Category: ... Removal: ...\")] or delete the obsolete test instead.");
            }
        }

        await Assert.That(offenders).IsEmpty()
            .Because("disabled tests must be explicit and governed; a commented [Test] silently removes coverage from every CI lane");
    }

    [Test]
    public async Task SkippedTests_ShouldDeclareCategoryAndRemovalCondition()
    {
        var offenders = new List<string>();

        foreach (var file in EnumerateTestSourceFiles())
        {
            var content = await File.ReadAllTextAsync(file);
            foreach (Match match in SkipAttributePattern.Matches(content))
            {
                var skipAttribute = match.Value.ReplaceLineEndings(" ");
                if (!skipAttribute.Contains("Category:", StringComparison.Ordinal) || !skipAttribute.Contains("Removal:", StringComparison.Ordinal))
                {
                    offenders.Add($"{GetRelativePath(file)}:{GetLineNumber(content, match.Index)} skip must include both 'Category:' and 'Removal:' details.");
                }
            }
        }

        await Assert.That(offenders).IsEmpty()
            .Because("skipped tests must stay visible, categorized by suite, and include the concrete condition for re-enabling or deleting them");
    }

    private static IEnumerable<string> EnumerateTestSourceFiles()
    {
        var repoRoot = FindRepoRoot();

        return TestProjectDirectories
            .Select(directory => Path.Combine(repoRoot, directory))
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories))
            .Where(path => !IsGeneratedOrBuildOutput(path))
            .OrderBy(path => path, StringComparer.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Explore.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root containing Explore.slnx.");
    }

    private static bool IsGeneratedOrBuildOutput(string path)
    {
        var normalized = path.Replace(Path.DirectorySeparatorChar, '/');

        return normalized.Contains("/bin/", StringComparison.Ordinal)
            || normalized.Contains("/obj/", StringComparison.Ordinal)
            || normalized.EndsWith(".g.cs", StringComparison.Ordinal)
            || normalized.EndsWith(".Designer.cs", StringComparison.Ordinal);
    }

    private static string GetRelativePath(string path)
    {
        return Path.GetRelativePath(FindRepoRoot(), path).Replace(Path.DirectorySeparatorChar, '/');
    }

    private static int GetLineNumber(string content, int index)
    {
        return content[..index].Count(character => character == '\n') + 1;
    }

    #endregion
}
