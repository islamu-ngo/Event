using Microsoft.Extensions.Configuration;

namespace Explore.Persistence.Security;

/// <summary>One original runner invocation; inherited execution contexts cannot retain it after exit.</summary>
internal sealed class ConfigurationManifestStartupInvocation
{
    private static readonly AsyncLocal<ConfigurationManifestStartupInvocation?> Current = new();
    private volatile bool active = true;
    private readonly ConfigurationManifestStartupRunner runner;
    private ConfigurationManifestStartupInvocation(ConfigurationManifestStartupRunner runner) => this.runner = runner;
    internal IConfiguration Configuration => runner.Configuration;

    internal static async Task RunAsync(ConfigurationManifestStartupRunner runner,
        Func<CancellationToken, Task> operation, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(runner);
        if (Current.Value is { active: true })
            throw new UnauthorizedAccessException("Post-migration startup cannot nest its original invocation.");
        var invocation = new ConfigurationManifestStartupInvocation(runner);
        Current.Value = invocation;
        try { await operation(token); }
        finally { invocation.active = false; Current.Value = null; }
    }

    internal static ConfigurationManifestStartupInvocation RequireCurrent() =>
        Current.Value is { active: true } current ? current
            : throw new UnauthorizedAccessException("The actual retained startup runner invocation is required.");
}
