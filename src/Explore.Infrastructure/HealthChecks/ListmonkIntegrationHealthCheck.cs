using System.Net.Http.Headers;
using System.Text;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Settings;
using Explore.Domain.Constants;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Integrations.Listmonk;
using Explore.Infrastructure.Integrations.Listmonk.Generated;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Explore.Infrastructure.HealthChecks;

public sealed class ListmonkIntegrationHealthCheck(
    IInstanceSystemSettingsReader settingsReader,
    ISecretResolver secretResolver,
    IHttpClientFactory httpClientFactory)
    : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var stored = (await settingsReader.ReadAsync(
        [
            GovernanceSettingKeys.Integrations.Listmonk.Enabled,
            GovernanceSettingKeys.Integrations.Listmonk.SyncOnRegistration,
            GovernanceSettingKeys.Integrations.Listmonk.PreconfirmSubscriptions,
            GovernanceSettingKeys.Integrations.Listmonk.InstanceUrl,
            GovernanceSettingKeys.Integrations.Listmonk.DefaultListId
        ], cancellationToken)).ToDictionary(row => row.SettingKey, StringComparer.Ordinal);
        T Resolve<T>(string key, T defaultValue) =>
            SettingValueSerializer.Deserialize(HierarchicalSettingMerge.Resolve(key, stored)?.Value, defaultValue);
        var enabled = Resolve(GovernanceSettingKeys.Integrations.Listmonk.Enabled, false);
        var syncOnRegistration = Resolve(GovernanceSettingKeys.Integrations.Listmonk.SyncOnRegistration, false);
        var preconfirmSubscriptions = Resolve(GovernanceSettingKeys.Integrations.Listmonk.PreconfirmSubscriptions, false);

        if (!enabled)
        {
            return HealthCheckResult.Healthy(
                "Listmonk integration is disabled.",
                BuildData(enabled, syncOnRegistration, preconfirmSubscriptions));
        }

        var instanceUrl = Resolve<string?>(GovernanceSettingKeys.Integrations.Listmonk.InstanceUrl, null);
        var defaultListId = Resolve(GovernanceSettingKeys.Integrations.Listmonk.DefaultListId, 0);
        var username = await secretResolver.ResolveAsync(
            SecretDefinitionRegistry.Keys.Integrations.Listmonk.ApiUsername,
            tenantId: null,
            cancellationToken);
        var apiKey = await secretResolver.ResolveAsync(
            SecretDefinitionRegistry.Keys.Integrations.Listmonk.ApiKey,
            tenantId: null,
            cancellationToken);

        var data = BuildData(
            enabled,
            syncOnRegistration,
            preconfirmSubscriptions,
            !string.IsNullOrWhiteSpace(instanceUrl),
            defaultListId > 0,
            username.IsResolved,
            apiKey.IsResolved,
            username.Status.ToString(),
            apiKey.Status.ToString());

        if (string.IsNullOrWhiteSpace(instanceUrl) ||
            defaultListId <= 0 ||
            !username.IsResolved || string.IsNullOrWhiteSpace(username.Value) ||
            !apiKey.IsResolved || string.IsNullOrWhiteSpace(apiKey.Value))
        {
            return HealthCheckResult.Degraded(
                "Listmonk integration is enabled but configuration is incomplete.",
                data: data);
        }

        try
        {
            var client = httpClientFactory.CreateClient(ListmonkSyncService.HttpClientName);
            client.BaseAddress = NormalizeApiBaseUri(instanceUrl);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Authorization = BuildBasicAuthHeader(username.Value, apiKey.Value);

            var apiClient = new ListmonkApiClient(client);
            await apiClient.GetHealthCheckAsync(cancellationToken);

            return HealthCheckResult.Healthy("Listmonk API health check succeeded.", data);
        }
        catch (ApiException ex)
        {
            return HealthCheckResult.Degraded(
                $"Listmonk API returned HTTP {ex.StatusCode}.",
                data: data);
        }
        catch (HttpRequestException)
        {
            return HealthCheckResult.Degraded("Listmonk API request failed.", data: data);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Degraded("Listmonk API request timed out.", data: data);
        }
        catch (InvalidOperationException)
        {
            return HealthCheckResult.Degraded("Listmonk integration configuration is invalid.", data: data);
        }
    }

    private static Dictionary<string, object> BuildData(
        bool enabled,
        bool syncOnRegistration,
        bool preconfirmSubscriptions,
        bool instanceUrlConfigured = false,
        bool defaultListIdConfigured = false,
        bool apiUsernameResolved = false,
        bool apiKeyResolved = false,
        string apiUsernameState = "Unconfigured",
        string apiKeyState = "Unconfigured")
    {
        return new Dictionary<string, object>
        {
            ["enabled"] = enabled,
            ["syncOnRegistration"] = syncOnRegistration,
            ["preconfirmSubscriptions"] = preconfirmSubscriptions,
            ["instanceUrlConfigured"] = instanceUrlConfigured,
            ["defaultListIdConfigured"] = defaultListIdConfigured,
            ["apiUsernameResolved"] = apiUsernameResolved,
            ["apiKeyResolved"] = apiKeyResolved,
            ["apiUsernameState"] = apiUsernameState,
            ["apiKeyState"] = apiKeyState
        };
    }

    private static AuthenticationHeaderValue BuildBasicAuthHeader(string username, string apiKey)
    {
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{apiKey}"));
        return new AuthenticationHeaderValue("Basic", credentials);
    }

    private static Uri NormalizeApiBaseUri(string instanceUrl)
    {
        if (!Uri.TryCreate(instanceUrl.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Listmonk instance URL must be an absolute HTTP(S) URL.");
        }

        var builder = new UriBuilder(uri) { Query = string.Empty, Fragment = string.Empty };
        var path = builder.Path.TrimEnd('/');

        if (!path.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
        {
            path = string.IsNullOrEmpty(path) ? "/api" : $"{path}/api";
        }

        builder.Path = $"{path}/";
        return builder.Uri;
    }
}
