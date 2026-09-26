using Event.Standalone.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Event.Standalone.IntegrationTests;

[NotInParallel]
public sealed class AgentBrowserAdmissionTests
{
    [Test]
    public async Task CombinedEntrypointRejectsOptInBeforeServiceCompositionEvenWhenConfigurationClaimsSplit()
    {
        await using var factory = new RejectedAgentHost();
        Exception? failure = null;
        try { using var client = factory.CreateClient(); }
        catch (Exception exception) { failure = exception; }
        await Assert.That(failure).IsNotNull();
        var messages = new List<string>();
        for (Exception? current = failure; current is not null; current = current.InnerException)
            messages.Add(current.Message);
        await Assert.That(messages.Any(message => message.Contains("agent-browser-host-unsupported", StringComparison.Ordinal))).IsTrue();
        await Assert.That(factory.ServicesComposed).IsFalse();
    }

    private sealed class RejectedAgentHost : WebApplicationFactory<StandaloneHostMarker>
    {
        public bool ServicesComposed { get; private set; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("AGENT_BROWSER_SEED_ENABLED", "true");
            builder.UseSetting("ISLAMU_ASPIRE_MODE", "AgentBrowser");
            builder.UseSetting("Hosting:Topology", "Split");
            builder.UseSetting("IdentityDatabase:Topology", "external");
            builder.ConfigureServices(_ => ServicesComposed = true);
        }
    }
}
