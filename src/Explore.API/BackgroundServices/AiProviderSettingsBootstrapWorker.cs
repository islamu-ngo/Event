using Explore.Application.Contracts.Infrastructure.Ai;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Settings;
using Explore.Domain.Constants;
using Explore.Infrastructure.Ai;
using Microsoft.Extensions.Options;

namespace Explore.API.BackgroundServices;

public sealed class AiProviderSettingsBootstrapWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<AiProviderSettings> options,
    ILogger<AiProviderSettingsBootstrapWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await SeedMissingDefaultsAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("AI provider settings bootstrap worker stopped before seeding defaults");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AI provider settings bootstrap worker failed to seed defaults");
        }
    }

    private async Task SeedMissingDefaultsAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var providerId = settings.Provider;
        if (!settings.Enabled || providerId == AiProviderSettings.ProviderNone)
        {
            return;
        }

        if ((providerId == AiProviderSettings.ProviderOpenAi || providerId == AiProviderSettings.ProviderAnthropic)
            && (string.IsNullOrWhiteSpace(settings.ApiKey) || string.IsNullOrWhiteSpace(settings.ModelId)))
        {
            return;
        }

        if ((providerId == AiProviderSettings.ProviderOpenAiCompatible || providerId == AiProviderSettings.ProviderAnthropicCompatible)
            && (string.IsNullOrWhiteSpace(settings.EndpointUrl) || string.IsNullOrWhiteSpace(settings.ModelId)))
        {
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var systemSettings = scope.ServiceProvider.GetRequiredService<ISystemSettingRepository>();
        var upsertService = scope.ServiceProvider.GetRequiredService<SettingUpsertService>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await unitOfWork.ExecuteInTransactionAsync(
            async _ =>
            {
                await SeedIfMissingAsync(systemSettings, upsertService, GovernanceSettingKeys.AiAssistant.Enabled, settings.Enabled).ConfigureAwait(false);
                await SeedIfMissingAsync(systemSettings, upsertService, GovernanceSettingKeys.AiAssistant.Provider, AiProviderDefaults.ProviderIdToLabel(providerId)).ConfigureAwait(false);

                if (providerId == AiProviderSettings.ProviderOpenAi
                    || providerId == AiProviderSettings.ProviderAnthropic
                    || providerId == AiProviderSettings.ProviderOpenAiCompatible
                    || providerId == AiProviderSettings.ProviderAnthropicCompatible)
                {
                    var modelId = settings.ModelId.Trim();
                    if (providerId == AiProviderSettings.ProviderOpenAiCompatible
                        || providerId == AiProviderSettings.ProviderAnthropicCompatible)
                    {
                        await SeedIfMissingAsync(systemSettings, upsertService, GovernanceSettingKeys.AiAssistant.EndpointUrl, settings.EndpointUrl.Trim()).ConfigureAwait(false);
                    }

                    await SeedIfMissingAsync(systemSettings, upsertService, GovernanceSettingKeys.AiAssistant.ModelId, modelId).ConfigureAwait(false);
                    await SeedIfMissingAsync(systemSettings, upsertService, GovernanceSettingKeys.AiAssistant.AllowedModelIds, new[] { modelId }).ConfigureAwait(false);

                    if (!string.IsNullOrWhiteSpace(settings.ApiKey))
                    {
                        await SeedIfMissingAsync(systemSettings, upsertService, GovernanceSettingKeys.AiAssistant.ApiKey, settings.ApiKey.Trim()).ConfigureAwait(false);
                    }
                }

                await SeedIfMissingAsync(systemSettings, upsertService, GovernanceSettingKeys.AiAssistant.MaxInputTokens, settings.MaxInputTokens).ConfigureAwait(false);
                await SeedIfMissingAsync(systemSettings, upsertService, GovernanceSettingKeys.AiAssistant.MaxOutputTokens, settings.MaxOutputTokens).ConfigureAwait(false);
                await SeedIfMissingAsync(systemSettings, upsertService, GovernanceSettingKeys.AiAssistant.Temperature, settings.Temperature).ConfigureAwait(false);
                await SeedIfMissingAsync(systemSettings, upsertService, GovernanceSettingKeys.AiAssistant.TimeoutSeconds, settings.TimeoutSeconds).ConfigureAwait(false);
                await SeedIfMissingAsync(systemSettings, upsertService, GovernanceSettingKeys.AiAssistant.RetentionDays, settings.RetentionDays).ConfigureAwait(false);
                await SeedIfMissingAsync(systemSettings, upsertService, GovernanceSettingKeys.AiAssistant.DailyMessageLimit, settings.DailyMessageLimit).ConfigureAwait(false);
                await SeedIfMissingAsync(systemSettings, upsertService, GovernanceSettingKeys.AiAssistant.ToolProposalsEnabled, settings.ToolProposalsEnabled).ConfigureAwait(false);
                await SeedIfMissingAsync(systemSettings, upsertService, GovernanceSettingKeys.AiAssistant.StreamingEnabled, settings.StreamingEnabled).ConfigureAwait(false);
                await SeedIfMissingAsync(systemSettings, upsertService, GovernanceSettingKeys.AiAssistant.AllowAnonymousAccess, false).ConfigureAwait(false);
                await SeedIfMissingAsync(systemSettings, upsertService, GovernanceSettingKeys.TenantDelegation.LockAiAssistant, true).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task SeedIfMissingAsync<T>(
        ISystemSettingRepository systemSettings,
        SettingUpsertService upsertService,
        string key,
        T value)
    {
        if (await systemSettings.GetByKey(key).ConfigureAwait(false) is not null)
        {
            return;
        }

        await upsertService.UpsertValueAsync(key, SettingValueSerializer.Serialize(value), isLocked: true, actorId: null).ConfigureAwait(false);
    }

}
