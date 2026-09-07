namespace Explore.Diagnostic.AiEvaluation;

public sealed record AiEvaluationCommandOptions(
    string? RepositoryRoot,
    string? OutputDirectory,
    bool ShowHelp)
{
    public static AiEvaluationCommandOptions Parse(string[] args)
    {
        string? repositoryRoot = null;
        string? outputDirectory = null;
        var showHelp = false;

        for (var index = 0; index < args.Length; index++)
        {
            var current = args[index];
            switch (current)
            {
                case "--help":
                case "-h":
                    showHelp = true;
                    break;
                case "--root" when index + 1 < args.Length:
                    repositoryRoot = args[++index];
                    break;
                case "--output" when index + 1 < args.Length:
                    outputDirectory = args[++index];
                    break;
            }
        }

        return new AiEvaluationCommandOptions(repositoryRoot, outputDirectory, showHelp);
    }
}
