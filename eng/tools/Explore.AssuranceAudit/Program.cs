namespace Explore.AssuranceAudit;

internal static class Program
{
    private static readonly System.Text.Json.JsonSerializerOptions PortfolioJsonOptions = new() { WriteIndented = true };

    public static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: Explore.AssuranceAudit <repository-root> --changed-from <git-ref> | --portfolio <msbuild-json> [...] | <relative-test-path> [...]");
            return 2;
        }

        string repositoryRoot = Path.GetFullPath(args[0]);
        if (args[1] == "--portfolio")
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Portfolio inventory requires evaluated MSBuild JSON inputs.");
                return 2;
            }

            PortfolioInventory.Report report = PortfolioInventory.Create(repositoryRoot, args[2..].Select(File.ReadAllText));
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(report, PortfolioJsonOptions));
            return 0;
        }

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
