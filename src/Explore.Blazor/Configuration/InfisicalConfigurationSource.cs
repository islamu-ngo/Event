namespace Explore.Blazor.Configuration;

using Microsoft.Extensions.Configuration;

public sealed class InfisicalConfigurationSource : IConfigurationSource
{
    public required string Url { get; set; }

    public required string ProjectId { get; set; }

    public required string ClientId { get; set; }

    public required string ClientSecret { get; set; }

    public required string Environment { get; set; }

    public List<string> Paths { get; } = ["/"];

    /// <summary>
    /// Supplies an owned authority transport; omission uses the normal IPv4 handler.
    /// </summary>
    public Func<HttpMessageHandler>? MessageHandlerFactory { get; init; }

    public IConfigurationProvider Build(IConfigurationBuilder builder) =>
        new InfisicalConfigurationProvider(this);
}
