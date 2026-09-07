namespace Explore.Infrastructure.Services;

using System.Net;
using Explore.Application.Features.ConfigurationManifest.Managed;

public sealed class ConfigurationTransferDestinationResolver
    : IConfigurationTransferDestinationResolver
{
    public async Task<IReadOnlyCollection<IPAddress>> ResolveAsync(
        string host,
        CancellationToken cancellationToken) =>
        await Dns.GetHostAddressesAsync(host, cancellationToken);
}
