namespace Explore.Diagnostic.AiReplay;

public sealed record AiReplayCommandOptions(
    string? RepositoryRoot,
    string? OutputDirectory,
    bool ShowHelp)
{
    public static AiReplayCommandOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

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
                case "--root" when TryReadValue(args, ref index, out var root):
                    repositoryRoot = root;
                    break;
                case "--output" when TryReadValue(args, ref index, out var output):
                    outputDirectory = output;
                    break;
            }
        }

        return new AiReplayCommandOptions(repositoryRoot, outputDirectory, showHelp);
    }

    private static bool TryReadValue(string[] args, ref int index, out string? value)
    {
        value = null;
        if (index + 1 >= args.Length)
        {
            return false;
        }

        value = string.IsNullOrWhiteSpace(args[++index]) ? null : args[index].Trim();
        return true;
    }
}
