namespace Explore.AssuranceAudit;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: Explore.AssuranceAudit <repository-root> --changed-from <git-ref> | <relative-test-path> [...]");
            return 2;
        }

        string repositoryRoot = Path.GetFullPath(args[0]);
        bool changedMode = args.Length == 3 && args[1] == "--changed-from";
        IReadOnlyList<AssuranceDiagnostic> diagnostics = changedMode
            ? AssuranceAudit.AnalyzeChangedFiles(repositoryRoot, args[2])
            : AssuranceAudit.AnalyzeFiles(repositoryRoot, args[1..]);
        foreach (AssuranceDiagnostic diagnostic in diagnostics)
        {
            Console.Error.WriteLine(diagnostic);
        }

        Console.WriteLine("Assurance audit: {0} diagnostic(s) across the {1} scope.", diagnostics.Count, changedMode ? "changed-test" : "explicit-root");
        return diagnostics.Count == 0 ? 0 : 1;
    }
}
