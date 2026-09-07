using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Contracts.Services.Webhooks;

namespace Explore.Blazor.Client.Services;

public sealed class ListmonkIntegrationSettingsService(
    IListmonkIntegrationSettingsClient apiClient,
    ILogger<ListmonkIntegrationSettingsService> logger) : IListmonkIntegrationSettingsService
{
    public async Task<ListmonkIntegrationSettingsDto?> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await apiClient.GetListmonkIntegrationSettingsAsync(cancellationToken: cancellationToken);
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Listmonk settings load failed with status {StatusCode}.", ex.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Listmonk settings load failed.");
            return null;
        }
    }

    public async Task<WebhookActionResult> UpdateSettingsAsync(
        UpdateListmonkIntegrationSettingsDto request,
        CancellationToken cancellationToken = default) =>
        await ExecuteCommandAsync(
            () => apiClient.UpdateListmonkIntegrationSettingsAsync(request, cancellationToken: cancellationToken),
            "Listmonk settings saved.",
            "Unable to save Listmonk settings.");

    public async Task<WebhookActionResult> TestConnectionAsync(CancellationToken cancellationToken = default) =>
        await ExecuteCommandAsync(
            () => apiClient.TestListmonkIntegrationConnectionAsync(cancellationToken: cancellationToken),
            "Listmonk connection OK.",
            "Listmonk connection failed.");

    private async Task<WebhookActionResult> ExecuteCommandAsync(
        Func<Task<BaseCommandResponseOfGuid>> action,
        string successMessage,
        string fallbackFailureMessage)
    {
        try
        {
            return FromCommandResponse(await action(), successMessage, fallbackFailureMessage);
        }
        catch (ApiException<BaseCommandResponseOfGuid> ex)
        {
            logger.LogWarning(ex, "Listmonk command failed with status {StatusCode}.", ex.StatusCode);
            return FromCommandResponse(ex.Result, successMessage, fallbackFailureMessage);
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Listmonk command failed with status {StatusCode}.", ex.StatusCode);
            return WebhookActionResult.Failed(fallbackFailureMessage);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Listmonk command failed.");
            return WebhookActionResult.Failed(fallbackFailureMessage);
        }
    }

    private static WebhookActionResult FromCommandResponse(
        BaseCommandResponseOfGuid? response,
        string successMessage,
        string fallbackFailureMessage)
    {
        if (response?.Success == true)
        {
            return WebhookActionResult.Succeeded(response.Message ?? successMessage, response.Id);
        }

        var errors = response?.Errors is { Count: > 0 }
            ? string.Join(" ", response.Errors)
            : null;
        return WebhookActionResult.Failed(errors ?? response?.Message ?? fallbackFailureMessage);
    }
}
