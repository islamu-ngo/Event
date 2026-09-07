using System.Text;

namespace ISLAMU.AgentWorkflow.Tests;

[NotInParallel("AgentWorkflowConsole")]
public sealed class GuardTests
{
    [Test]
    public async Task IntentsYamlMustBeOneValidDocument()
    {
        string path = Path.Combine(Path.GetTempPath(), $"intents-{Guid.NewGuid():N}.yaml");
        try
        {
            File.WriteAllText(path, "intents:\n  - id: event-read\n", new UTF8Encoding(false));
            await Assert.That(Invoke(["validate-intents", path])).IsEqualTo(0);

            File.WriteAllText(path, "intents:\n  - id: [\n", new UTF8Encoding(false));
            await Assert.That(Invoke(["validate-intents", path])).IsEqualTo(2);

            File.WriteAllText(path, "intents:\n  - id: event-read\n", Encoding.Unicode);
            await Assert.That(Invoke(["validate-intents", path])).IsEqualTo(2);

            await Assert.That(Invoke(["validate-intents", string.Empty])).IsEqualTo(2);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task CommitGuardAcceptsOnlyDistinctLiteralFiles()
    {
        await Assert.That(Invoke([
            "validate-commit", "--", "git", "commit", "--only", "-m", "safe", "--", "src/Event.cs", "docs/README.md",
        ])).IsEqualTo(0);

        string[][] unsafePaths = [["."], ["src/*.cs"], ["../escape.cs"], ["src/"], [":(glob)src/*.cs"], ["src/Event.cs", "src/Event.cs"]];
        foreach (string[] paths in unsafePaths)
        {
            await Assert.That(Invoke(["validate-commit", "--", "git", "commit", "--", .. paths])).IsEqualTo(2);
        }

        await Assert.That(Invoke(["validate-commit", "--", "git", "commit", "--", ".", "--", "src/Event.cs"])).IsEqualTo(2);
        await Assert.That(Invoke(["validate-commit", "--", "git", "commit", "--all", "--", "src/Event.cs"])).IsEqualTo(2);
    }

    private static int Invoke(string[] args)
    {
        TextWriter output = Console.Out;
        TextWriter error = Console.Error;
        try
        {
            Console.SetOut(TextWriter.Null);
            Console.SetError(TextWriter.Null);
            return Program.Main(args);
        }
        finally
        {
            Console.SetOut(output);
            Console.SetError(error);
        }
    }
}
