using System.Net.Http.Headers;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;

namespace Event.Persistence.IntegrationTests.Fixtures;

internal sealed partial class AuthenticatedSettingsFixture
{
    internal async Task<HttpResponseMessage> ExportConfigurationManifestViaApiAsync(
        ConfigurationManifestExportView view,
        CancellationToken cancellationToken,
        ConfigurationManifestExportScope? scope = null)
    {
        string path = $"/api/admin/instance/configuration-manifest/export?view={view}";
        if (scope is { } selectedScope)
            path += $"&scope={selectedScope}";
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await client.SendAsync(request, cancellationToken);
    }
}
