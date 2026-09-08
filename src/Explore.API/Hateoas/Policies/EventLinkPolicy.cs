// ABOUTME: HATEOAS link policies for event detail and collection resources.
// ABOUTME: Emits event navigation, management, registration, and organizer subscription affordances.

namespace Explore.API.Hateoas.Policies;

using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Explore.API.Hateoas;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.EventSession;
using Explore.Application.DTOs.EventSessionGroup;
using Explore.Application.Hateoas;
using Explore.Domain.Enums;
using Explore.Domain.Services.Lifecycle;
using Explore.Domain.Services.Registration;

/// <summary>
/// Link policy for EventDto (detail view).
/// Generates links based on event state and user authorization.
/// </summary>
public sealed class EventDetailLinkPolicy : ILinkPolicy<EventDto>
{
    /// <inheritdoc />
    public IEnumerable<LinkDefinition> GetLinks(EventDto dto, ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.Self,
            dto.IsManagementView ? RouteNames.GetEventManagementDetails : RouteNames.GetEventById,
            new { id = dto.Id },
            "GET",
            "Event details");

        if (dto.IsPubliclyEligible)
        {
            yield return new LinkDefinition(
                LinkRelations.Collection,
                RouteNames.GetEvents,
                null,
                "GET",
                "All events");

            yield return new LinkDefinition(
                LinkRelations.Sessions,
                RouteNames.GetEventSessions,
                new { eventId = dto.Id },
                "GET",
                $"Event sessions ({dto.SessionCount ?? 0})");

            yield return new LinkDefinition(
                LinkRelations.Program,
                RouteNames.GetEventSessionGroupsByEvent,
                new { eventId = dto.Id },
                "GET",
                "Event program");

            yield return new LinkDefinition(
                LinkRelations.ProgramSummary,
                RouteNames.GetEventProgramSummary,
                new { id = dto.Id },
                "GET",
                "Program summary");

            yield return new LinkDefinition(
                LinkRelations.SessionGroups,
                RouteNames.GetEventSessionGroupsByEvent,
                new { eventId = dto.Id },
                "GET",
                "Program sections");
        }

        // Child resources of this event authorize through it: the session or section being created does
        // not exist yet, so the parent event supplies every trusted fact.
        var eventScopedFacts = new EventScopedAuthorizationFacts(dto.TenantId, dto.Id);
        var eventSessionPreCreateFacts = new PreCreateAuthorizationFacts(dto.TenantId, dto.Id);
        var eventAuthorizationScope = new AuthorizationScope(TenantId: dto.TenantId.ToString());

        yield return new LinkDefinition(
            LinkRelations.AddSession,
            RouteNames.CreateDraftEventSession,
            null,
            "POST",
            "Add session",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Create,
                typeof(EventSessionDto),
                dto.Id.ToString(),
                eventAuthorizationScope,
                eventSessionPreCreateFacts);

        yield return new LinkDefinition(
            LinkRelations.CreateSessionDraft,
            RouteNames.CreateDraftEventSession,
            null,
            "POST",
            "Create draft session",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Create,
                typeof(EventSessionDto),
                dto.Id.ToString(),
                eventAuthorizationScope,
                eventSessionPreCreateFacts);

        yield return new LinkDefinition(
            LinkRelations.SessionCreateContext,
            RouteNames.GetEventSessionCreateContext,
            new { id = dto.Id },
            "GET",
            "Program item defaults",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Create,
                typeof(EventSessionDto),
                dto.Id.ToString(),
                eventAuthorizationScope,
                eventSessionPreCreateFacts);

        yield return new LinkDefinition(
            LinkRelations.AddSessionGroup,
            RouteNames.CreateEventSessionGroup,
            null,
            "POST",
            "Add program section",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Create,
                typeof(EventSessionGroupDto),
                dto.Id.ToString(),
                eventAuthorizationScope,
                eventScopedFacts);

        yield return new LinkDefinition(
            LinkRelations.Team,
            RouteNames.GetEventTeam,
            new { eventId = dto.Id },
            "GET",
            "Event team",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Events.ManageTeam, ResourceDescriptors.Event, dto);

        yield return new LinkDefinition(
            LinkRelations.ModerationHistory,
            RouteNames.GetEventModerationHistory,
            new { id = dto.Id },
            "GET",
            "Moderation history",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Events.ViewManagement, ResourceDescriptors.Event, dto);

        yield return new LinkDefinition(
            LinkRelations.ModerationReports,
            RouteNames.GetModerationReportQueue,
            new { eventId = dto.Id },
            "GET",
            "Moderation reports",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Events.ViewManagement, ResourceDescriptors.Event, dto);

        if (dto.IsPubliclyEligible
            && dto.EventStatusId == (int)EventStatusEnum.Published
            && dto.VisibilityTypeId == (int)VisibilityTypeEnum.Public)
        {
            yield return new LinkDefinition(
                LinkRelations.PublicActions,
                RouteNames.GetEventPublicActions,
                new { eventId = dto.Id },
                HttpMethods.Get,
                "Public event actions");

            yield return new LinkDefinition(
                LinkRelations.EventReportOptions,
                RouteNames.GetEventReportOptions,
                new { eventId = dto.Id },
                "GET",
                "Event report options");

            if (dto.IsReportingIntakeEnabled)
            {
                yield return new LinkDefinition(
                    LinkRelations.ReportEvent,
                    RouteNames.SubmitEventReport,
                    null,
                    "POST",
                    "Report event",
                    RequiresAuth: true)
                    .AdvertisedWhenAnonymous();
            }

            yield return new LinkDefinition(
                LinkRelations.SuggestCorrection,
                RouteNames.SubmitEventCorrection,
                null,
                HttpMethods.Post,
                "Suggest a correction",
                RequiresAuth: true)
                .AdvertisedWhenAnonymous();

            yield return new LinkDefinition(
                LinkRelations.ReportExternalLink,
                RouteNames.SubmitUnsafeExternalLinkReport,
                null,
                HttpMethods.Post,
                "Report an unsafe external link",
                RequiresAuth: true)
                .AdvertisedWhenAnonymous();

            yield return new LinkDefinition(
                LinkRelations.ReportLegalOrCopyright,
                RouteNames.SubmitLegalOrCopyrightComplaint,
                null,
                HttpMethods.Post,
                "Report a legal or copyright concern",
                RequiresAuth: true)
                .AdvertisedWhenAnonymous();

            if (!dto.OrganizerActorId.HasValue)
            {
                yield return new LinkDefinition(
                    LinkRelations.ClaimEvent,
                    RouteNames.SubmitEventOrganizerClaim,
                    new { eventId = dto.Id },
                    HttpMethods.Post,
                    "Claim this event",
                    RequiresAuth: true)
                    .RequirePermission(AuthorizationActions.Events.ClaimOrganizer, ResourceDescriptors.EventOrganizerClaimForEvent, dto);
            }

            foreach (var link in GetParticipationLinks(dto, user))
            {
                yield return link;
            }
        }

        yield return new LinkDefinition(
            LinkRelations.ManagePublicActions,
            RouteNames.CreateEventPublicAction,
            new { eventId = dto.Id },
            HttpMethods.Post,
            "Add public action",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Events.ManagePublicActions, ResourceDescriptors.Event, dto);

        yield return new LinkDefinition(
            LinkRelations.ConfigureParticipation,
            RouteNames.ConfigureEventParticipation,
            new { eventId = dto.Id },
            HttpMethods.Patch,
            "Configure participation",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Events.ManageRegistrations, ResourceDescriptors.Event, dto);

        if (dto.IsManagementView)
        {
            yield return new LinkDefinition(
                LinkRelations.ManageRegistrationWorkflow,
                RouteNames.GetRegistrationWorkflow,
                new { eventId = dto.Id, purpose = "registration" },
                HttpMethods.Get,
                "Manage registration workflow",
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.Events.ManageRegistrationWorkflow, ResourceDescriptors.Event, dto);

            yield return new LinkDefinition(
                LinkRelations.ViewRegistrationProviderHealth,
                RouteNames.GetRegistrationProviderHealth,
                new { eventId = dto.Id, tenantId = dto.TenantId },
                HttpMethods.Get,
                "Registration provider health",
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.Events.ViewRegistrationProviderHealth, ResourceDescriptors.Event, dto);

            yield return new LinkDefinition(
                LinkRelations.ManageRegistrationChannels,
                RouteNames.GetRegistrationProviderQueue,
                new { eventId = dto.Id, tenantId = dto.TenantId },
                HttpMethods.Get,
                "Manage registration channels",
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.Events.ManageRegistrationChannels, ResourceDescriptors.Event, dto);
        }

        if (dto.ParticipationConfiguration?.ParticipationHandlingModeId == (int)ParticipationHandlingModeEnum.PlatformManaged)
        {
            yield return new LinkDefinition(LinkRelations.ManageTicketTypes, RouteNames.GetEventTicketCatalogManagement, new { eventId = dto.Id }, HttpMethods.Get, "Manage ticket types", RequiresAuth: true)
                .RequirePermission(AuthorizationActions.Events.ManageTickets, ResourceDescriptors.Event, dto);
            yield return new LinkDefinition(LinkRelations.ManageCapacityPools, RouteNames.GetEventTicketCatalogManagement, new { eventId = dto.Id }, HttpMethods.Get, "Manage capacity pools", RequiresAuth: true)
                .RequirePermission(AuthorizationActions.Events.ManageTickets, ResourceDescriptors.Event, dto);
            yield return new LinkDefinition(
                    LinkRelations.IssueScannerCapability,
                    RouteNames.IssueAdmissionScannerCapability,
                    new { eventId = dto.Id },
                    HttpMethods.Post,
                    "Issue admission scanner capability",
                    RequiresAuth: true)
                .RequirePermission(AuthorizationActions.Events.ManageTickets, ResourceDescriptors.Event, dto);
            yield return new LinkDefinition(
                    LinkRelations.CheckInAdmissions,
                    RouteNames.CheckInAdmission,
                    new { eventId = dto.Id },
                    HttpMethods.Post,
                    "Check in admissions",
                    RequiresAuth: true)
                .RequirePermission(AuthorizationActions.Events.EventCheckInManage, ResourceDescriptors.Event, dto);
            yield return new LinkDefinition(
                    LinkRelations.AdmissionCheckInSummary,
                    RouteNames.GetAdmissionCheckInSummary,
                    new { eventId = dto.Id },
                    HttpMethods.Get,
                    "Admission check-in summary",
                    RequiresAuth: true)
                .RequirePermission(AuthorizationActions.Events.EventCheckInView, ResourceDescriptors.Event, dto);
            yield return new LinkDefinition(
                    LinkRelations.AdmissionCheckInAudit,
                    RouteNames.GetAdmissionCheckInAudit,
                    new { eventId = dto.Id, pageSize = 100 },
                    HttpMethods.Get,
                    "Admission check-in audit",
                    RequiresAuth: true)
                .RequirePermission(AuthorizationActions.Events.EventCheckInView, ResourceDescriptors.Event, dto);
            yield return new LinkDefinition(
                    LinkRelations.AdmissionCheckInHealth,
                    RouteNames.GetAdmissionCheckInHealth,
                    new { eventId = dto.Id },
                    HttpMethods.Get,
                    "Admission check-in health",
                    RequiresAuth: true)
                .RequirePermission(AuthorizationActions.Events.EventCheckInView, ResourceDescriptors.Event, dto);
            yield return new LinkDefinition(
                    LinkRelations.StopAdmissionCheckIn,
                    RouteNames.StopAdmissionCheckIn,
                    new { eventId = dto.Id },
                    HttpMethods.Post,
                    "Stop admission check-in",
                    RequiresAuth: true)
                .RequirePermission(AuthorizationActions.Events.EventCheckInManage, ResourceDescriptors.Event, dto);
            yield return new LinkDefinition(
                    LinkRelations.RestoreAdmissionCheckIn,
                    RouteNames.RestoreAdmissionCheckIn,
                    new { eventId = dto.Id },
                    HttpMethods.Post,
                    "Restore admission check-in",
                    RequiresAuth: true)
                .RequirePermission(AuthorizationActions.Events.EventCheckInManage, ResourceDescriptors.Event, dto);
            yield return new LinkDefinition(
                    LinkRelations.ReconcileAdmissionCheckIn,
                    RouteNames.ReconcileAdmissionCheckIn,
                    new { eventId = dto.Id },
                    HttpMethods.Post,
                    "Reconcile admission check-in",
                    RequiresAuth: true)
                .RequirePermission(AuthorizationActions.Events.EventCheckInManage, ResourceDescriptors.Event, dto);

            if (dto.IsManagementView)
            {
                yield return new LinkDefinition(
                    LinkRelations.ViewRegistrationOrders,
                    RouteNames.GetEventRegistrationOrders,
                    new { eventId = dto.Id },
                    HttpMethods.Get,
                    "View registration orders",
                    RequiresAuth: true)
                    .RequirePermission(AuthorizationActions.Events.ManageRegistrations, ResourceDescriptors.Event, dto);

                yield return new LinkDefinition(
                    LinkRelations.RefundCampaigns,
                    RouteNames.GetRefundCampaigns,
                    new { eventId = dto.Id },
                    HttpMethods.Get,
                    "Refund campaigns",
                    RequiresAuth: true)
                    .RequirePermission(AuthorizationActions.Events.ManagePaidEventCommerce, ResourceDescriptors.Event, dto);

                yield return new LinkDefinition(
                    LinkRelations.ViewParticipants,
                    RouteNames.GetEventRegistrationOrders,
                    new { eventId = dto.Id },
                    HttpMethods.Get,
                    "View participants",
                    RequiresAuth: true)
                    .RequirePermission(AuthorizationActions.Events.ManageRegistrations, ResourceDescriptors.Event, dto);

                yield return new LinkDefinition(
                    LinkRelations.ViewRegistrationAnalytics,
                    RouteNames.GetRegistrationAnswerAnalytics,
                    new { eventId = dto.Id },
                    HttpMethods.Get,
                    "View registration analytics",
                    RequiresAuth: true)
                    .RequirePermission(AuthorizationActions.Events.ManageRegistrations, ResourceDescriptors.Event, dto);

                if (dto.OrganizerActorId is Guid organizerActorId
                    && dto.OrganizerActorOrganizationId is Guid organizerOrganizationId)
                {
                    yield return new LinkDefinition(
                        LinkRelations.ExportAttendees,
                        RouteNames.ExportOrganizationSharedContacts,
                        new { recipientActorId = organizerActorId, eventId = dto.Id, format = "csv" },
                        HttpMethods.Post,
                        "Export consented attendee contacts",
                        RequiresAuth: true)
                        .RequirePermission(
                            AuthorizationActions.ExportSharedContacts,
                            ResourceKinds.EventContactShareConsent,
                            organizerOrganizationId.ToString(),
                            eventAuthorizationScope,
                            new ContactShareAuthorizationFacts(dto.TenantId, organizerOrganizationId));
                }
            }
        }

        if (dto.IsPubliclyEligible)
        {
            yield return new LinkDefinition(
                LinkRelations.OrganizerClaims,
                RouteNames.GetEventOrganizerClaims,
                new { eventId = dto.Id },
                HttpMethods.Get,
                "Organizer claims",
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.Events.ViewOrganizerClaims, ResourceDescriptors.EventOrganizerClaimForEvent, dto);
        }

        if (dto.AvailableAspects?.Contains("Islamic") == true || dto.IslamicAspect != null)
        {
            if (dto.IsPubliclyEligible)
            {
                yield return new LinkDefinition(
                    "islamic-aspect",
                    RouteNames.GetEventIslamicAspect,
                    new { id = dto.Id },
                    "GET",
                    "Islamic aspect details");
            }

            yield return new LinkDefinition(
                "islamic-aspect:edit",
                RouteNames.UpdateEventIslamicAspect,
                new { id = dto.Id },
                HttpMethods.Patch,
                "Edit Islamic aspect",
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.Update, ResourceDescriptors.Event, dto);
        }
        else
        {
            yield return new LinkDefinition(
                "islamic-aspect:create",
                RouteNames.CreateEventIslamicAspect,
                new { id = dto.Id },
                HttpMethods.Post,
                "Add Islamic aspect",
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.Update, ResourceDescriptors.Event, dto);
        }

        if (dto.AvailableAspects?.Contains("Tech") == true || dto.TechAspect != null)
        {
            if (dto.IsPubliclyEligible)
            {
                yield return new LinkDefinition(
                    "tech-aspect",
                    RouteNames.GetEventTechAspect,
                    new { id = dto.Id },
                    "GET",
                    "Tech aspect details");
            }

            yield return new LinkDefinition(
                "tech-aspect:edit",
                RouteNames.UpdateEventTechAspect,
                new { id = dto.Id },
                HttpMethods.Patch,
                "Edit Tech aspect",
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.Update, ResourceDescriptors.Event, dto);
        }
        else
        {
            yield return new LinkDefinition(
                "tech-aspect:create",
                RouteNames.CreateEventTechAspect,
                new { id = dto.Id },
                HttpMethods.Post,
                "Add Tech aspect",
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.Update, ResourceDescriptors.Event, dto);
        }

        if (dto.IsPubliclyEligible)
        {
            yield return new LinkDefinition(
                "actor",
                RouteNames.GetActorById,
                new { id = dto.ActorId },
                "GET",
                dto.ActorDisplayName);

            if (CanSubscribeToOrganizer(dto.ActorTypeId))
            {
                yield return new LinkDefinition(
                    "organizer-subscription",
                    RouteNames.GetActorSubscriptionByActor,
                    new { targetActorId = dto.ActorId },
                    "GET",
                    "My subscription to this organizer",
                    RequiresAuth: true)
                    .RequirePermission(AuthorizationActions.ActorSubscriptions.View,
                        ResourceKinds.ActorSubscription,
                        dto.ActorId.ToString(),
                        facts: new PersonalResourceAuthorizationFacts(dto.TenantId));

                yield return new LinkDefinition(
                    "subscribe-organizer",
                    RouteNames.SubscribeToActor,
                    null,
                    "POST",
                    "Subscribe to this organizer",
                    RequiresAuth: true)
                    .RequirePermission(AuthorizationActions.ActorSubscriptions.Create,
                        ResourceKinds.ActorSubscription,
                        dto.ActorId.ToString(),
                        facts: new PersonalResourceAuthorizationFacts(dto.TenantId));
            }
        }

        // Edit link - requires authentication
        yield return new LinkDefinition(
            LinkRelations.Edit,
            RouteNames.UpdateEvent,
            new { id = dto.Id },
            "PATCH",
            "Update event",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Update, ResourceDescriptors.Event, dto);

        var eventStatus = (EventStatusEnum)dto.EventStatusId;
        var canPublish = eventStatus != EventStatusEnum.Published
            && EventLifecycleRules.CanTransition(eventStatus, EventStatusEnum.Published);

        if (canPublish)
        {
            yield return new LinkDefinition(
                LinkRelations.PublishReadiness,
                RouteNames.GetEventPublishReadiness,
                new { id = dto.Id },
                "GET",
                "Review publish readiness",
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.Update, ResourceDescriptors.Event, dto);

            if (dto.ParticipationConfiguration?.IdentityAccessModeId != (int)IdentityAccessModeEnum.AccountRequired
                || dto.VisitorAccess is { AllowsAccountRequiredParticipation: true })
            {
                yield return new LinkDefinition(
                    LinkRelations.Publish,
                    RouteNames.PublishEvent,
                    new { id = dto.Id },
                    "POST",
                    "Publish event",
                    RequiresAuth: true)
                    .RequirePermission(AuthorizationActions.Update, ResourceDescriptors.Event, dto);
            }
        }

        if (eventStatus != EventStatusEnum.Cancelled
            && EventLifecycleRules.CanTransition(eventStatus, EventStatusEnum.Cancelled))
        {
            yield return CreateExplicitLifecycleLink(LinkRelations.Cancel, dto, "Cancel event", RouteNames.CancelEvent);
        }

        if (eventStatus != EventStatusEnum.Archived
            && EventLifecycleRules.CanTransition(eventStatus, EventStatusEnum.Archived))
        {
            yield return CreateExplicitLifecycleLink(LinkRelations.Archive, dto, "Archive event", RouteNames.ArchiveEvent);
        }

        if (eventStatus != EventStatusEnum.Moderated
            && EventLifecycleRules.CanTransition(eventStatus, EventStatusEnum.Moderated))
        {
            yield return new LinkDefinition(
                LinkRelations.ModerateLight,
                RouteNames.ModerateEventLight,
                new { id = dto.Id },
                "POST",
                "Light moderate event",
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.Events.ModerateLight, ResourceDescriptors.Event, dto);
        }

        if (CanAdvertiseHeavyModeration(dto))
        {
            yield return new LinkDefinition(
                LinkRelations.ModerateHeavy,
                RouteNames.ModerateEventHeavy,
                new { id = dto.Id },
                "POST",
                "Heavy redact event",
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.Events.ModerateHeavy, ResourceDescriptors.Event, dto);
        }

        if (EventLifecycleRules.CanRestoreAfterLightModeration(eventStatus) && dto.IsUnmoderationEligible)
        {
            yield return new LinkDefinition(
                LinkRelations.Unmoderate,
                RouteNames.UnmoderateEvent,
                new { id = dto.Id },
                "POST",
                "Unmoderate event",
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.Events.Unmoderate, ResourceDescriptors.Event, dto);
        }

        // Delete link - requires authentication
        yield return new LinkDefinition(
            "delete",
            RouteNames.DeleteEvent,
            new { id = dto.Id },
            "DELETE",
            "Delete event",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Delete, ResourceDescriptors.Event, dto);
    }

    private static IEnumerable<LinkDefinition> GetParticipationLinks(EventDto dto, ClaimsPrincipal? user)
    {
        var participationModeId = dto.ParticipationConfiguration?.ParticipationHandlingModeId;
        if (!participationModeId.HasValue || !Enum.IsDefined(typeof(ParticipationHandlingModeEnum), participationModeId.Value))
        {
            yield break;
        }

        switch ((ParticipationHandlingModeEnum)participationModeId.Value)
        {
            case ParticipationHandlingModeEnum.InformationOnly:
                yield break;
            case ParticipationHandlingModeEnum.WalkIn:
                if (dto.ParticipationConfiguration!.HasValidOptionalQuestionnaire)
                {
                    yield return new LinkDefinition(
                        LinkRelations.OptionalQuestionnaire,
                        RouteNames.GetOptionalQuestionnaire,
                        new { eventId = dto.Id },
                        HttpMethods.Get,
                        "Optional questionnaire");
                }

                yield break;
            case ParticipationHandlingModeEnum.ExternalManaged:
                foreach (var link in GetExternalRegistrationLinks(dto))
                {
                    yield return link;
                }

                yield break;
            case ParticipationHandlingModeEnum.PlatformManaged:
                if (dto.VisitorAccess is not { AllowsNewNativeAllocation: true }
                    || dto.ParticipationConfiguration!.IdentityAccessModeId == (int)IdentityAccessModeEnum.AccountRequired
                        && !dto.VisitorAccess.AllowsAccountRequiredParticipation)
                {
                    yield break;
                }

                var isAuthenticated = user?.Identity?.IsAuthenticated == true;
                if (isAuthenticated)
                {
                    yield return new LinkDefinition(
                        LinkRelations.StartRegistration,
                        RouteNames.StartAuthenticatedRegistrationOrder,
                        new { eventId = dto.Id },
                        HttpMethods.Post,
                        "Start registration",
                        RequiresAuth: true)
                    .RequirePermission(
                        AuthorizationActions.Create,
                        ResourceKinds.RegistrationOrder,
                        dto.Id.ToString(), scope: ResourceDescriptors.Event.GetScope(dto),
                        facts: ResourceDescriptors.Event.GetFacts(dto));
                }
                else if (dto.ParticipationConfiguration?.IdentityAccessModeId is
                    (int)IdentityAccessModeEnum.GuestAllowed or (int)IdentityAccessModeEnum.CapabilityTokenAllowed)
                {
                    yield return new LinkDefinition(
                        LinkRelations.StartGuestRegistration,
                        RouteNames.StartGuestRegistrationOrder,
                        new { eventId = dto.Id },
                        HttpMethods.Post,
                        "Start guest registration");
                }
                else
                {

                    yield return new LinkDefinition(
                        LinkRelations.SignInToRegister,
                        RouteNames.StartAuthenticatedRegistrationOrder,
                        new { eventId = dto.Id },
                        HttpMethods.Post,
                        "Sign in to register",
                        RequiresAuth: true)
                        .AdvertisedWhenAnonymous();
                }

                yield break;
        }
    }

    private static IEnumerable<LinkDefinition> GetExternalRegistrationLinks(EventDto dto)
    {
        var action = dto.PublicActions
            .Where(action => action.KindId == (int)EventPublicActionKindEnum.ExternalRegistration)
            .OrderByDescending(action => action.IsPrimary)
            .ThenBy(action => action.SortOrder)
            .ThenBy(action => action.Id)
            .FirstOrDefault();

        if (action is null)
        {
            yield break;
        }

        yield return new LinkDefinition(
            LinkRelations.ExternalRegistration,
            RouteNames.RedirectEventPublicAction,
            new { eventId = dto.Id, actionId = action.Id, surface = EventDetailSurface },
            HttpMethods.Get,
            GetExternalRegistrationLabel(dto));
    }

    private static string GetExternalRegistrationLabel(EventDto dto)
    {
        var authority = EventAuthorityRules.Resolve(dto.ProvenanceTypeId, dto.ActorId, dto.OrganizerActorId);
        return authority.HasParticipationManagementAuthority
                ? "Register on organizer website"
                : "View original event page";
    }

    private const string EventDetailSurface = "event_detail";

    private static bool CanSubscribeToOrganizer(int actorTypeId) => actorTypeId is (int)ActorTypeEnum.Organization or (int)ActorTypeEnum.Group;

    private static bool CanAdvertiseHeavyModeration(EventDto dto) =>
        dto.EventStatusId != (int)EventStatusEnum.Moderated || dto.IsUnmoderationEligible;

    private static LinkDefinition CreateExplicitLifecycleLink(string relation, EventDto dto, string title, string routeName) =>
        new LinkDefinition(
            relation,
            routeName,
            new { id = dto.Id },
            "POST",
            title,
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Update, ResourceDescriptors.Event, dto);

}

/// <summary>
/// Link policy for EventListDto (collection items).
/// </summary>
public sealed class EventCollectionLinkPolicy : ICollectionLinkPolicy<EventListDto>
{
    /// <inheritdoc />
    public IEnumerable<LinkDefinition> GetItemLinks(EventListDto dto, ClaimsPrincipal? user)
    {
        // Self link for item
        yield return new LinkDefinition(
            LinkRelations.Self,
            dto.IsManagementView ? RouteNames.GetEventManagementDetails : RouteNames.GetEventById,
            new { id = dto.Id },
            "GET",
            dto.Title);

        // Sessions link
        if (dto.SessionCount.HasValue && dto.SessionCount.Value > 0)
        {
            yield return new LinkDefinition(
                "sessions",
                dto.IsManagementView ? RouteNames.GetManagedEventSessionsByEvent : RouteNames.GetEventSessions,
                new { eventId = dto.Id },
                "GET",
                $"{dto.SessionCount} sessions");
        }


        // Actor link
        yield return new LinkDefinition(
            "actor",
            RouteNames.GetActorById,
            new { id = dto.ActorId },
            "GET",
            dto.ActorDisplayName);

        if (!dto.IsManagementView
            && dto.EventStatusId == (int)EventStatusEnum.Published
            && dto.VisibilityTypeId == (int)VisibilityTypeEnum.Public)
        {
            yield return new LinkDefinition(
                LinkRelations.EventReportOptions,
                RouteNames.GetEventReportOptions,
                new { eventId = dto.Id },
                "GET",
                "Event report options");

            if (dto.IsReportingIntakeEnabled)
            {
                yield return new LinkDefinition(
                    LinkRelations.ReportEvent,
                    RouteNames.SubmitEventReport,
                    null,
                    "POST",
                    "Report event",
                    RequiresAuth: true)
                    .AdvertisedWhenAnonymous();
            }

            yield return new LinkDefinition(
                LinkRelations.SuggestCorrection,
                RouteNames.SubmitEventCorrection,
                null,
                HttpMethods.Post,
                "Suggest a correction",
                RequiresAuth: true)
                .AdvertisedWhenAnonymous();

            yield return new LinkDefinition(
                LinkRelations.ReportExternalLink,
                RouteNames.SubmitUnsafeExternalLinkReport,
                null,
                HttpMethods.Post,
                "Report an unsafe external link",
                RequiresAuth: true)
                .AdvertisedWhenAnonymous();

            yield return new LinkDefinition(
                LinkRelations.ReportLegalOrCopyright,
                RouteNames.SubmitLegalOrCopyrightComplaint,
                null,
                HttpMethods.Post,
                "Report a legal or copyright concern",
                RequiresAuth: true)
                .AdvertisedWhenAnonymous();
        }

        // Edit link - requires authentication and permission
        yield return new LinkDefinition(
            LinkRelations.Edit,
            RouteNames.UpdateEvent,
            new { id = dto.Id },
            "PATCH",
            "Update event",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Update, ResourceDescriptors.EventList, dto);

        // Delete link - requires authentication and permission
        yield return new LinkDefinition(
            "delete",
            RouteNames.DeleteEvent,
            new { id = dto.Id },
            "DELETE",
            "Delete event",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Delete, ResourceDescriptors.EventList, dto);
    }

    /// <inheritdoc />
    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user)
    {
        // Create link - requires authentication
        yield return new LinkDefinition(
            "create",
            RouteNames.CreateEvent,
            null,
            "POST",
            "Create new event",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Create, typeof(EventDto), "event");

        // My events link - requires authentication
        yield return new LinkDefinition(
            "my-events",
            RouteNames.GetMyEvents,
            null,
            "GET",
            "My events",
            RequiresAuth: true);
    }
}
