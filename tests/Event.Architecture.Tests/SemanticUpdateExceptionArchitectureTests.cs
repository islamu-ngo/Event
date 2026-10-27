using System.Text.Json;
using Asp.Versioning;
using Explore.API.Controllers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Architecture.Tests;

public sealed class SemanticUpdateExceptionArchitectureTests
{
    private static readonly SemanticException[] Exceptions =
    [
        new("SaveTenantOnboardingStepProgress", "/api/tenantonboarding/steps", "put", "Atomic onboarding progress checkpoint."),
        new("SetInstanceAdminTenantSetting", "/api/admin/instance/tenants/{tenantId}/settings/{key}", "put", "Exact tenant setting replacement addressed by route key."),
        new("UpdateOrganizationMemberRole", "/api/organizationmember/role", "put", "Authorized member-role replacement with last-admin protection."),
        new("SetOrganizationNotificationPreferenceMute", "/api/organization/{id}/notification-preferences/mute", "put", "Exact organization mute-state replacement."),
        new("UpdateOrganizationApprovalStatus", "/api/organization/{id}/approval-status", "put", "Audited organization approval transition."),
        new("SetActiveAppearanceProfile", "/api/user/appearance/active-profile", "put", "Exact active-profile selection."),
        new("SetAppearanceThemeMode", "/api/user/appearance/mode", "put", "Exact current theme-mode replacement."),
        new("ArchiveAppearanceProfile", "/api/user/appearance/profiles/{profileId}/archive", "put", "Appearance-profile lifecycle transition."),
        new("SetGroupNotificationPreferenceMute", "/api/group/{id}/notification-preferences/mute", "put", "Exact group mute-state replacement."),
        new("UpdateGroupApprovalStatus", "/api/group/{id}/approval-status", "put", "Audited group approval transition."),
        new("PauseEmailDispatchTenant", "/api/admin/email-dispatch/tenants/{tenantId}/pause", "put", "Operational tenant dispatch pause transition."),
        new("ParkEmailDispatch", "/api/admin/email-dispatch/tenants/{tenantId}/outbox/{outboxId}/park", "put", "Operational outbox parking transition."),
        new("PauseEmailDispatchProcessor", "/api/admin/email-dispatch/control/pause", "put", "Operational processor pause transition."),
        new("SetEmailDispatchGlobalRateLimitOverride", "/api/admin/email-dispatch/control/rate-limit", "put", "Exact global rate-limit control replacement."),
        new("UpdateGroupMember", "/api/groupmember/role", "put", "Authorized member-role replacement with last-admin protection."),
        new("SetCurrentUserNotificationPreferenceMute", "/api/notification/preferences/me/mute", "put", "Exact current-user mute-state replacement."),
        new("MarkNotificationAsRead", "/api/notification/{id}/read", "patch", "Idempotent notification read transition."),
        new("ArchiveNotification", "/api/notification/{id}/archive", "patch", "Reversible notification archive transition."),
        new("SnoozeNotification", "/api/notification/{id}/snooze", "patch", "Notification snooze transition to one timestamp."),
        new("ReorderTenantNavigationLinks", "/api/tenant/navigation/reorder", "put", "Complete ordered navigation sequence replacement."),
        new("SetEventSessionCustomPropertyValue", "/api/eventsessioncustomproperty/value", "put", "Complete scalar custom-property value replacement."),
        new("SetEventSessionCustomPropertyMultiValues", "/api/eventsessioncustomproperty/values", "put", "Complete custom-property value-set replacement."),
        new("UpdateUserSettingsBatch", "/api/settings/user/{category}", "put", "Exact user category replacement with registered keys."),
        new("UpdateUserSetting", "/api/settings/user/keys/{key}", "put", "Exact user setting replacement addressed by route key."),
        new("UpdateTenantSettingsBatch", "/api/settings/tenant/{category}", "put", "Exact tenant category replacement with lock enforcement."),
        new("UpdateTenantSetting", "/api/settings/tenant/keys/{key}", "put", "Exact tenant setting replacement addressed by route key."),
        new("UpdateInstanceAtprotoFederationSetting", "/api/settings/instance/atproto-federation/{key}", "put", "Exact governed instance setting replacement addressed by route key."),
        new("UploadStorageUploadSessionContent", "/api/storageobject/upload-sessions/{uploadSessionId}/content", "put", "Complete byte content upload for one reserved session."),
        new("SetEventCustomPropertyValue", "/api/eventcustomproperty/value", "put", "Complete scalar custom-property value replacement."),
        new("SetEventCustomPropertyMultiValues", "/api/eventcustomproperty/values", "put", "Complete custom-property value-set replacement."),
        new("UpdateUserLastActiveTenant", "/api/user/active-tenant/{tenantId}", "post", "Authenticated tenant-context selection action."),
        new("UpdateCurrentUserProfileImage", "/api/user/{id}/profile-image", "patch", "Ownership-checked, revision-checked avatar replacement independent of private profile collection."),
        new("ConfigureEventParticipation", "/api/events/{eventId}/participation", "patch", "Atomic coupled participation-configuration replacement."),
        new("UpdateEventPublicAction", "/api/events/{eventId}/public-actions/{actionId}", "put", "Atomic reviewed public-action replacement."),
        new("UpdateEventTicketType", "/api/events/{eventId}/ticketing/ticket-types/{ticketTypeId}", "put", "Atomic ticket-type replacement within one draft catalog."),
        new("UpdateEventCapacityPool", "/api/events/{eventId}/ticketing/capacity-pools/{capacityPoolId}", "put", "Atomic capacity-pool replacement within one draft catalog."),
        new("UpdateInstancePlatformMonetizationSettings", "/api/instance/settings/platform-monetization", "put", "Complete instance-level fee and contribution settings replacement with expected revision checks."),
        new("SaveInstanceOnboardingProfile", "/api/instanceonboarding/profile", "patch", "Setup-secret-gated non-secret profile save before instance onboarding completes.")
    ];

    [Test]
    public async Task PublicSemanticExceptionsMustMatchExactRegistry()
    {
        await using Stream stream = GeneratedContractInputs.OpenSchema();
        using JsonDocument document = await JsonDocument.ParseAsync(stream);
        JsonElement paths = document.RootElement.GetProperty("paths");

        foreach (SemanticException exception in Exceptions)
        {
            JsonElement operation = paths.GetProperty(exception.Path).GetProperty(exception.Method);
            await Assert.That(operation.GetProperty("operationId").GetString()).IsEqualTo(exception.OperationId);
            await Assert.That(string.IsNullOrWhiteSpace(exception.Rationale)).IsFalse();
        }

        await Assert.That(Exceptions.Select(exception => (exception.Path, exception.Method)).Distinct().Count())
            .IsEqualTo(Exceptions.Length);
    }

    [Test]
    public async Task ProfileImageReplacementPreservesItsDedicatedBodyAndConcurrencyHeader()
    {
        await using Stream stream = GeneratedContractInputs.OpenSchema();
        using JsonDocument document = await JsonDocument.ParseAsync(stream);
        JsonElement operation = document.RootElement.GetProperty("paths")
            .GetProperty("/api/user/{id}/profile-image").GetProperty("patch");
        await Assert.That(operation.GetProperty("requestBody").GetProperty("required").GetBoolean()).IsTrue();
        await Assert.That(operation.GetProperty("requestBody").GetProperty("content").EnumerateObject()
            .Select(media => media.Value.GetProperty("schema").GetProperty("$ref").GetString())
            .Distinct().ToArray()).IsEquivalentTo(new[] { "#/components/schemas/UpdateUserProfileImageDto" });
        await Assert.That(operation.GetProperty("parameters").EnumerateArray().Any(parameter =>
            parameter.GetProperty("name").GetString() == "If-Match"
            && parameter.GetProperty("in").GetString() == "header")).IsTrue();
    }

    [Test]
    public async Task NestedAndApplicationOnlyUpdatesMustNotHaveDirectControllerOperations()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddControllers().AddApplicationPart(typeof(UserProfileImageController).Assembly);
        builder.Services.AddApiVersioning().AddMvc();
        await using var app = builder.Build();
        app.MapControllers();
        string[] bodyTypes = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>())
            .OfType<ControllerActionDescriptor>()
            .SelectMany(action => action.Parameters)
            .Where(parameter => parameter.BindingInfo?.BindingSource?.Id == BindingSource.Body.Id)
            .SelectMany(parameter => BodyContractTypes(parameter.ParameterType))
            .Select(type => type.Name)
            .Distinct().ToArray();
        await Assert.That(bodyTypes).Contains(nameof(Explore.Application.DTOs.User.UpdateUserProfileImageDto));

        string[] internalTypes =
        [
            "UpdateActorAppearanceDto",
            "UpdateUserNamesDto",
            "UpdateEventDraftRequestDto",
            "UpdateEventTemplateDefinitionDto",
            "UpdateEventSessionTemplateDefinitionDto",
            "UpdateEventRoleAssignmentWindowCommand",
            "UpdateRolePermissionsCommand"
        ];

        foreach (string internalType in internalTypes)
        {
            await Assert.That(bodyTypes).DoesNotContain(internalType);
        }
    }

    private static IEnumerable<Type> BodyContractTypes(Type type) => type.HasElementType
        ? BodyContractTypes(type.GetElementType()!)
        : new[] { type }.Concat(type.GetGenericArguments().SelectMany(BodyContractTypes));

    [Test]
    [Arguments(typeof(Explore.Application.DTOs.User.UpdateUserProfileImageDto[]))]
    [Arguments(typeof(IReadOnlyList<Explore.Application.DTOs.User.UpdateUserProfileImageDto>))]
    [Arguments(typeof(Dictionary<string, Explore.Application.DTOs.User.UpdateUserProfileImageDto[]>))]
    public async Task BodyContractDiscoveryIncludesArrayAndGenericArguments(Type bodyType)
    {
        await Assert.That(BodyContractTypes(bodyType).Select(type => type.Name).ToArray())
            .Contains(nameof(Explore.Application.DTOs.User.UpdateUserProfileImageDto));
    }

    private sealed record SemanticException(string OperationId, string Path, string Method, string Rationale);
}
