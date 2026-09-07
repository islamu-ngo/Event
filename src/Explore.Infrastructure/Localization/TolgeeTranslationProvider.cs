using System.Net.Http.Headers;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Secrets;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Localization.Generated.Tolgee;
using Microsoft.Extensions.Logging;

namespace Explore.Infrastructure.Localization;

/// <summary>
/// Tolgee Translation Management System provider.
/// API docs: https://tolgee.io/api
/// Auth: X-API-Key header.
/// </summary>
public class TolgeeTranslationProvider : ITranslationManagementProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ITranslationConfigResolver _configResolver;
    private readonly ISecretResolver _secretResolver;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<TolgeeTranslationProvider> _logger;

    public TolgeeTranslationProvider(
        IHttpClientFactory httpClientFactory,
        ITranslationConfigResolver configResolver,
        ISecretResolver secretResolver,
        ITenantContext tenantContext,
        ILogger<TolgeeTranslationProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configResolver = configResolver;
        _secretResolver = secretResolver;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    private async Task<TolgeeApiClient?> CreateApiAsync(TranslationConfiguration config, CancellationToken ct)
    {
        var apiKey = await ResolveApiKeyAsync(ct);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Tolgee request skipped: localization TMS API key is not configured");
            return null;
        }

        var client = _httpClientFactory.CreateClient("TolgeeClient");
        client.BaseAddress = new Uri(config.ApiUrl!.TrimEnd('/'));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-API-Key", apiKey);
        return new TolgeeApiClient(client);
    }

    private bool TryGetProjectId(TranslationConfiguration config, out long projectId)
    {
        if (long.TryParse(config.ProjectId, out projectId))
            return true;

        _logger.LogWarning("Tolgee request skipped: ProjectId must be the numeric Tolgee project id");
        return false;
    }

    private async Task<string?> ResolveApiKeyAsync(CancellationToken ct)
    {
        var secret = await _secretResolver.ResolveAsync(
            SecretDefinitionRegistry.Keys.Localization.TmsApiKey,
            _tenantContext.TenantId,
            ct);
        return string.IsNullOrWhiteSpace(secret?.Value) ? null : secret.Value.Trim();
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        var config = await _configResolver.ResolveAsync(ct);
        if (string.IsNullOrWhiteSpace(config.ApiUrl) || string.IsNullOrWhiteSpace(config.ProjectId) || !TryGetProjectId(config, out var projectId))
            return false;

        try
        {
            var api = await CreateApiAsync(config, ct);
            if (api is null) return false;
            await api.GetProjectAsync(projectId, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tolgee connection test failed");
            return false;
        }
    }

    public async Task ImportKeysAsync(IEnumerable<TranslationKeyImport> keys, CancellationToken ct = default)
    {
        var config = await _configResolver.ResolveAsync(ct);
        if (string.IsNullOrWhiteSpace(config.ApiUrl) || string.IsNullOrWhiteSpace(config.ProjectId) || !TryGetProjectId(config, out var projectId))
        {
            _logger.LogWarning("Tolgee import skipped: missing ApiUrl or ProjectId");
            return;
        }

        var keyList = keys.ToList();
        if (keyList.Count == 0) return;

        var payload = new ImportKeysResolvableDto
        {
            Keys = keyList.Select(k => new ImportKeysResolvableItemDto
            {
                Name = k.KeyName,
                Translations = k.Translations.ToDictionary(
                    pair => pair.Key,
                    pair => new ImportTranslationResolvableDto { Text = pair.Value })
            }).ToList()
        };

        var api = await CreateApiAsync(config, ct);
        if (api is null) return;
        await api.ImportKeysAsync(projectId, payload, cancellationToken: ct);

        _logger.LogInformation("Tolgee import completed: {Count} keys", keyList.Count);
    }

    public async Task<IEnumerable<TranslationExport>> ExportTranslationsAsync(string languageCode, CancellationToken ct = default)
    {
        var config = await _configResolver.ResolveAsync(ct);
        if (string.IsNullOrWhiteSpace(config.ApiUrl) || string.IsNullOrWhiteSpace(config.ProjectId) || !TryGetProjectId(config, out var projectId))
            return [];

        var api = await CreateApiAsync(config, ct);
        if (api is null) return [];
        var translations = await api.GetAllTranslationsAsync(projectId, languageCode, ".", cancellationToken: ct);

        if (!translations.TryGetValue(languageCode, out var languageTranslations))
            return [];

        return languageTranslations
            .Where(kvp => kvp.Value is not null)
            .Select(kvp => new TranslationExport(kvp.Key, kvp.Value!));
    }

    public async Task<IEnumerable<string>> GetAvailableLanguagesAsync(CancellationToken ct = default)
    {
        var config = await _configResolver.ResolveAsync(ct);
        if (string.IsNullOrWhiteSpace(config.ApiUrl) || string.IsNullOrWhiteSpace(config.ProjectId) || !TryGetProjectId(config, out var projectId))
            return [];

        var api = await CreateApiAsync(config, ct);
        if (api is null) return [];
        var response = await api.GetLanguagesAsync(projectId, ct);

        return response._embedded?.Languages?
            .Select(l => l.Tag)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag!) ?? [];
    }
}
