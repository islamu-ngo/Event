using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Integrations.Listmonk.Generated;
using Microsoft.Extensions.Logging;

namespace Explore.Infrastructure.Integrations.Listmonk;

public sealed class ListmonkSyncService(
    IHttpClientFactory httpClientFactory,
    IHierarchicalSettingsResolver settingsResolver,
    ISecretResolver secretResolver,
    ILogger<ListmonkSyncService> logger)
{
    public const string HttpClientName = "ListmonkClient";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ListmonkSyncResult> SyncSubscriberAsync(
        IntegrationSyncOutbox outbox,
        Func<CancellationToken, Task<bool>> beginProviderHandoff,
        CancellationToken cancellationToken)
    {
        if (outbox.Kind != IntegrationKind.Listmonk)
        {
            return ListmonkSyncResult.DefiniteFailure("Integration outbox row is not a Listmonk sync.");
        }

        var settingContext = new SettingContext(TenantId: outbox.TenantId);
        var instanceUrl = await settingsResolver.ResolveAsync<string>(
            GovernanceSettingKeys.Integrations.Listmonk.InstanceUrl,
            settingContext,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(instanceUrl))
        {
            return ListmonkSyncResult.Retryable("Listmonk instance URL is not configured.");
        }

        var username = await secretResolver.ResolveAsync(
            SecretDefinitionRegistry.Keys.Integrations.Listmonk.ApiUsername,
            outbox.TenantId,
            cancellationToken);
        var apiKey = await secretResolver.ResolveAsync(
            SecretDefinitionRegistry.Keys.Integrations.Listmonk.ApiKey,
            outbox.TenantId,
            cancellationToken);

        if (!username.IsResolved || string.IsNullOrWhiteSpace(username.Value) ||
            !apiKey.IsResolved || string.IsNullOrWhiteSpace(apiKey.Value))
        {
            return ListmonkSyncResult.Retryable("Listmonk API credentials are not configured.");
        }

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            client.BaseAddress = NormalizeApiBaseUri(instanceUrl);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Authorization = BuildBasicAuthHeader(username.Value, apiKey.Value);
            var apiClient = new ListmonkApiClient(client);
            var subscriber = BuildSubscriber(outbox);
            if (!await beginProviderHandoff(cancellationToken))
            {
                return ListmonkSyncResult.LostClaim();
            }

            await apiClient.CreateSubscriberAsync(subscriber, cancellationToken);
            return ListmonkSyncResult.Success();
        }
        catch (ApiException ex)
        {
            logger.LogWarning(
                "Listmonk subscriber sync failed. StatusCode={StatusCode}",
                ex.StatusCode);
            if (ex.StatusCode == 408 || ex.StatusCode >= 500)
            {
                return ListmonkSyncResult.Ambiguous($"Listmonk API returned HTTP {ex.StatusCode}.");
            }

            return ex.StatusCode == 429
                ? ListmonkSyncResult.Retryable("Listmonk API rate limit rejected the request.")
                : ListmonkSyncResult.DefiniteFailure($"Listmonk API returned HTTP {ex.StatusCode}.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("Listmonk subscriber sync transport failed. FailureType={FailureType}", ex.GetType().Name);
            return ListmonkSyncResult.Ambiguous("Listmonk API request outcome is uncertain.");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Listmonk subscriber sync timed out. FailureType={FailureType}", ex.GetType().Name);
            return ListmonkSyncResult.Ambiguous("Listmonk API request outcome is uncertain.");
        }
        catch (JsonException ex)
        {
            logger.LogWarning("Listmonk subscriber payload is invalid. FailureType={FailureType}", ex.GetType().Name);
            return ListmonkSyncResult.DefiniteFailure("Listmonk subscriber payload is invalid.");
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning("Listmonk subscriber configuration is invalid. FailureType={FailureType}", ex.GetType().Name);
            return ListmonkSyncResult.DefiniteFailure("Listmonk subscriber configuration is invalid.");
        }
    }

    private static AuthenticationHeaderValue BuildBasicAuthHeader(string username, string apiKey)
    {
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{apiKey}"));
        return new AuthenticationHeaderValue("Basic", credentials);
    }

    private static NewSubscriber BuildSubscriber(IntegrationSyncOutbox outbox)
    {
        var subscriber = JsonSerializer.Deserialize<NewSubscriber>(outbox.SubscriberPayloadJson, JsonOptions)
            ?? new NewSubscriber();

        subscriber.Email ??= outbox.SubscriberEmail;
        subscriber.Name ??= string.IsNullOrWhiteSpace(outbox.SubscriberName)
            ? outbox.SubscriberEmail
            : outbox.SubscriberName;
        subscriber.Status ??= "enabled";
        subscriber.Preconfirm_subscriptions ??= outbox.PreconfirmSubscriptions;

        if (subscriber.Lists is null || !subscriber.Lists.Contains(outbox.ListmonkListId))
        {
            subscriber.Lists = new List<int> { outbox.ListmonkListId };
        }

        return subscriber;
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

public sealed record ListmonkSyncResult(ListmonkSyncOutcome Outcome, string? ErrorMessage)
{
    public bool Succeeded => Outcome == ListmonkSyncOutcome.Succeeded;
    public bool IsRetryable => Outcome == ListmonkSyncOutcome.Retryable;

    public static ListmonkSyncResult Success() => new(ListmonkSyncOutcome.Succeeded, null);
    public static ListmonkSyncResult Retryable(string errorMessage) => new(ListmonkSyncOutcome.Retryable, errorMessage);
    public static ListmonkSyncResult DefiniteFailure(string errorMessage) => new(ListmonkSyncOutcome.DefiniteFailure, errorMessage);
    public static ListmonkSyncResult Ambiguous(string errorMessage) => new(ListmonkSyncOutcome.Ambiguous, errorMessage);
    public static ListmonkSyncResult LostClaim() => new(ListmonkSyncOutcome.LostClaim, null);
}

public enum ListmonkSyncOutcome
{
    Succeeded = 1,
    Retryable = 2,
    DefiniteFailure = 3,
    Ambiguous = 4,
    LostClaim = 5
}
