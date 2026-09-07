using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventTicketing;
using Explore.Application.Services.Registration;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;
using Explore.Domain.ValueObjects;

namespace Explore.Application.Features.EventTicketing.Services;

public sealed class PaidEventPublicationPreflightService(
    IEventRepository events,
    IEventTicketCatalogRepository catalogs,
    IPaidEventPolicyRepository policies,
    IOrganizerPaymentProviderConnectionRepository connections,
    IOrganizationTenantRepository tenantOrganizations,
    IGroupTenantRepository tenantGroups,
    IAuthorizationProvider authorization,
    ITenantContext tenant,
    IOrganizerPaymentCommerceConfiguration commerceConfiguration,
    ITenantDirectoryOperatorReadinessEvaluator directoryOperatorReadiness,
    IPaidCheckoutActivationService checkoutActivation)
{
    private readonly ITenantDirectoryOperatorReadinessEvaluator _directoryOperatorReadiness =
        directoryOperatorReadiness;

    public async Task<PaidEventPublicationPreflightDto> AssessAsync(Guid eventId, CancellationToken cancellationToken)
    {
        Event? eventTarget = await events.GetEventWithDetails(eventId);
        EventTicketCatalogVersion? draft = await catalogs.GetDraftCatalogForUpdateAsync(eventId, tenant.TenantId, cancellationToken);
        return await AssessAsync(eventId, eventTarget, draft, cancellationToken);
    }

    public async Task<PaidEventPublicationPreflightDto> AssessAsync(
        Guid eventId,
        Event? eventTarget,
        EventTicketCatalogVersion? draft,
        CancellationToken cancellationToken)
    {
        if (eventTarget is null || eventTarget.TenantId != tenant.TenantId || draft is null)
        {
            return Result(eventId, draft?.Id, isPaid: false, [Block("ticketing_not_found", "Ticketing configuration was not found.")]);
        }

        bool isPaid = IsPaid(draft);
        if (!isPaid)
        {
            return Result(eventTarget, draft.Id, isPaid, []);
        }

        var blockers = new List<PaidEventPublicationPreflightBlockerDto>();
        TenantDirectoryOperatorReadinessAssessment directoryIdentity =
            await _directoryOperatorReadiness.EvaluateAsync(
                eventTarget.TenantId,
                TenantDirectoryOperatorIdentityCapability.PaidCommerce,
                cancellationToken);
        if (!directoryIdentity.IsReady)
        {
            blockers.Add(Block(
                "tenant_directory_operator_identity_unavailable",
                "Tenant directory operator identity is unavailable for paid commerce."));
            return Result(eventTarget, draft.Id, isPaid, blockers);
        }

        PaidCheckoutActivationResult saleControl = await checkoutActivation.EvaluateSaleControlAsync(
            eventTarget.TenantId, eventTarget.Id, cancellationToken);
        if (!saleControl.IsActive)
        {
            blockers.Add(Block(saleControl.FailureCode ?? "checkout_governance_unavailable", saleControl.Message));
            return Result(eventTarget, draft.Id, isPaid, blockers);
        }

        PaidEventPolicyVersion? instancePolicy = await policies.GetActiveInstanceAsync(cancellationToken);
        PaidEventPolicyVersion? tenantPolicy = await policies.GetActiveTenantAsync(tenant.TenantId, cancellationToken);
        if (!TryValidatePolicy(instancePolicy, tenantPolicy, blockers))
        {
            return Result(eventTarget, draft.Id, isPaid, blockers);
        }
        PaidEventPolicyVersion effectivePolicy = tenantPolicy ?? instancePolicy!;

        Actor? organizer = eventTarget.OrganizerActor;
        if (eventTarget.OrganizerActorId is null || organizer is null)
        {
            blockers.Add(Block("organizer_missing", "Paid publication requires one persisted organizer actor."));
            return Result(eventTarget, draft.Id, isPaid, blockers);
        }

        if (!effectivePolicy.AllowedOrganizerKinds.Contains((ActorTypeEnum)organizer.ActorTypeId))
        {
            blockers.Add(Block("organizer_kind_not_allowed", "The organizer actor kind is not allowed for paid events."));
        }

        if (effectivePolicy.RequiresLocalVerification && !await IsLocallyVerifiedAsync(organizer, eventTarget.TenantId, cancellationToken))
        {
            blockers.Add(Block("organizer_verification_required", "The organizer must be locally verified before paid publication."));
        }

        string? confirmedCurrency = PaidEventPolicyRules.ResolveConfirmedCatalogCurrency(instancePolicy, tenantPolicy, null, draft.CurrencyCode);
        if (confirmedCurrency is null)
        {
            blockers.Add(Block("currency_not_allowed", "The catalog currency must be explicitly allowed by the effective paid-event policy."));
        }

        if (MissingDisclosures(draft))
        {
            blockers.Add(Block("commercial_disclosures_missing", "Paid publication requires merchant, refund, and support disclosures."));
        }

        if (string.IsNullOrWhiteSpace(commerceConfiguration.ProviderCode) || string.IsNullOrWhiteSpace(commerceConfiguration.ConnectPlatformId))
        {
            blockers.Add(Block("payment_platform_not_configured", "The payment platform is not configured for paid publication."));
            return Result(eventTarget, draft.Id, isPaid, blockers);
        }

        OrganizerPaymentProviderConnection? connection = await connections.GetActiveByScopeAsync(
            eventTarget.TenantId,
            eventTarget.OrganizerActorId.Value,
            commerceConfiguration.ProviderCode,
            commerceConfiguration.ConnectPlatformId,
            cancellationToken);
        if (connection is null)
        {
            blockers.Add(Block("organizer_connection_missing", "The organizer needs an active payment connection for this platform."));
        }
        else
        {
            AddConnectionBlockers(connection, confirmedCurrency, blockers);
        }

        var commerceDecision = await authorization.AuthorizeAsync(
            new AuthorizationRequest(
                AuthorizationCapabilityCatalog.Require(ResourceKinds.Event, AuthorizationActions.Events.ManagePaidEventCommerce),
                eventTarget.Id.ToString(),
                Scope: ResourceDescriptors.EventAuthorizationTarget.GetScope(eventTarget),
                Facts: ResourceDescriptors.EventAuthorizationTarget.GetFacts(eventTarget)),
            cancellationToken);
        if (!commerceDecision.IsAllowed)
        {
            blockers.Add(Block("commerce_authorization_denied", "The current principal is not authorized to manage paid commerce for this exact organizer."));
        }

        return Result(eventTarget, draft.Id, isPaid, blockers);
    }

    private static bool TryValidatePolicy(PaidEventPolicyVersion? instancePolicy, PaidEventPolicyVersion? tenantPolicy, List<PaidEventPublicationPreflightBlockerDto> blockers)
    {
        if (instancePolicy is null || !instancePolicy.IsActive || !instancePolicy.IsPaymentsEnabled || instancePolicy.TenantId is not null)
        {
            blockers.Add(Block("paid_event_policy_unavailable", "Paid events are not enabled by an active instance and tenant policy."));
            return false;
        }

        if (tenantPolicy is null)
        {
            return true;
        }

        if (!tenantPolicy.IsActive || !tenantPolicy.IsPaymentsEnabled || tenantPolicy.TenantId is null)
        {
            blockers.Add(Block("paid_event_policy_unavailable", "Paid events are not enabled by an active instance and tenant policy."));
            return false;
        }

        try
        {
            PaidEventPolicyRules.ValidateTenantPolicy(instancePolicy, tenantPolicy);
            return true;
        }
        catch (ArgumentException)
        {
            blockers.Add(Block("paid_event_policy_invalid", "The effective paid-event policy is invalid."));
            return false;
        }
        catch (InvalidOperationException)
        {
            blockers.Add(Block("paid_event_policy_invalid", "The effective paid-event policy is invalid."));
            return false;
        }
    }

    private async Task<bool> IsLocallyVerifiedAsync(Actor organizer, Guid tenantId, CancellationToken cancellationToken)
    {
        if (organizer.UserId is not null)
        {
            return true;
        }

        if (organizer.OrganizationId is { } organizationId)
        {
            OrganizationTenant? organizationTenant = await tenantOrganizations.GetByOrganizationAndTenant(organizationId, tenantId, cancellationToken);
            return organizationTenant is { IsDeleted: false, IsSuspended: false, IsOrganizerEligible: true, ApprovedAt: not null };
        }

        if (organizer.GroupId is { } groupId)
        {
            GroupTenant? groupTenant = await tenantGroups.GetByGroupAndTenant(groupId, tenantId, cancellationToken);
            return groupTenant is { IsDeleted: false, IsSuspended: false, IsOrganizerEligible: true, ApprovedAt: not null };
        }

        return false;
    }

    private static void AddConnectionBlockers(OrganizerPaymentProviderConnection connection, string? confirmedCurrency, List<PaidEventPublicationPreflightBlockerDto> blockers)
    {
        if (connection.StatusId != (int)OrganizerPaymentProviderConnectionStatusEnum.Ready)
        {
            blockers.Add(Block("organizer_connection_not_ready", "The organizer payment connection is not ready."));
        }

        if (connection.ChargeCapabilityStateId != (int)ChargeCapabilityState.Active)
        {
            blockers.Add(Block("charge_capability_inactive", "The organizer payment connection cannot accept charges."));
        }

        if (connection.RequirementsStateId != (int)ProviderRequirementsState.Satisfied)
        {
            blockers.Add(Block("provider_requirements_pending", "The organizer payment connection still has provider requirements."));
        }

        if (confirmedCurrency is not null && !connection.SupportedCurrencyCodes.Contains(confirmedCurrency, StringComparer.Ordinal))
        {
            blockers.Add(Block("connection_currency_unsupported", "The organizer payment connection does not support the catalog currency."));
        }
    }

    private static bool IsPaid(EventTicketCatalogVersion catalog) =>
        catalog.TicketTypes.Any(ticketType => !ticketType.IsDeleted && ticketType.TicketPricingModeId != (int)TicketPricingModeEnum.Free);

    private static bool MissingDisclosures(EventTicketCatalogVersion catalog) =>
        string.IsNullOrWhiteSpace(catalog.MerchantDisclosureText)
        || string.IsNullOrWhiteSpace(catalog.RefundPolicyDisclosureText)
        || string.IsNullOrWhiteSpace(catalog.SupportContactDisclosureText);

    private static PaidEventPublicationPreflightDto Result(Guid eventId, Guid? catalogId, bool isPaid, IReadOnlyList<PaidEventPublicationPreflightBlockerDto> blockers) => new()
    {
        EventId = eventId,
        CatalogId = catalogId,
        IsPaidCatalog = isPaid,
        IsReady = blockers.Count == 0,
        Blockers = blockers
    };

    private static PaidEventPublicationPreflightDto Result(Event eventTarget, Guid? catalogId, bool isPaid, IReadOnlyList<PaidEventPublicationPreflightBlockerDto> blockers) => new()
    {
        EventId = eventTarget.Id,
        CatalogId = catalogId,
        IsPaidCatalog = isPaid,
        IsReady = blockers.Count == 0,
        Blockers = blockers,
        TenantId = eventTarget.TenantId,
        ActorId = eventTarget.ActorId,
        ActorUserId = eventTarget.Actor?.UserId,
        ActorOrganizationId = eventTarget.Actor?.OrganizationId,
        ActorGroupId = eventTarget.Actor?.GroupId,
        OrganizerActorId = eventTarget.OrganizerActorId,
        OrganizerUserId = eventTarget.OrganizerActor?.UserId,
        OrganizerOrganizationId = eventTarget.OrganizerActor?.OrganizationId,
        OrganizerGroupId = eventTarget.OrganizerActor?.GroupId
    };

    private static PaidEventPublicationPreflightBlockerDto Block(string code, string explanation) => new()
    {
        Code = code,
        Explanation = explanation
    };
}
