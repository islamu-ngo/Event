using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.EmailDispatch;
using Explore.Application.Features.EmailDispatch;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Policies;

public sealed class EmailDispatchProcessorControlDetailLinkPolicy
    : ILinkPolicy<EmailDispatchProcessorControlDto>
{
    public IEnumerable<LinkDefinition> GetLinks(EmailDispatchProcessorControlDto dto, ClaimsPrincipal? user)
    {
        yield return Link("self", RouteNames.GetEmailDispatchProcessorControl, "GET", isUpdate: false);

        yield return dto.IsPaused
            ? Link("resume", RouteNames.ResumeEmailDispatchProcessor, "DELETE", isUpdate: true)
            : Link("pause", RouteNames.PauseEmailDispatchProcessor, "PUT", isUpdate: true);

        yield return Link("set-rate-limit", RouteNames.SetEmailDispatchGlobalRateLimitOverride, "PUT", isUpdate: true);

        if (dto.GlobalSmtpRateLimitPerMinuteOverride.HasValue)
        {
            yield return Link("clear-rate-limit", RouteNames.ClearEmailDispatchGlobalRateLimitOverride, "DELETE", isUpdate: true);
        }
    }

    private static LinkDefinition Link(string relation, string routeName, string method, bool isUpdate) =>
        isUpdate
            ? new LinkDefinition(relation, routeName, null, method, relation, RequiresAuth: true)
                .RequirePermission(AuthorizationActions.InstanceSettings.Update, ResourceKinds.InstanceSetting, EmailDispatchProcessorControl.SettingKey, facts: InstanceScopedAuthorizationFacts.Instance)
            : new LinkDefinition(relation, routeName, null, method, relation, RequiresAuth: true)
                .RequirePermission(AuthorizationActions.InstanceSettings.View, ResourceKinds.InstanceSetting, EmailDispatchProcessorControl.SettingKey, facts: InstanceScopedAuthorizationFacts.Instance);
}

public sealed class EmailDispatchProcessorControlCollectionLinkPolicy
    : ICollectionLinkPolicy<EmailDispatchProcessorControlDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(EmailDispatchProcessorControlDto dto, ClaimsPrincipal? user) => [];
    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}
