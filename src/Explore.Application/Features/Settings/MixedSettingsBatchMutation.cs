using System.Collections.Immutable;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Settings;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;

namespace Explore.Application.Features.Settings;

/// <summary>
/// Classifies original batches that require a combined source rather than ordinary custody.
/// </summary>
public static class MixedSettingsBatchMutation
{
    public static bool RequiresCombinedSource(UpdateSettingBatchCommand command)
    {
        bool email = command.Values.Keys.Any(EmailDeliverySettingKeys.Contains);
        if (email)
            return command.Values.Keys.Any(key => !EmailDeliverySettingKeys.Contains(key));
        if (command.Scope is not (SettingScope.Instance or SettingScope.Tenant))
            return false;
        bool Specialized(string key) => EventResourceSettingMutationGuard.Handles(key)
            || VisitorAccessSettingMutationGuard.Handles(key)
            || PublicationPolicySettingKeys.All.Contains(key, StringComparer.Ordinal)
            || LocationPrivacySettingDefinitions.All.Any(definition => definition.Key == key);
        return command.Values.Keys.Any(Specialized)
            && command.Values.Keys.Any(key => !Specialized(key));
    }

}
