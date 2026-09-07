namespace Explore.Diagnostic.Doctor;

public sealed record DoctorCommandOptions(string? RepositoryRoot, TimeSpan Timeout, bool ShowHelp)
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    public static DoctorCommandOptions Parse(string[] args)
    {
        string? repositoryRoot = null;
        var timeout = DefaultTimeout;
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
                case "--timeout-seconds" when index + 1 < args.Length && int.TryParse(args[index + 1], out var seconds):
                    index++;
                    timeout = TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 60));
                    break;
            }
        }

        return new DoctorCommandOptions(repositoryRoot, timeout, showHelp);
    }
}
