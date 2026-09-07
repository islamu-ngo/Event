using System.Net.Http.Headers;
using System.Text;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Settings;
using Explore.Domain.Constants;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Integrations.Listmonk.Generated;
using Microsoft.Extensions.Logging;

namespace Explore.Infrastructure.Integrations.Listmonk;

public sealed class ListmonkConnectionTester(
    IHierarchicalSettingsResolver settingsResolver,
    ISecretResolver secretResolver,
    ITenantContext tenantContext,
    IHttpClientFactory httpClientFactory,
    ILogger<ListmonkConnectionTester> logger)
    : IListmonkConnectionTester
{
    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var context = new SettingContext(TenantId: tenantContext.TenantId);
        var instanceUrl = await settingsResolver.ResolveAsync<string>(
            GovernanceSettingKeys.Integrations.Listmonk.InstanceUrl,
            context,
            cancellationToken);
        var username = await secretResolver.ResolveAsync(
            SecretDefinitionRegistry.Keys.Integrations.Listmonk.ApiUsername,
            tenantContext.TenantId,
            cancellationToken);
        var apiKey = await secretResolver.ResolveAsync(
            SecretDefinitionRegistry.Keys.Integrations.Listmonk.ApiKey,
            tenantContext.TenantId,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(instanceUrl) ||
            string.IsNullOrWhiteSpace(username?.Value) ||
            string.IsNullOrWhiteSpace(apiKey?.Value))
        {
            return false;
        }

        try
        {
            using var client = httpClientFactory.CreateClient(ListmonkSyncService.HttpClientName);
            client.BaseAddress = NormalizeApiBaseUri(instanceUrl);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username.Value}:{apiKey.Value}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);

            var apiClient = new ListmonkApiClient(client);
            await apiClient.GetHealthCheckAsync(cancellationToken);
            return true;
        }
        catch (ApiException ex)
        {
            logger.LogWarning(
                ex,
                "Listmonk connection test failed for tenant {TenantId} with status {StatusCode}.",
                tenantContext.TenantId,
                ex.StatusCode);
            return false;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Listmonk connection test failed for tenant {TenantId}.", tenantContext.TenantId);
            return false;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Listmonk connection test timed out for tenant {TenantId}.", tenantContext.TenantId);
            return false;
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Listmonk connection test has invalid configuration for tenant {TenantId}.", tenantContext.TenantId);
            return false;
        }
    }

    private static Uri NormalizeApiBaseUri(string instanceUrl)
    {
        if (!Uri.TryCreate(instanceUrl.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Listmonk instance URL must be an absolute HTTP(S) URL.");
        }

        var path = uri.AbsolutePath.TrimEnd('/');
        var apiPath = path.EndsWith("/api", StringComparison.OrdinalIgnoreCase)
            ? path
            : $"{path}/api";
        if (string.IsNullOrWhiteSpace(apiPath) || apiPath == "/api")
            apiPath = "/api";

        var builder = new UriBuilder(uri)
        {
            Path = apiPath.TrimEnd('/') + "/",
            Query = string.Empty,
            Fragment = string.Empty
        };
        return builder.Uri;
    }
}
