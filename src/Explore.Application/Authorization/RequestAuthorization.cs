using System.Diagnostics;
using System.Reflection;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Exceptions;
using Explore.Application.Features.Webhooks.Requests.Commands;
using Explore.Application.Features.OrganizationMembers.Requests.Commands;
using Explore.Application.Features.OrganizationMembers.Requests.Queries;
using Explore.Application.Features.TenantUserRoleGrants.Requests.Commands;
using Explore.Application.Features.TenantUserRoleGrants.Requests.Queries;
using Explore.Application.Features.Tenants.Requests.Commands;
using Explore.Application.Features.SupportAccess.Requests.Commands;
using Explore.Application.Features.SupportAccess.Requests.Queries;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Application.Features.EventPrograms.Requests.Queries;
using Explore.Application.Features.EventCustomProperties.Requests.Queries;
using Explore.Application.Features.RegistrationOrders.Requests.Queries;
using Explore.Application.Features.EventRoleAssignments.Requests.Queries;
using Explore.Application.Features.EventSessionTemplateSync.Queries.GetEventSessionTemplateSyncHistory;
using Explore.Application.Operations;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Authorization;

/// <summary>
/// Evaluates the existing request authorization contract in increasing trust order:
/// request facts, typed enrichment, persisted resource facts, then the selected provider.
/// Unannotated public, worker and handler-owned authorities remain enforced by their owners.
/// </summary>
public sealed class RequestAuthorization<TRequest>(
    IAuthorizationProvider authorizationProvider,
    IMachinePrincipalAccessor machinePrincipalAccessor,
    AuthorizationResourceContextResolver? resourceContextResolver = null,
    IAuthorizationContextEnricher<TRequest>? authorizationContextEnricher = null)
    where TRequest : notnull
{
    private static readonly AuthorizeResourceAttribute? Attribute = typeof(TRequest).GetCustomAttribute<AuthorizeResourceAttribute>();
    private static readonly ActivitySource AuthorizationActivitySource = new("Explore.Authorization");
    private readonly AuthorizationResourceContextResolver _resourceContextResolver = resourceContextResolver ?? new();

    public async Task AuthorizeAsync(TRequest request, ILogger logger, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Attribute is null)
            return;

        // The Footer owner evaluates the unchanged annotation on original source inputs,
        // outside the physical attempt. Machine operations retain this existing evaluator.
        if (machinePrincipalAccessor.Current is null && FooterOperationScope.Handles(typeof(TRequest)))
            return;

        // The creator selects its Global owner before evaluating this same annotation
        // from original human sources; earlier principal projection would bind Standard authority.
        if (machinePrincipalAccessor.Current is null && request is CreateTenantCommand)
            return;

        // Mandatory private readers evaluate these annotated actions from original
        // human sources and retain their authority through native read finalization.
        if (machinePrincipalAccessor.Current is null && request is
            GetEventManagementDetailsRequest or GetEventPublishReadinessRequest or GetManagedEventProgramRequest
            or GetManagedEventCustomPropertiesRequest or GetEventRegistrationOrdersQuery
            or GetEventTeamListRequest or GetAssignableEventRolePresetsRequest or GetManagedEventTeamRequest
            or GetManagedSessionTemplateSyncHistoryQuery)
            return;

        // These closed commands acquire original human authority in their own physical
        // owner or joined remote-release boundary. An earlier request-cache PDP decision
        // is neither that authority nor an additional provider route. Machine callers
        // still require the annotated outer evaluation, as do all other request types.
        if (machinePrincipalAccessor.Current is null && request is
            CreateWebhookConsumerCommand or CreateWebhookEndpointCommand
            or UpdateWebhookConsumerProviderModeCommand or UpdateWebhookEndpointCommand
            or ArchiveWebhookEndpointCommand or PauseWebhookEndpointCommand
            or ResumeWebhookEndpointCommand or RotateWebhookEndpointSecretCommand
            or TestWebhookEndpointCommand or RetryWebhookDeliveryAttemptCommand
            or ScheduleWebhookBulkReplayCommand or CancelWebhookBulkReplayCommand
            or RepairWebhookProviderBindingCommand or ReconcileWebhookProviderPublicationCommand
            or AbandonWebhookProviderPublicationCommand or OpenSvixAppPortalCommand)
            return;

        // These selected account owners evaluate their unchanged annotations from
        // original sources and retain the decision through commit or disclosure.
        if (machinePrincipalAccessor.Current is null && request is
            CreateTenantUserRoleGrantCommand or RevokeTenantUserRoleGrantCommand
            or AddOrganizationMemberCommand or UpdateOrganizationMemberRoleCommand
            or DeleteOrganizationMemberCommand
            or GetTenantUserRoleGrantDetailsRequest or GetTenantUserRoleGrantListRequest
            or GetOrganizationMemberDetailsRequest or GetOrganizationMembersRequest)
            return;

        if (machinePrincipalAccessor.Current is null && request is
            StartSupportAccessSessionCommand or StopSupportAccessSessionCommand
            or ForceStopSupportAccessSessionCommand or ListSupportAccessSessionsQuery
            or GetSupportAccessAuditEventsQuery)
            return;

        var resourceId = typeof(TRequest).Name;
        IAuthorizationFacts? facts = null;
        if (request is ISecureRequest secureRequest)
        {
            resourceId = secureRequest.ResourceId ?? resourceId;
            facts = secureRequest.AuthorizationFacts;
        }
        if (authorizationContextEnricher is not null)
        {
            var context = await authorizationContextEnricher.ResolveAsync(request, cancellationToken);
            resourceId = context.ResourceId ?? resourceId;
            facts = context.Facts ?? facts;
        }
        var resolvedContext = await _resourceContextResolver.ResolveAsync(
            request, Attribute.Resource, Attribute.Action, resourceId, facts, cancellationToken);

        using var activity = AuthorizationActivitySource.StartActivity("authorization.evaluate");
        activity?.SetTag("resource.kind", Attribute.Resource);
        activity?.SetTag("resource.action", Attribute.Action);
        activity?.SetTag("request.type", typeof(TRequest).Name);
        var correlationId = Activity.Current?.Id ?? string.Empty;
        var decision = await authorizationProvider.AuthorizeAsync(
            new AuthorizationRequest(
                AuthorizationCapabilityCatalog.Require(Attribute.Resource, Attribute.Action),
                resolvedContext.ResourceId ?? resourceId,
                Facts: resolvedContext.Facts),
            cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        if (!decision.IsAllowed)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Authorization denied");
            logger.LogWarning(
                "Authorization decision: {Decision} request={RequestType} resource={Resource} action={Action} reason={Reason} provider={Provider} correlationId={CorrelationId}",
                "deny", typeof(TRequest).Name, Attribute.Resource, Attribute.Action, decision.ReasonCode, decision.Provider.ProviderId, correlationId);
            if (string.Equals(decision.ReasonCode, AuthorizationDecisionReasonCodes.ProviderUnavailable, StringComparison.Ordinal))
                throw new AuthorizationProviderUnavailableException(Attribute.Resource, Attribute.Action);
            throw new AuthorizationException(Attribute.Resource, Attribute.Action);
        }
        logger.LogDebug(
            "Authorization decision: {Decision} request={RequestType} resource={Resource} action={Action} provider={Provider} correlationId={CorrelationId}",
            "allow", typeof(TRequest).Name, Attribute.Resource, Attribute.Action, decision.Provider.ProviderId, correlationId);
    }
}
