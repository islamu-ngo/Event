namespace Explore.API.Configuration;

public sealed class McpAdapterSettings
{
    public const string SectionName = "Mcp";

    public bool Enabled { get; set; } = true;

    public string EndpointPath { get; set; } = "/mcp";

    public bool Stateless { get; set; } = true;

    public bool EnableLegacySse { get; set; } = true;
}
