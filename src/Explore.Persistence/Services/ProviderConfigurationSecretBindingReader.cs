using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Services;

/// <summary>
/// Reads instance-scoped provider-configuration secret bindings from the independent Global pool
/// through <see cref="PostgresProviderConfigurationContract"/> on PostgreSQL so Runtime keeps zero direct access to <c>secret_bindings</c>.
/// </summary>
public sealed class ProviderConfigurationSecretBindingReader(
    IGlobalExploreDbContextFactory globalFactory,
    IServiceProvider services) : IProviderConfigurationSecretBindingReader
{
    public async Task<SecretBinding?> GetInstanceBindingAsync(
        string settingKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingKey);
        if (!ProviderConfigurationState.SecretKeys.Contains(settingKey, StringComparer.Ordinal))
            return null;

        await using var context = await globalFactory.CreateDbContextAsync(cancellationToken);
        if (!context.Database.IsNpgsql())
        {
            return await context.SecretBindings
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    binding => binding.SettingKey == settingKey
                        && binding.Qualifier == string.Empty
                        && binding.SettingScopeId == (int)ConfigurationScopeEnum.Instance
                        && binding.ScopeId == null,
                    cancellationToken);
        }

        await context.Database.OpenConnectionAsync(cancellationToken);
        var state = await PostgresProviderConfigurationContract.ReadAsync(
            context, services.GetRequiredService<PostgresNativeQueueEnrollment>(), null, cancellationToken);
        using var document = JsonDocument.Parse(state.Bindings);
        var match = document.RootElement.EnumerateArray().SingleOrDefault(row =>
            string.Equals(row.GetProperty(nameof(SecretBinding.SettingKey)).GetString(), settingKey, StringComparison.Ordinal)
            && row.GetProperty(nameof(SecretBinding.SettingScopeId)).GetInt32() == (int)ConfigurationScopeEnum.Instance
            && row.GetProperty(nameof(SecretBinding.ScopeId)).ValueKind == JsonValueKind.Null
            && string.Equals(row.GetProperty(nameof(SecretBinding.Qualifier)).GetString(), string.Empty, StringComparison.Ordinal));
        if (match.ValueKind == JsonValueKind.Undefined)
            return null;

        return InstanceBootstrapStateQuery.MaterializeJsonRow<SecretBinding>(context, match);
    }
}
