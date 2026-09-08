using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Settings;
using Explore.Domain.Constants;
using Explore.Domain.Enums;

namespace Explore.Application.Features.Geocoding;

public sealed class AddressGovernancePolicyResolver(
    IHierarchicalSettingsResolver settingsResolver,
    IAuthorizationProvider authorizationProvider)
    : IAddressGovernancePolicyResolver
{
    private const string GovernanceResourceId = "address-governance";

    public async Task<AddressGovernancePolicyDecision> ResolveAsync(
        AddressGovernancePolicyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var settingContext = new SettingContext(
            TenantId: request.TenantId,
            OrganizationId: request.OrganizationId,
            UserId: null);
        string? storedMode = await settingsResolver.ResolveAsync<string>(
            GovernanceSettingKeys.AddressGovernance.CreationMode,
            settingContext,
            cancellationToken);

        AddressCreationMode mode = ParseMode(storedMode);
        if (mode == AddressCreationMode.Disabled || !HasTrustedContext(request))
        {
            return AddressGovernancePolicyDecision.Denied(mode);
        }

        return mode switch
        {
            AddressCreationMode.AdminOnly => await ResolveAdminOnlyAsync(request, cancellationToken),
            AddressCreationMode.OrganizationGoverned => await ResolveOrganizationGovernedAsync(
                request,
                settingContext,
                cancellationToken),
            AddressCreationMode.OpenWithModeration => await ResolveOpenWithModerationAsync(
                request,
                cancellationToken),
            _ => AddressGovernancePolicyDecision.Denied(AddressCreationMode.Disabled)
        };
    }

    private async Task<AddressGovernancePolicyDecision> ResolveAdminOnlyAsync(
        AddressGovernancePolicyRequest request,
        CancellationToken cancellationToken)
    {
        AuthorizationDecision management = await AuthorizeLocationAsync(
            request,
            AuthorizationActions.Locations.ManageCustomAddresses,
            cancellationToken);
        if (!management.IsAllowed)
        {
            return AddressGovernancePolicyDecision.Denied(AddressCreationMode.AdminOnly);
        }

        AuthorizationDecision approval = await AuthorizeLocationAsync(
            request,
            AuthorizationActions.Locations.ApproveTenantAddress,
            cancellationToken);
        return AddressGovernancePolicyDecision.Allowed(
            AddressCreationMode.AdminOnly,
            approval.IsAllowed
                ? LocationAddressVisibilityEnum.TenantApproved
                : LocationAddressVisibilityEnum.CreatorPrivate);
    }

    private async Task<AddressGovernancePolicyDecision> ResolveOrganizationGovernedAsync(
        AddressGovernancePolicyRequest request,
        SettingContext settingContext,
        CancellationToken cancellationToken)
    {
        if (!HasOrganization(request))
        {
            return AddressGovernancePolicyDecision.Denied(AddressCreationMode.OrganizationGoverned);
        }

        bool granted = await settingsResolver.ResolveAsync<bool>(
            GovernanceSettingKeys.AddressGovernance.OrganizationCreationGrant,
            settingContext,
            cancellationToken);
        if (!granted)
        {
            return AddressGovernancePolicyDecision.Denied(AddressCreationMode.OrganizationGoverned);
        }

        AuthorizationDecision authorization = await AuthorizeOrganizationAsync(request, cancellationToken);
        return authorization.IsAllowed
            ? AddressGovernancePolicyDecision.Allowed(
                AddressCreationMode.OrganizationGoverned,
                LocationAddressVisibilityEnum.OrganizationScoped,
                request.OrganizationId)
            : AddressGovernancePolicyDecision.Denied(AddressCreationMode.OrganizationGoverned);
    }

    private async Task<AddressGovernancePolicyDecision> ResolveOpenWithModerationAsync(
        AddressGovernancePolicyRequest request,
        CancellationToken cancellationToken)
    {
        AuthorizationDecision creation = await AuthorizeLocationAsync(
            request,
            AuthorizationActions.Locations.Create,
            cancellationToken);
        if (!creation.IsAllowed)
        {
            return AddressGovernancePolicyDecision.Denied(AddressCreationMode.OpenWithModeration);
        }

        if (HasOrganization(request))
        {
            AuthorizationDecision organization = await AuthorizeOrganizationAsync(request, cancellationToken);
            if (organization.IsAllowed)
            {
                return AddressGovernancePolicyDecision.Allowed(
                    AddressCreationMode.OpenWithModeration,
                    LocationAddressVisibilityEnum.OrganizationScoped,
                    request.OrganizationId);
            }
        }

        return AddressGovernancePolicyDecision.Allowed(
            AddressCreationMode.OpenWithModeration,
            LocationAddressVisibilityEnum.CreatorPrivate);
    }

    private Task<AuthorizationDecision> AuthorizeLocationAsync(
        AddressGovernancePolicyRequest request,
        string action,
        CancellationToken cancellationToken) =>
        authorizationProvider.AuthorizeAsync(
            new AuthorizationRequest(
                ResourceKinds.Location,
                GovernanceResourceId,
                action,
                Scope: Scope(request),
                Facts: new PreCreateAuthorizationFacts(
                    request.TenantId!.Value,
                    OrganizationId: ValidOrganizationId(request)),
                Subject: new AuthorizationSubject(request.UserId),
                Tenant: new AuthorizationTenant(request.TenantId, ValidOrganizationId(request))),
            cancellationToken);

    private Task<AuthorizationDecision> AuthorizeOrganizationAsync(
        AddressGovernancePolicyRequest request,
        CancellationToken cancellationToken) =>
        authorizationProvider.AuthorizeAsync(
            new AuthorizationRequest(
                ResourceKinds.Organization,
                GovernanceResourceId,
                AuthorizationActions.Locations.CreateCustomAddress,
                Scope: Scope(request),
                Facts: new OrganizationAuthorizationFacts(
                    request.TenantId!.Value,
                    request.OrganizationId),
                Subject: new AuthorizationSubject(request.UserId),
                Tenant: new AuthorizationTenant(request.TenantId, request.OrganizationId)),
            cancellationToken);

    private static AuthorizationScope Scope(AddressGovernancePolicyRequest request) => new(
        TenantId: request.TenantId?.ToString(),
        OrganizationId: ValidOrganizationId(request)?.ToString());

    private static Guid? ValidOrganizationId(AddressGovernancePolicyRequest request) =>
        HasOrganization(request) ? request.OrganizationId : null;

    private static bool HasTrustedContext(AddressGovernancePolicyRequest request) =>
        request.TenantId is { } tenantId && tenantId != Guid.Empty &&
        request.ActorId is { } actorId && actorId != Guid.Empty &&
        request.UserId is { } userId && userId != Guid.Empty &&
        request.OrganizationId != Guid.Empty;

    private static bool HasOrganization(AddressGovernancePolicyRequest request) =>
        request.OrganizationId is { } organizationId && organizationId != Guid.Empty;

    private static AddressCreationMode ParseMode(string? storedMode) =>
        storedMode is not null && Enum.TryParse(storedMode, ignoreCase: false, out AddressCreationMode mode) &&
        Enum.IsDefined(mode)
            ? mode
            : AddressCreationMode.Disabled;
}
