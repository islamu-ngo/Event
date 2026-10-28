using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Infrastructure.Ai;
using Explore.Application.Features.AiAssistant;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using Explore.Domain;
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
        IHierarchicalSettingsResolver settings,
        IAiProviderCapabilityResolver capabilities,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var context = new SettingContext(TenantId: tenantId);
        bool personalCreation = await settings.ResolveAsync<bool>(
            GovernanceSettingKeys.Events.UserSubmissionEnabled, context, cancellationToken);
        var ai = await settings.ResolveGroupAsync<AiAssistantSettingGroup>(context, cancellationToken);
        bool normalAi = await AiAssistantAvailability.ResolveDisabledReasonAsync(
            ai, capabilities, cancellationToken) is null;
        return new(personalCreation, normalAi);
    }

    internal bool Studio(IReadOnlyList<Actor> actors) => PersonalCreation || actors.Any(actor =>
        !actor.IsDeleted && !actor.IsSuspended
        && (actor.ActorTypeId == (int)ActorTypeEnum.Organization && actor.OrganizationId.HasValue
            || actor.ActorTypeId == (int)ActorTypeEnum.Group && actor.GroupId.HasValue));
}
