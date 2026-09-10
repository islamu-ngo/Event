
using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Identity;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Application.Features.Authentication.Local.Requests.Queries;
using Explore.Application.Hateoas;
using Microsoft.AspNetCore.Http;

namespace Explore.API.Hateoas.Policies;

public sealed class LocalIdentityDetailLinkPolicy : ILinkPolicy<LocalIdentitySummary>
{
    public IEnumerable<LinkDefinition> GetLinks(LocalIdentitySummary dto, ClaimsPrincipal? user)
    {
        yield return LocalIdentityAdministrationLinks.Collection();
        if (dto.CurrentOperationId is { } operationId && operationId != Guid.Empty)
        {
            yield return LocalIdentityAdministrationLinks.Operation(operationId: operationId, relation: LinkRelations.Related);
        }
        if (LocalIdentityAdministrationLinks.CanReset(dto))
        {
            yield return LocalIdentityAdministrationLinks.Reset(localSubjectId: dto.LocalSubjectId);
        }
    }
}

public sealed class LocalIdentityCollectionLinkPolicy : ICollectionLinkPolicy<LocalIdentitySummary>
{
    public IEnumerable<LinkDefinition> GetItemLinks(LocalIdentitySummary dto, ClaimsPrincipal? user)
    {
        if (dto.CurrentOperationId is { } operationId && operationId != Guid.Empty)
        {
            yield return LocalIdentityAdministrationLinks.Operation(operationId: operationId, relation: LinkRelations.Related);
        }
        if (LocalIdentityAdministrationLinks.CanReset(dto))
        {
            yield return LocalIdentityAdministrationLinks.Reset(localSubjectId: dto.LocalSubjectId);
        }
    }

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user)
    {
        yield return LocalIdentityAdministrationLinks.Create();
    }
}

public sealed class LocalCredentialOperationDetailLinkPolicy : ILinkPolicy<LocalCredentialOperationStatus>
{
    public IEnumerable<LinkDefinition> GetLinks(LocalCredentialOperationStatus dto, ClaimsPrincipal? user)
    {
        yield return LocalIdentityAdministrationLinks.Operation(operationId: dto.Receipt.OperationId, relation: LinkRelations.Self);
        yield return LocalIdentityAdministrationLinks.Collection();
        if (LocalIdentityAdministrationLinks.CanReconcile(dto))
        {
            yield return LocalIdentityAdministrationLinks.Reconcile(operationId: dto.Receipt.OperationId);
        }
    }
}

public sealed class LocalCredentialOperationCollectionLinkPolicy : ICollectionLinkPolicy<LocalCredentialOperationStatus>
{
    public IEnumerable<LinkDefinition> GetItemLinks(LocalCredentialOperationStatus dto, ClaimsPrincipal? user)
    {
        yield return LocalIdentityAdministrationLinks.Operation(operationId: dto.Receipt.OperationId, relation: LinkRelations.Self);
        if (LocalIdentityAdministrationLinks.CanReconcile(dto))
        {
            yield return LocalIdentityAdministrationLinks.Reconcile(operationId: dto.Receipt.OperationId);
        }
    }
    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}

public sealed class LocalCredentialIssueDetailLinkPolicy : ILinkPolicy<LocalCredentialIssueDto>
{
    public IEnumerable<LinkDefinition> GetLinks(LocalCredentialIssueDto dto, ClaimsPrincipal? user)
    {
        yield return LocalIdentityAdministrationLinks.Operation(operationId: dto.Operation.Receipt.OperationId, relation: LinkRelations.Self);
        yield return LocalIdentityAdministrationLinks.Collection();
        if (LocalIdentityAdministrationLinks.CanReconcile(dto.Operation))
        {
            yield return LocalIdentityAdministrationLinks.Reconcile(operationId: dto.Operation.Receipt.OperationId);
        }
    }
}

public sealed class LocalCredentialIssueCollectionLinkPolicy : ICollectionLinkPolicy<LocalCredentialIssueDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(LocalCredentialIssueDto dto, ClaimsPrincipal? user)
    {
        yield return LocalIdentityAdministrationLinks.Operation(operationId: dto.Operation.Receipt.OperationId, relation: LinkRelations.Self);
        if (LocalIdentityAdministrationLinks.CanReconcile(dto.Operation))
        {
            yield return LocalIdentityAdministrationLinks.Reconcile(operationId: dto.Operation.Receipt.OperationId);
        }
    }
    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}

internal static class LocalIdentityAdministrationLinks
{
    internal static bool CanReset(LocalIdentitySummary identity)
        => identity.HasExactBinding
            && identity.CredentialState is LocalCredentialState.ChangeRequired or LocalCredentialState.Ready
            && identity.CurrentOperationId is { } operationId && operationId != Guid.Empty
            && identity.CurrentOperationConcurrencyStamp is { } stamp && stamp != Guid.Empty;

    internal static bool CanReconcile(LocalCredentialOperationStatus operation)
        => operation.IsCurrent
            && operation.CredentialState == LocalCredentialState.ProvisioningPending
            && operation.Receipt.Kind == LocalCredentialOperationKind.Create
            && operation.Receipt.Stage == LocalCredentialOperationStage.ProvisioningPending;

    internal static LinkDefinition Collection()
        => RequireInstancePermission(
            link: new LinkDefinition(Rel: LinkRelations.Collection, RouteName: RouteNames.ListLocalIdentities,
                Method: HttpMethods.Get, Title: "Local identities", RequiresAuth: true),
            action: AuthorizationActions.InstanceSettings.View);

    internal static LinkDefinition Create()
        => RequireInstancePermission(
            link: new LinkDefinition(Rel: LinkRelations.CreateLocalIdentity, RouteName: RouteNames.CreateLocalIdentity,
                Method: HttpMethods.Post, Title: "Create Local identity", RequiresAuth: true),
            action: AuthorizationActions.InstanceSettings.Update);

    internal static LinkDefinition Operation(Guid operationId, string relation)
        => RequireInstancePermission(
            link: new LinkDefinition(Rel: relation, RouteName: RouteNames.GetLocalCredentialOperation,
                RouteValues: new { operationId }, Method: HttpMethods.Get,
                Title: "Local credential operation", RequiresAuth: true),
            action: AuthorizationActions.InstanceSettings.View);

    internal static LinkDefinition Reset(Guid localSubjectId)
        => RequireInstancePermission(
            link: new LinkDefinition(Rel: LinkRelations.IssueTemporaryCredential, RouteName: RouteNames.ResetLocalCredential,
                RouteValues: new { userId = localSubjectId }, Method: HttpMethods.Post,
                Title: "Issue temporary credential", RequiresAuth: true),
            action: AuthorizationActions.InstanceSettings.Update);

    internal static LinkDefinition Reconcile(Guid operationId)
        => RequireInstancePermission(
            link: new LinkDefinition(Rel: LinkRelations.Reconcile, RouteName: RouteNames.ReconcileLocalCredentialOperation,
                RouteValues: new { operationId }, Method: HttpMethods.Post,
                Title: "Reconcile Local credential operation", RequiresAuth: true),
            action: AuthorizationActions.InstanceSettings.Update);

    private static LinkDefinition RequireInstancePermission(LinkDefinition link, string action)
        => link.RequirePermission(action, ResourceKinds.InstanceSetting,
            ListLocalIdentitiesQuery.ResourceKey, facts: InstanceScopedAuthorizationFacts.Instance);
}
