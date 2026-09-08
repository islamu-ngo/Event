using System.Net.Http.Headers;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Secrets;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Localization.Generated.Weblate;
using Microsoft.Extensions.Logging;

namespace Explore.Infrastructure.Localization;

/// <summary>
/// Weblate Translation Management System provider.
/// API docs: https://docs.weblate.org/en/latest/api.html
/// Auth: Authorization: Token {token} header.
/// </summary>
public class WeblateTranslationProvider : ITranslationManagementProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ITranslationConfigResolver _configResolver;
    private readonly ISecretResolver _secretResolver;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<WeblateTranslationProvider> _logger;

    public WeblateTranslationProvider(
        IHttpClientFactory httpClientFactory,
        ITranslationConfigResolver configResolver,
        ISecretResolver secretResolver,
        ITenantContext tenantContext,
        ILogger<WeblateTranslationProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configResolver = configResolver;
        _secretResolver = secretResolver;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    private async Task<WeblateApiClient?> CreateApiAsync(TranslationConfiguration config, CancellationToken ct)
    {
        var apiKey = await ResolveApiKeyAsync(ct);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Weblate request skipped: localization TMS API key is not configured");
            return null;
        }

        var client = _httpClientFactory.CreateClient("WeblateClient");
        client.BaseAddress = new Uri(config.ApiUrl!.TrimEnd('/'));
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Token", apiKey);
        return new WeblateApiClient(client);
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
        if (string.IsNullOrWhiteSpace(config.ApiUrl) || string.IsNullOrWhiteSpace(config.ProjectId))
            return false;

        try
        {
            var api = await CreateApiAsync(config, ct);
            if (api is null) return false;
            await api.Api_projects_retrieveAsync(config.ProjectId, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Weblate connection test failed");
            return false;
        }
    }

    public async Task ImportKeysAsync(IEnumerable<TranslationKeyImport> keys, CancellationToken ct = default)
    {
        var config = await _configResolver.ResolveAsync(ct);
        if (string.IsNullOrWhiteSpace(config.ApiUrl) || string.IsNullOrWhiteSpace(config.ProjectId) || string.IsNullOrWhiteSpace(config.Component))
        {
            _logger.LogWarning("Weblate import skipped: missing ApiUrl, ProjectId, or Component");
            return;
        }

        var keyList = keys.ToList();
        if (keyList.Count == 0) return;

        var languageCodes = keyList.SelectMany(k => k.Translations.Keys).Distinct().ToList();
        var api = await CreateApiAsync(config, ct);
        if (api is null) return;

        foreach (var lang in languageCodes)
        {
            var payload = keyList
                .Where(k => k.Translations.ContainsKey(lang))
                .ToDictionary(k => k.KeyName, k => k.Translations[lang]);

            var json = JsonSerializer.SerializeToUtf8Bytes(payload);
            await using var stream = new MemoryStream(json);
            var file = new FileParameter(stream, $"{lang}.json", "application/json");

            await api.Api_translations_file_createAsync(
                config.ProjectId,
                config.Component,
                lang,
                file,
                UploadMethod.Translate,
                UploadFuzzy.Process,
                UploadConflicts.Replace,
                ct);
        }

        _logger.LogInformation("Weblate import completed: {Count} keys across {LangCount} languages", keyList.Count, languageCodes.Count);
    }

    public async Task<IEnumerable<TranslationExport>> ExportTranslationsAsync(string languageCode, CancellationToken ct = default)
    {
        var config = await _configResolver.ResolveAsync(ct);
        if (string.IsNullOrWhiteSpace(config.ApiUrl) || string.IsNullOrWhiteSpace(config.ProjectId) || string.IsNullOrWhiteSpace(config.Component))
            return [];

        var api = await CreateApiAsync(config, ct);
        if (api is null) return [];
        using var response = await api.Api_translations_file_retrieveAsync(config.ProjectId, config.Component, languageCode, ct);
        var translations = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(
            response.Stream,
            cancellationToken: ct);

        if (translations is null)
            return [];

        return translations.Select(kvp => new TranslationExport(kvp.Key, kvp.Value));
    }

    public async Task<IEnumerable<string>> GetAvailableLanguagesAsync(CancellationToken ct = default)
    {
        var config = await _configResolver.ResolveAsync(ct);
        if (string.IsNullOrWhiteSpace(config.ApiUrl) || string.IsNullOrWhiteSpace(config.ProjectId))
            return [];

        var api = await CreateApiAsync(config, ct);
        if (api is null) return [];
        var response = await api.Api_projects_languages_listAsync(config.ProjectId, ct);

        return response
            .Select(l => l.Code)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code!);
    }
}
