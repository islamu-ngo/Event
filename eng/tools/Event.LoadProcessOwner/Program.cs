namespace Event.LoadTesting;

/// <summary>
/// Standalone Linux x64 load-workload process owner used by distributed k6 integration tests.
/// </summary>
internal static class Program
{
    public static int Main(string[] args) => LoadProcessOwner.Run(args);
}
