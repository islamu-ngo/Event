using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.RegistrationForms;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Policies;

public sealed class RegistrationFormTemplateLinkPolicy : ILinkPolicy<RegistrationFormTemplateDto>
{
    public IEnumerable<LinkDefinition> GetLinks(RegistrationFormTemplateDto dto, ClaimsPrincipal? user)
    {
        yield return LinkDefinition.Self(RouteNames.GetRegistrationFormTemplate, new { templateId = dto.Id });
        yield return new LinkDefinition(LinkRelations.Collection, RouteNames.GetRegistrationFormTemplates);
        // Instantiation has no target event yet, so the affordance is decided by authority over the tenant
        // that owns the template. The command handler re-authorizes against the event once one is named.
        yield return new LinkDefinition(
            LinkRelations.Instantiate,
            RouteNames.InstantiateRegistrationFormTemplate,
            new { templateId = dto.Id },
            HttpMethods.Post,
            RequiresAuth: true).RequirePermission(
                AuthorizationActions.Events.ManageRegistrationWorkflow,
                ResourceKinds.Event,
                null,
                dto.TenantId is null ? null : new AuthorizationScope(TenantId: dto.TenantId.ToString()),
                TenantFacts(dto));
    }

    private static LinkDefinition Action(string rel, string route, object values, string method, string action, RegistrationFormTemplateDto dto) =>
        new LinkDefinition(rel, route, values, method, RequiresAuth: true).RequirePermission(
            action,
            ResourceKinds.RegistrationForm,
            dto.Id.ToString(),
            new AuthorizationScope(TenantId: dto.TenantId?.ToString()),
            TenantFacts(dto));

    /// <summary>
    /// A template without a tenant belongs to no authority zone, so it publishes no facts and the provider
    /// denies rather than falling back to the caller's ambient tenant.
    /// </summary>
    private static IAuthorizationFacts? TenantFacts(RegistrationFormTemplateDto dto) =>
        dto.TenantId is { } tenantId ? new TenantScopedAuthorizationFacts(tenantId) : null;
}

public sealed class RegistrationFormTemplateCollectionLinkPolicy : ICollectionLinkPolicy<RegistrationFormTemplateDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(RegistrationFormTemplateDto dto, ClaimsPrincipal? user) =>
        new RegistrationFormTemplateLinkPolicy().GetLinks(dto, user);

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.Create,
            RouteNames.CreateRegistrationFormTemplate,
            null,
            HttpMethods.Post,
            RequiresAuth: true).RequirePermission(
                AuthorizationActions.RegistrationForms.Create,
                ResourceKinds.RegistrationForm,
                null,
                null,
                null);
    }
}
