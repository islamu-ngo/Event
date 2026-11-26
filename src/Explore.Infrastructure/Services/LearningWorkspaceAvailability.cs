using Explore.Application.Contracts.Infrastructure.Ai;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Settings;
using Explore.Application.Features.AiAssistant;
using Explore.Domain;
using Explore.Domain.Ai;
using Explore.Domain.Constants;
using Explore.Domain.Enums;

namespace Explore.Infrastructure.Services;

/// <summary>
/// Uses the shell's native settings and provider readiness without opening an AI
/// conversation. Credential readiness is observed only outside a learning transaction.
/// </summary>
internal sealed record LearningWorkspaceAvailability(bool PersonalCreation, bool NormalAi)
{
    internal static async Task<LearningWorkspaceAvailability> ObserveAsync(
        LearningSourceFacts sources,
        IAiProviderCapabilityResolver capabilities,
        CancellationToken cancellationToken)
    {
        var ai = sources.CreateAiSettings();
        bool delegationLocked = sources.EffectiveSettings.TryGetValue(
                GovernanceSettingKeys.TenantDelegation.LockAiAssistant, out var delegation)
            && SettingValueSerializer.DeserializeBool(delegation.Value, true);
        bool normalAi = !(delegationLocked && (ai.TenantOwnedTransport || ai.CredentialScope == AiCredentialScope.Tenant))
            && await AiAssistantAvailability.ResolveDisabledReasonAsync(ai, capabilities, cancellationToken) is null;
        return new(sources.PersonalCreation, normalAi);
    }

    internal bool Studio(IReadOnlyList<Actor> actors) => PersonalCreation || actors.Any(actor =>
        !actor.IsDeleted && !actor.IsSuspended
        && (actor.ActorTypeId == (int)ActorTypeEnum.Organization && actor.OrganizationId.HasValue
            || actor.ActorTypeId == (int)ActorTypeEnum.Group && actor.GroupId.HasValue));
}
