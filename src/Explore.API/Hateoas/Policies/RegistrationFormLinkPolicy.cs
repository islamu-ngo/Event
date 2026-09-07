using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.RegistrationForms;
using Explore.Application.Hateoas;
using Explore.Domain.Enums;

namespace Explore.API.Hateoas.Policies;

public sealed class RegistrationWorkflowLinkPolicy : ILinkPolicy<RegistrationWorkflowDto>
{
    public IEnumerable<LinkDefinition> GetLinks(RegistrationWorkflowDto dto, ClaimsPrincipal? user)
    {
        yield return LinkDefinition.Self(RouteNames.GetRegistrationWorkflow, new { eventId = dto.EventId, purpose = dto.Purpose });
        yield return new LinkDefinition(LinkRelations.Event, RouteNames.GetEventManagementDetails, new { id = dto.EventId });
        yield return EventAction(LinkRelations.Edit, RouteNames.UpdateRegistrationWorkflow, new { eventId = dto.EventId, workflowId = dto.Id }, HttpMethods.Patch, dto);
        yield return EventAction(LinkRelations.CreateRequirement, RouteNames.CreateRegistrationRequirement, new { eventId = dto.EventId, workflowId = dto.Id }, HttpMethods.Post, dto);
        yield return FormAction(LinkRelations.CreateForm, RouteNames.CreateRegistrationForm, new { eventId = dto.EventId, workflowId = dto.Id }, HttpMethods.Post, dto);
    }

    public IEnumerable<LinkDefinition> GetRequirementLinks(RegistrationWorkflowDto workflow, RegistrationRequirementDto requirement)
    {
        yield return EventAction(LinkRelations.Edit, RouteNames.UpdateRegistrationRequirement, new { eventId = workflow.EventId, workflowId = workflow.Id, requirementId = requirement.Id }, HttpMethods.Patch, workflow);
        yield return EventAction(LinkRelations.Delete, RouteNames.DeleteRegistrationRequirement, new { eventId = workflow.EventId, workflowId = workflow.Id, requirementId = requirement.Id }, HttpMethods.Delete, workflow);
        if (requirement.IsAttached)
        {
            yield return RequirementAction(
                LinkRelations.Detach,
                RouteNames.DetachRegistrationRequirement,
                HttpMethods.Delete,
                AuthorizationActions.RegistrationForms.Detach,
                workflow,
                requirement);
        }
        else
        {
            yield return RequirementAction(
                LinkRelations.Attach,
                RouteNames.AttachRegistrationRequirement,
                HttpMethods.Post,
                AuthorizationActions.RegistrationForms.Attach,
                workflow,
                requirement);
        }
    }

    private static LinkDefinition EventAction(string rel, string route, object values, string method, RegistrationWorkflowDto dto) =>
        new LinkDefinition(rel, route, values, method, RequiresAuth: true).RequirePermission(
            AuthorizationActions.Events.ManageRegistrationWorkflow, ResourceKinds.Event, dto.EventId.ToString(),
            new AuthorizationScope(TenantId: dto.TenantId.ToString()),
            new EventScopedAuthorizationFacts(dto.TenantId, dto.EventId));

    private static LinkDefinition FormAction(string rel, string route, object values, string method, RegistrationWorkflowDto dto) =>
        new LinkDefinition(rel, route, values, method, RequiresAuth: true).RequirePermission(
            AuthorizationActions.RegistrationForms.Create, ResourceKinds.RegistrationForm, dto.EventId.ToString(),
            new AuthorizationScope(TenantId: dto.TenantId.ToString()),
            new EventScopedAuthorizationFacts(dto.TenantId, dto.EventId));

    private static LinkDefinition RequirementAction(
        string rel,
        string route,
        string method,
        string action,
        RegistrationWorkflowDto workflow,
        RegistrationRequirementDto requirement) =>
        new LinkDefinition(
            rel,
            route,
            new { eventId = workflow.EventId, requirementId = requirement.Id },
            method,
            RequiresAuth: true).RequirePermission(
                action,
                ResourceKinds.RegistrationForm,
                requirement.Id.ToString(),
                new AuthorizationScope(TenantId: workflow.TenantId.ToString()),
                new EventScopedAuthorizationFacts(workflow.TenantId, workflow.EventId));
}

public sealed class RegistrationFormLinkPolicy : ILinkPolicy<RegistrationFormDto>
{
    public IEnumerable<LinkDefinition> GetLinks(RegistrationFormDto dto, ClaimsPrincipal? user)
    {
        yield return LinkDefinition.Self(RouteNames.GetRegistrationForm, new { eventId = dto.EventId, formId = dto.Id });
        yield return new LinkDefinition(LinkRelations.Event, RouteNames.GetEventManagementDetails, new { id = dto.EventId });
        yield return Action(LinkRelations.CreateVersion, RouteNames.CreateRegistrationFormVersion, new { eventId = dto.EventId, formId = dto.Id }, HttpMethods.Post, AuthorizationActions.RegistrationForms.Create, dto);
    }

    public IEnumerable<LinkDefinition> GetVersionLinks(RegistrationFormDto form, RegistrationFormVersionSummaryDto version)
    {
        yield return LinkDefinition.Self(RouteNames.GetRegistrationFormVersion, new { eventId = form.EventId, formId = form.Id, versionId = version.Id });
    }

    internal static LinkDefinition Action(string rel, string route, object values, string method, string action, RegistrationFormDto dto) =>
        new LinkDefinition(rel, route, values, method, RequiresAuth: true).RequirePermission(
            action, ResourceKinds.RegistrationForm, dto.Id.ToString(),
            new AuthorizationScope(TenantId: dto.TenantId.ToString()),
            new EventScopedAuthorizationFacts(dto.TenantId, dto.EventId));
}

public sealed class RegistrationFormVersionLinkPolicy : ILinkPolicy<RegistrationFormVersionDto>
{
    public IEnumerable<LinkDefinition> GetLinks(RegistrationFormVersionDto dto, ClaimsPrincipal? user)
    {
        yield return LinkDefinition.Self(RouteNames.GetRegistrationFormVersion, Values(dto));
        yield return new LinkDefinition(LinkRelations.Form, RouteNames.GetRegistrationForm, new { eventId = dto.EventId, formId = dto.RegistrationFormId });
        yield return Action(LinkRelations.Preflight, RouteNames.GetRegistrationFormPublishPreflight, Values(dto), HttpMethods.Post, AuthorizationActions.RegistrationForms.Preflight, dto);
        if (!IsDraft(dto)) yield break;
        yield return Action(LinkRelations.Publish, RouteNames.PublishRegistrationFormVersion, Values(dto), HttpMethods.Post, AuthorizationActions.RegistrationForms.Publish, dto);
        yield return Action(LinkRelations.AddSection, RouteNames.AddRegistrationFormSection, Values(dto), HttpMethods.Post, AuthorizationActions.RegistrationForms.Create, dto);
        yield return Action(LinkRelations.ReorderSections, RouteNames.ReorderRegistrationFormSections, Values(dto), HttpMethods.Put, AuthorizationActions.RegistrationForms.Update, dto);
        yield return Action(LinkRelations.AddRule, RouteNames.AddRegistrationFormRule, Values(dto), HttpMethods.Post, AuthorizationActions.RegistrationForms.Create, dto);
    }

    public IEnumerable<LinkDefinition> GetSectionLinks(RegistrationFormVersionDto version, RegistrationFormSectionDto section)
    {
        if (!IsDraft(version)) yield break;
        yield return Action(LinkRelations.Edit, RouteNames.UpdateRegistrationFormSection, new { eventId = version.EventId, formId = version.RegistrationFormId, versionId = version.Id, sectionId = section.Id }, HttpMethods.Patch, AuthorizationActions.RegistrationForms.Update, version);
        yield return Action(LinkRelations.Delete, RouteNames.DeleteRegistrationFormSection, new { eventId = version.EventId, formId = version.RegistrationFormId, versionId = version.Id, sectionId = section.Id }, HttpMethods.Delete, AuthorizationActions.RegistrationForms.Delete, version);
        yield return Action(LinkRelations.AddField, RouteNames.AddRegistrationFormField, new { eventId = version.EventId, formId = version.RegistrationFormId, versionId = version.Id, sectionId = section.Id }, HttpMethods.Post, AuthorizationActions.RegistrationForms.Create, version);
        yield return Action(LinkRelations.ReorderFields, RouteNames.ReorderRegistrationFormFields, new { eventId = version.EventId, formId = version.RegistrationFormId, versionId = version.Id, sectionId = section.Id }, HttpMethods.Put, AuthorizationActions.RegistrationForms.Update, version);
    }

    public IEnumerable<LinkDefinition> GetFieldLinks(RegistrationFormVersionDto version, RegistrationFormSectionDto section, RegistrationFormFieldDto field)
    {
        if (!IsDraft(version)) yield break;
        var values = new { eventId = version.EventId, formId = version.RegistrationFormId, versionId = version.Id, sectionId = section.Id, fieldId = field.Id };
        yield return Action(LinkRelations.Edit, RouteNames.UpdateRegistrationFormField, values, HttpMethods.Patch, AuthorizationActions.RegistrationForms.Update, version);
        yield return Action(LinkRelations.Delete, RouteNames.DeleteRegistrationFormField, values, HttpMethods.Delete, AuthorizationActions.RegistrationForms.Delete, version);
        yield return Action(LinkRelations.AddOption, RouteNames.AddRegistrationFormFieldOption, values, HttpMethods.Post, AuthorizationActions.RegistrationForms.Create, version);
    }

    public IEnumerable<LinkDefinition> GetOptionLinks(RegistrationFormVersionDto version, RegistrationFormSectionDto section, RegistrationFormFieldDto field, RegistrationFormFieldOptionDto option)
    {
        if (!IsDraft(version)) yield break;
        var values = new { eventId = version.EventId, formId = version.RegistrationFormId, versionId = version.Id, sectionId = section.Id, fieldId = field.Id, optionId = option.Id };
        yield return Action(LinkRelations.Edit, RouteNames.UpdateRegistrationFormFieldOption, values, HttpMethods.Patch, AuthorizationActions.RegistrationForms.Update, version);
        yield return Action(LinkRelations.Retire, RouteNames.RetireRegistrationFormFieldOption, values, HttpMethods.Delete, AuthorizationActions.RegistrationForms.Update, version);
    }

    public IEnumerable<LinkDefinition> GetRuleLinks(RegistrationFormVersionDto version, RegistrationFormRuleDto rule)
    {
        if (!IsDraft(version)) yield break;
        var values = new { eventId = version.EventId, formId = version.RegistrationFormId, versionId = version.Id, ruleId = rule.Id };
        yield return Action(LinkRelations.Edit, RouteNames.UpdateRegistrationFormRule, values, HttpMethods.Patch, AuthorizationActions.RegistrationForms.Update, version);
        yield return Action(LinkRelations.Delete, RouteNames.DeleteRegistrationFormRule, values, HttpMethods.Delete, AuthorizationActions.RegistrationForms.Delete, version);
    }

    private static bool IsDraft(RegistrationFormVersionDto dto) => dto.StatusId == (int)RegistrationFormStatusEnum.Draft;
    private static object Values(RegistrationFormVersionDto dto) => new { eventId = dto.EventId, formId = dto.RegistrationFormId, versionId = dto.Id };
    private static LinkDefinition Action(string rel, string route, object values, string method, string action, RegistrationFormVersionDto dto) =>
        new LinkDefinition(rel, route, values, method, RequiresAuth: true).RequirePermission(
            action, ResourceKinds.RegistrationForm, dto.RegistrationFormId.ToString(),
            new AuthorizationScope(TenantId: dto.TenantId.ToString()),
            new EventScopedAuthorizationFacts(dto.TenantId, dto.EventId));
}

public sealed class RegistrationFormPublishPreflightLinkPolicy : ILinkPolicy<RegistrationFormPublishPreflightDto>
{
    public IEnumerable<LinkDefinition> GetLinks(RegistrationFormPublishPreflightDto dto, ClaimsPrincipal? user) => [];
}

public sealed class RegistrationWorkflowCollectionLinkPolicy(RegistrationWorkflowLinkPolicy detail) : ICollectionLinkPolicy<RegistrationWorkflowDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(RegistrationWorkflowDto dto, ClaimsPrincipal? user) => detail.GetLinks(dto, user);
    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}

public sealed class RegistrationFormCollectionLinkPolicy(RegistrationFormLinkPolicy detail) : ICollectionLinkPolicy<RegistrationFormDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(RegistrationFormDto dto, ClaimsPrincipal? user) => detail.GetLinks(dto, user);
    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}

public sealed class RegistrationFormVersionCollectionLinkPolicy(RegistrationFormVersionLinkPolicy detail) : ICollectionLinkPolicy<RegistrationFormVersionDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(RegistrationFormVersionDto dto, ClaimsPrincipal? user) => detail.GetLinks(dto, user);
    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}

public sealed class RegistrationFormPublishPreflightCollectionLinkPolicy(RegistrationFormPublishPreflightLinkPolicy detail) : ICollectionLinkPolicy<RegistrationFormPublishPreflightDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(RegistrationFormPublishPreflightDto dto, ClaimsPrincipal? user) => detail.GetLinks(dto, user);
    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}
