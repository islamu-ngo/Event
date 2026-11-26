using System.Collections.Immutable;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Appearance.Requests.Commands;
using Explore.Application.Settings;
using Explore.Domain.Constants;
using Explore.Domain.Settings;

namespace Explore.Infrastructure.Services;

public sealed partial class HumanSettingsMutationPreparation
{
    public Task<IPreparedHumanSettingsMutation> PrepareAsync(
        UpdateCurrentUserAppearancePreferencesCommand command,
        string parentLanguage, string parentDirection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var localization = command.Preferences.Localization
            ?? throw new ArgumentException("A localization intent is required.", nameof(command));
        return PrepareCoreAsync(command, SettingScope.User, null, Authority.Subject, null,
            (tenant, actor) =>
            {
                var effects = ImmutableArray.CreateBuilder<HumanSettingsMutation>();
                Add(GovernanceSettingKeys.Appearance.Language, localization.Language, parentLanguage);
                Add(GovernanceSettingKeys.Appearance.Direction, localization.Direction, parentDirection);
                return effects.ToImmutable();

                void Add(string key, string? input, string inherited)
                {
                    if (input is null)
                        return;
                    string normalized = input.Trim().ToLowerInvariant();
                    bool remove = string.Equals(inherited, normalized, StringComparison.Ordinal);
                    effects.Add(new(HumanSettingsOperation.UserSettings, SettingScope.User, tenant, actor,
                        key, remove ? HumanSettingsMutationKind.Remove : HumanSettingsMutationKind.SetValue,
                        remove ? null : SettingValueSerializer.Serialize(normalized)));
                }
            }, [], cancellationToken);
    }
}
