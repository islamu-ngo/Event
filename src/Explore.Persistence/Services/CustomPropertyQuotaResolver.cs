using System.Globalization;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain.Settings;

namespace Explore.Persistence.Services;

public class CustomPropertyQuotaResolver : ICustomPropertyQuotaResolver
{
    private readonly ITenantSettingRepository _tenantSettingRepository;
    private readonly ISystemSettingRepository _systemSettingRepository;

    public CustomPropertyQuotaResolver(
        ITenantSettingRepository tenantSettingRepository,
        ISystemSettingRepository systemSettingRepository)
    {
        _tenantSettingRepository = tenantSettingRepository;
        _systemSettingRepository = systemSettingRepository;
    }

    public async Task<int> GetIntAsync(string key, Guid tenantId, CancellationToken cancellationToken)
    {
        var raw = await ResolveRawAsync(key, tenantId);
        return ParseInt(raw, key);
    }

    public async Task<bool> GetBoolAsync(string key, Guid tenantId, CancellationToken cancellationToken)
    {
        var raw = await ResolveRawAsync(key, tenantId);
        return ParseBool(raw, key);
    }

    private async Task<string> ResolveRawAsync(string key, Guid tenantId)
    {
        var tenantOverride = await _tenantSettingRepository.GetByTenantAndKey(tenantId, key);
        if (tenantOverride is not null)
        {
            return tenantOverride.Value;
        }

        var systemOverride = await _systemSettingRepository.GetByKey(key);
        if (systemOverride is not null && !string.IsNullOrEmpty(systemOverride.Value))
        {
            return systemOverride.Value;
        }

        var definition = SettingRegistry.Get(key)
            ?? throw new InvalidOperationException($"Custom-property quota setting '{key}' is not registered.");
        return definition.DefaultValue;
    }

    private static int ParseInt(string raw, string key)
    {
        try
        {
            using var document = JsonDocument.Parse(raw);
            return document.RootElement.ValueKind switch
            {
                JsonValueKind.Number => document.RootElement.GetInt32(),
                JsonValueKind.String => int.Parse(document.RootElement.GetString()!, CultureInfo.InvariantCulture),
                _ => throw new InvalidOperationException($"Quota '{key}' is not an integer JSON value."),
            };
        }
        catch (JsonException)
        {
            return int.Parse(raw, CultureInfo.InvariantCulture);
        }
    }

    private static bool ParseBool(string raw, string key)
    {
        try
        {
            using var document = JsonDocument.Parse(raw);
            return document.RootElement.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => bool.Parse(document.RootElement.GetString()!),
                _ => throw new InvalidOperationException($"Quota '{key}' is not a boolean JSON value."),
            };
        }
        catch (JsonException)
        {
            return bool.Parse(raw);
        }
    }
}
