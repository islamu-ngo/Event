using Explore.API.Configuration;
using TUnit.Core;

namespace ApiIntegrationTests.Features;

public sealed class McpAdapterSettingsValidatorTests
{
    private readonly McpAdapterSettingsValidator _validator = new();

    [Test]
    public async Task Validate_WhenDefaultsAreUsed_Succeeds()
    {
        var settings = new McpAdapterSettings();

        var result = _validator.Validate(null, settings);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(settings.Enabled).IsTrue();
        await Assert.That(settings.EndpointPath).IsEqualTo("/mcp");
        await Assert.That(settings.Stateless).IsTrue();
        await Assert.That(settings.EnableLegacySse).IsTrue();
    }

    [Test]
    public async Task Validate_WhenEndpointPathIsRelative_Fails()
    {
        var result = _validator.Validate(null, new McpAdapterSettings { EndpointPath = "mcp" });

        await Assert.That(result.Failed).IsTrue();
    }

    [Test]
    public async Task Validate_WhenStatefulTransportIsRequested_Fails()
    {
        var result = _validator.Validate(null, new McpAdapterSettings { Stateless = false });

        await Assert.That(result.Failed).IsTrue();
    }

    [Test]
    public async Task Validate_WhenLegacySseCeilingIsRequested_Succeeds()
    {
        var result = _validator.Validate(null, new McpAdapterSettings { EnableLegacySse = true });

        await Assert.That(result.Succeeded).IsTrue();
    }
}
