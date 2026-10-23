using System.Collections.Immutable;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Settings;
using FluentValidation;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Infrastructure.Services;

public class ResolverConfigService : IResolverConfigService
{
    private const string CacheKey = "ResolverConfigService.Configuration";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private readonly ISystemSettingRepository _systemSettingRepository;
    private readonly IMemoryCache _cache;
    private readonly IServiceProvider _services;

    public ResolverConfigService(
        ISystemSettingRepository systemSettingRepository,
        IMemoryCache cache,
        IServiceProvider services)
    {
        _systemSettingRepository = systemSettingRepository;
        _cache = cache;
        _services = services;
    }

    public async Task<ResolverConfigurationDto> GetConfigurationAsync(CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(CacheKey, out ResolverConfigurationDto? cached) && cached != null)
        {
            return Copy(cached);
        }

        var rows = ImmutableDictionary.CreateBuilder<string, ResolverConfigurationSettingSnapshot?>(StringComparer.Ordinal);
        foreach (string key in ResolverConfigurationMutation.Keys)
        {
            var row = await _systemSettingRepository.GetByKey(key, cancellationToken);
            rows.Add(key, row is null ? null : ResolverConfigurationSettingSnapshot.From(row));
        }
        var configuration = ResolverConfigurationMutation.Project(rows.ToImmutable());
        _cache.Set(CacheKey, configuration, CacheDuration);
        return Copy(configuration);
    }

    public async Task<BaseCommandResponse<Guid>> ApplyConfigurationAsync(
        UpdateResolverConfigurationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await using var selection = await _services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, cancellationToken);
        var selected = selection.Services;
        var source = await selected.GetRequiredService<IResolverConfigurationMutationAuthority>()
            .CaptureAsync(command, cancellationToken);
        var owner = selected.GetRequiredService<IUnitOfWork>();
        var writer = selected.GetRequiredService<IResolverConfigurationSettingsWriter>();
        var before = await writer.ObserveAsync(source, owner, cancellationToken);
        if (!command.Patch.HasChanges())
        {
            const string message = "Resolver configuration patch must include at least one setting.";
            return BaseCommandResponse.Validation<Guid>([message], message);
        }
        ResolverConfigurationMutation compiled;
        try
        {
            compiled = await ResolverConfigurationMutation.CompileAsync(command, source, owner, before,
                writer.NormalizeEffectTimestamp(selected.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime),
                cancellationToken);
        }
        catch (ValidationException exception)
        {
            return BaseCommandResponse.Validation<Guid>(exception.Errors.Select(error => error.ErrorMessage),
                "Invalid resolver configuration.");
        }
        using (compiled)
        {
            var preparation = selected.GetRequiredService<IHumanSettingsMutationPreparation>();
            var proof = await preparation.PrepareAsync(command, compiled, cancellationToken);
            await preparation.ExecutePreparedAsync(proof, async (_, token) =>
            {
                await writer.ExecuteAsync(compiled, token);
                return true;
            }, cancellationToken);
        }
        // Only acknowledged physical settlement activates the routing projection. There is
        // no resolver network preflight, remote handoff, setup enrollment or startup writer.
        InvalidateCache();
        return BaseCommandResponse.Success(Guid.Empty, "Tenant resolver configuration updated successfully.");
    }

    public void InvalidateCache()
    {
        _cache.Remove(CacheKey);
    }

    private static ResolverConfigurationDto Copy(ResolverConfigurationDto source)
    {
        return new ResolverConfigurationDto
        {
            HeaderEnabled = source.HeaderEnabled,
            SubdomainEnabled = source.SubdomainEnabled,
            CustomDomainEnabled = source.CustomDomainEnabled,
            PathEnabled = source.PathEnabled,
            PathPrefix = source.PathPrefix,
            ReservedSlugs = source.ReservedSlugs.ToArray(),
            InstanceBaseDomain = source.InstanceBaseDomain,
            AllowTenantCustomDomains = source.AllowTenantCustomDomains
        };
    }

}
