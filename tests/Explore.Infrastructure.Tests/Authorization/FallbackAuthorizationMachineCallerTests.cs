using Explore.Application.Authentication;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Settings;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Authorization;

public class FallbackAuthorizationMachineCallerTests
{
    private readonly IAdminContext _adminContext;
    private readonly IMachinePrincipalAccessor _machinePrincipalAccessor;
    private readonly IEventAuthoritySnapshotService _eventAuthoritySnapshotService;
    private readonly IHierarchicalSettingsResolver _settingsResolver;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<FallbackAuthorizationService> _logger;
    private readonly FallbackAuthorizationService _sut;

    public FallbackAuthorizationMachineCallerTests()
    {
        _adminContext = Substitute.For<IAdminContext>();
        _machinePrincipalAccessor = Substitute.For<IMachinePrincipalAccessor>();
        _eventAuthoritySnapshotService = Substitute.For<IEventAuthoritySnapshotService>();
        _settingsResolver = Substitute.For<IHierarchicalSettingsResolver>();
        _tenantContext = Substitute.For<ITenantContext>();
        _logger = Substitute.For<ILogger<FallbackAuthorizationService>>();

        _sut = new FallbackAuthorizationService(
            _adminContext,
            _machinePrincipalAccessor,
            _eventAuthoritySnapshotService,
            Substitute.For<IOrganizationMemberRepository>(),
            Substitute.For<IGroupMemberRepository>(),
            _settingsResolver,
            _tenantContext,
            _logger);
    }

    [Test]
    [Category("Phase43Ticketing")]
    public async Task EventManageTickets_MachineCaller_Denied()
    {
        var tenantId = Guid.NewGuid();
        var eventId = Guid.NewGuid();

        SetMachineContext(
            ExternalApiKeyOwnerType.InstanceAdmin,
            tenantId: null,
            Guid.NewGuid(),
            ExternalApiKeyScopes.AdminInstance);

        await Assert.That(await _sut.IsAllowedAsync(
            ResourceKinds.Event,
            eventId.ToString(),
            AuthorizationActions.Events.ManageTickets,
            new Dictionary<string, object>
            {
                ["tenantId"] = tenantId,
                ["eventId"] = eventId
            },
            CancellationToken.None)).IsFalse();
    }

    [Test]
    [Arguments("machine.registration_form_update_current_deny", ResourceKinds.RegistrationForm, AuthorizationActions.RegistrationForms.Update, ExternalApiKeyScopes.AdminTenant, false)]
    [Arguments("machine.event_public_action_current_allow", ResourceKinds.Event, AuthorizationActions.Events.ManagePublicActions, ExternalApiKeyScopes.AdminTenant, true)]
    [Arguments("machine.contact_export_current_allow", ResourceKinds.EventContactShareConsent, AuthorizationActions.ExportSharedContacts, ExternalApiKeyScopes.AdminTenant, true)]
    public async Task IsAllowed_Phase0MachineCallerCurrentBaseline(
        string scenario,
        string resourceKind,
        string action,
        string scope,
        bool expectedCurrentOutcome)
    {
        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        SetMachineContext(ExternalApiKeyOwnerType.Tenant, tenantId, tenantId, scope);

        var result = await _sut.IsAllowedAsync(
            resourceKind,
            ownerId.ToString("D"),
            action,
            new Dictionary<string, object>
            {
                ["tenantId"] = tenantId,
                ["eventId"] = Guid.NewGuid(),
                ["organizationId"] = ownerId
            },
            CancellationToken.None);

        await Assert.That(result)
            .IsEqualTo(expectedCurrentOutcome)
            .Because($"phase-0 provider scenario '{scenario}' must pin current machine-caller authorization.");
    }

    private void SetMachineContext(ExternalApiKeyOwnerType ownerType, Guid? tenantId, Guid ownerId, params string[] scopes)
    {
        var ctx = new ApiKeyPrincipalContext(
            KeyId: $"key-{Guid.NewGuid():N}",
            TenantId: tenantId,
            OwnerType: ownerType,
            OwnerId: ownerId,
            Scopes: scopes);
        _machinePrincipalAccessor.IsMachineCaller.Returns(true);
        _machinePrincipalAccessor.Current.Returns(ctx);
    }

    [Test]
    public async Task InstanceAdminOwner_WithScopeMatch_DeniesNonAllowlistedResourceAcrossAnyTenant()
    {
        var ownerId = Guid.NewGuid();
        var arbitraryTenantId = Guid.NewGuid();
        SetMachineContext(ExternalApiKeyOwnerType.InstanceAdmin, tenantId: null, ownerId,
            ExternalApiKeyScopes.AdminInstance);

        var attrs = new Dictionary<string, object> { ["tenantId"] = arbitraryTenantId };

        bool result = await _sut.IsAllowedAsync(ResourceKinds.Event, Guid.NewGuid().ToString(),
            AuthorizationActions.Delete, attrs, CancellationToken.None);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task InstanceAdminOwner_WithAdminInstanceScope_DeniesIncomingWebhookProcessing()
    {
        SetMachineContext(
            ExternalApiKeyOwnerType.InstanceAdmin,
            tenantId: null,
            Guid.NewGuid(),
            ExternalApiKeyScopes.AdminInstance);

        var result = await _sut.IsAllowedAsync(
            ResourceKinds.Webhook,
            Guid.NewGuid().ToString("D"),
            AuthorizationActions.Webhooks.ProcessIncoming,
            new Dictionary<string, object> { ["tenantId"] = Guid.NewGuid().ToString("D") },
            CancellationToken.None);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task InstanceAdminOwner_WithMissingAdminInstanceScope_DeniesEvenOnBaseResources()
    {
        SetMachineContext(ExternalApiKeyOwnerType.InstanceAdmin, tenantId: null, Guid.NewGuid(),
            ExternalApiKeyScopes.EventsRead);

        bool result = await _sut.IsAllowedAsync(ResourceKinds.InstanceSetting, "features.enabled",
            AuthorizationActions.Update, null, CancellationToken.None);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task InstanceAdminOwner_WithAdminInstanceScope_AllowsPlatformOperation()
    {
        SetMachineContext(ExternalApiKeyOwnerType.InstanceAdmin, tenantId: null, Guid.NewGuid(),
            ExternalApiKeyScopes.AdminInstance);

        bool result = await _sut.IsAllowedAsync(ResourceKinds.InstanceSetting, "platform.feature",
            AuthorizationActions.InstanceSettings.Update, null, CancellationToken.None);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task MachineCaller_CheckSettingAccessInstanceSetting_UsesApiKeyContextWithoutAmbientAdmin()
    {
        SetMachineContext(ExternalApiKeyOwnerType.InstanceAdmin, tenantId: null, Guid.NewGuid(),
            ExternalApiKeyScopes.AdminInstance);
        _adminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(false);

        var result = await _sut.CheckSettingAccessAsync(
            "platform.feature",
            AuthorizationActions.InstanceSettings.Update,
            cancellationToken: CancellationToken.None);

        await Assert.That(result).IsTrue();
        await _adminContext.DidNotReceive().IsInstanceAdminAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task MachineCaller_CheckSettingAccessInstanceSetting_DeniesTenantKeyDespiteAmbientAdmin()
    {
        var tenantId = Guid.NewGuid();
        SetMachineContext(ExternalApiKeyOwnerType.Tenant, tenantId, tenantId,
            ExternalApiKeyScopes.AdminTenant);
        _adminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(true);

        var result = await _sut.CheckSettingAccessAsync(
            "platform.feature",
            AuthorizationActions.InstanceSettings.Update,
            cancellationToken: CancellationToken.None);

        await Assert.That(result).IsFalse();
        await _adminContext.DidNotReceive().IsInstanceAdminAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task MachineCaller_CheckSettingAccessLockedTenantSetting_DeniesTenantKeyWithoutAmbientAdmin()
    {
        var tenantId = Guid.NewGuid();
        SetMachineContext(ExternalApiKeyOwnerType.Tenant, tenantId, tenantId,
            ExternalApiKeyScopes.AdminTenant);
        _adminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(true);
        _settingsResolver.ResolveWithMetadataAsync("deployment.mode", Arg.Any<SettingContext>(), Arg.Any<CancellationToken>())
            .Returns(new ResolvedSetting { Key = "deployment.mode", IsLocked = true });

        var result = await _sut.CheckSettingAccessAsync(
            "deployment.mode",
            AuthorizationActions.Update,
            tenantId: tenantId,
            cancellationToken: CancellationToken.None);

        await Assert.That(result).IsFalse();
        await _adminContext.DidNotReceive().IsInstanceAdminAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task MachineCaller_CheckSettingAccessUnlockedTenantSetting_AllowsTenantKeyWithoutAmbientAdmin()
    {
        var tenantId = Guid.NewGuid();
        SetMachineContext(ExternalApiKeyOwnerType.Tenant, tenantId, tenantId,
            ExternalApiKeyScopes.AdminTenant);
        _adminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(false);
        _settingsResolver.ResolveWithMetadataAsync("events.require_approval", Arg.Any<SettingContext>(), Arg.Any<CancellationToken>())
            .Returns(new ResolvedSetting { Key = "events.require_approval", IsLocked = false });

        var result = await _sut.CheckSettingAccessAsync(
            "events.require_approval",
            AuthorizationActions.Update,
            tenantId: tenantId,
            cancellationToken: CancellationToken.None);

        await Assert.That(result).IsTrue();
        await _adminContext.DidNotReceive().IsInstanceAdminAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task MachineCaller_CheckSettingAccessLockedTenantSetting_DeniesUserOwnerTenantAdminWithoutAmbientAdmin()
    {
        var tenantId = Guid.NewGuid();
        var userOwnerId = Guid.NewGuid();
        SetMachineContext(ExternalApiKeyOwnerType.User, tenantId, userOwnerId,
            ExternalApiKeyScopes.AdminTenant);
        _settingsResolver.ResolveWithMetadataAsync("events.require_approval", Arg.Any<SettingContext>(), Arg.Any<CancellationToken>())
            .Returns(new ResolvedSetting { Key = "events.require_approval", IsLocked = true });

        var result = await _sut.CheckSettingAccessAsync(
            "events.require_approval",
            AuthorizationActions.Update,
            tenantId: tenantId,
            cancellationToken: CancellationToken.None);

        await Assert.That(result).IsFalse();
        await _adminContext.DidNotReceive().IsInstanceAdminAsync(userOwnerId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SafeMode_TenantMachineCaller_DeniesEvenWithMatchingTenantScope()
    {
        var tenantId = Guid.NewGuid();
        SetMachineContext(ExternalApiKeyOwnerType.Tenant, tenantId, tenantId,
            ExternalApiKeyScopes.AdminTenant);
        _sut.ActivateSafeMode();

        bool result = await _sut.IsAllowedAsync(ResourceKinds.Category, Guid.NewGuid().ToString("D"),
            AuthorizationActions.Create, new Dictionary<string, object> { ["tenantId"] = tenantId }, CancellationToken.None);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task TenantOwner_ScopeGateDeniesInstanceResources()
    {
        var tenantId = Guid.NewGuid();
        SetMachineContext(ExternalApiKeyOwnerType.Tenant, tenantId, tenantId,
            ExternalApiKeyScopes.AdminTenant);

        bool result = await _sut.IsAllowedAsync(ResourceKinds.InstanceSetting, "platform.feature",
            AuthorizationActions.Update, null, CancellationToken.None);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task TenantOwner_WithMatchingTenantResource_Allows()
    {
        var tenantId = Guid.NewGuid();
        SetMachineContext(ExternalApiKeyOwnerType.Tenant, tenantId, tenantId,
            ExternalApiKeyScopes.EventsWrite, ExternalApiKeyScopes.AdminTenant);

        var attrs = new Dictionary<string, object> { ["tenantId"] = tenantId };

        bool result = await _sut.IsAllowedAsync(ResourceKinds.Event, Guid.NewGuid().ToString(),
            AuthorizationActions.Create, attrs, CancellationToken.None);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task TenantOwner_AccessingDifferentTenant_Denies()
    {
        var ownTenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        SetMachineContext(ExternalApiKeyOwnerType.Tenant, ownTenantId, ownTenantId,
            ExternalApiKeyScopes.EventsWrite);

        var attrs = new Dictionary<string, object> { ["tenantId"] = otherTenantId };

        bool result = await _sut.IsAllowedAsync(ResourceKinds.Event, Guid.NewGuid().ToString(),
            AuthorizationActions.Create, attrs, CancellationToken.None);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task OrganizationOwner_AccessingOwnOrgResource_Allows()
    {
        var tenantId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        SetMachineContext(ExternalApiKeyOwnerType.Organization, tenantId, orgId,
            ExternalApiKeyScopes.EventsWrite);

        var attrs = new Dictionary<string, object>
        {
            ["tenantId"] = tenantId,
            ["organizationId"] = orgId
        };

        bool result = await _sut.IsAllowedAsync(ResourceKinds.Event, Guid.NewGuid().ToString(),
            AuthorizationActions.Create, attrs, CancellationToken.None);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task OrganizationOwner_AccessingDifferentOrg_Denies()
    {
        var tenantId = Guid.NewGuid();
        var ownOrgId = Guid.NewGuid();
        var otherOrgId = Guid.NewGuid();
        SetMachineContext(ExternalApiKeyOwnerType.Organization, tenantId, ownOrgId,
            ExternalApiKeyScopes.EventsWrite);

        var attrs = new Dictionary<string, object>
        {
            ["tenantId"] = tenantId,
            ["organizationId"] = otherOrgId
        };

        bool result = await _sut.IsAllowedAsync(ResourceKinds.Event, Guid.NewGuid().ToString(),
            AuthorizationActions.Create, attrs, CancellationToken.None);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task OrganizationOwner_AccessingTenantWideResource_Denies()
    {
        var tenantId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        SetMachineContext(ExternalApiKeyOwnerType.Organization, tenantId, orgId,
            ExternalApiKeyScopes.AdminTenant);

        var attrs = new Dictionary<string, object> { ["tenantId"] = tenantId };

        bool result = await _sut.IsAllowedAsync(ResourceKinds.Tenant, tenantId.ToString(),
            AuthorizationActions.Update, attrs, CancellationToken.None);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task GroupOwner_AccessingOwnGroup_Allows()
    {
        var tenantId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        SetMachineContext(ExternalApiKeyOwnerType.Group, tenantId, groupId,
            ExternalApiKeyScopes.GroupsWrite);

        var attrs = new Dictionary<string, object>
        {
            ["tenantId"] = tenantId,
            ["groupId"] = groupId
        };

        bool result = await _sut.IsAllowedAsync(ResourceKinds.Group, groupId.ToString(),
            AuthorizationActions.Update, attrs, CancellationToken.None);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task GroupOwner_AccessingDifferentGroup_Denies()
    {
        var tenantId = Guid.NewGuid();
        var ownGroupId = Guid.NewGuid();
        var otherGroupId = Guid.NewGuid();
        SetMachineContext(ExternalApiKeyOwnerType.Group, tenantId, ownGroupId,
            ExternalApiKeyScopes.GroupsWrite);

        var attrs = new Dictionary<string, object>
        {
            ["tenantId"] = tenantId,
            ["groupId"] = otherGroupId
        };

        bool result = await _sut.IsAllowedAsync(ResourceKinds.Group, otherGroupId.ToString(),
            AuthorizationActions.Update, attrs, CancellationToken.None);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task GroupOwner_AccessingTenantWideResource_Denies()
    {
        var tenantId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        SetMachineContext(ExternalApiKeyOwnerType.Group, tenantId, groupId,
            ExternalApiKeyScopes.AdminTenant);

        var attrs = new Dictionary<string, object> { ["tenantId"] = tenantId };

        bool result = await _sut.IsAllowedAsync(ResourceKinds.Category, Guid.NewGuid().ToString(),
            AuthorizationActions.Create, attrs, CancellationToken.None);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task GroupOwner_AccessingOrgScopedResource_Denies()
    {
        var tenantId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        SetMachineContext(ExternalApiKeyOwnerType.Group, tenantId, groupId,
            ExternalApiKeyScopes.OrganizationsWrite);

        var attrs = new Dictionary<string, object>
        {
            ["tenantId"] = tenantId,
            ["organizationId"] = Guid.NewGuid()
        };

        bool result = await _sut.IsAllowedAsync(ResourceKinds.Organization, Guid.NewGuid().ToString(),
            AuthorizationActions.Update, attrs, CancellationToken.None);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task UserOwner_IsInstanceAdmin_DeniesNonAllowlistedResource()
    {
        var userId = Guid.NewGuid();
        SetMachineContext(ExternalApiKeyOwnerType.User, Guid.NewGuid(), userId,
            ExternalApiKeyScopes.EventsWrite);
        _adminContext.IsInstanceAdminAsync(userId, Arg.Any<CancellationToken>()).Returns(true);

        bool result = await _sut.IsAllowedAsync(ResourceKinds.Event, Guid.NewGuid().ToString(),
            AuthorizationActions.Delete, null, CancellationToken.None);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task UserOwner_AccessingSelfUserResource_Allows()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        SetMachineContext(ExternalApiKeyOwnerType.User, tenantId, userId,
            ExternalApiKeyScopes.UsersRead);
        _adminContext.IsInstanceAdminAsync(userId, Arg.Any<CancellationToken>()).Returns(false);

        var attrs = new Dictionary<string, object> { ["tenantId"] = tenantId };

        bool result = await _sut.IsAllowedAsync(ResourceKinds.User, userId.ToString(),
            AuthorizationActions.View, attrs, CancellationToken.None);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task UserOwner_AccessingOtherUser_Denies()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        SetMachineContext(ExternalApiKeyOwnerType.User, tenantId, userId,
            ExternalApiKeyScopes.UsersWrite);
        _adminContext.IsInstanceAdminAsync(userId, Arg.Any<CancellationToken>()).Returns(false);
        _adminContext.GetAdminTenantIdsAsync(userId, Arg.Any<CancellationToken>()).Returns(new List<Guid>());

        var attrs = new Dictionary<string, object> { ["tenantId"] = tenantId };

        bool result = await _sut.IsAllowedAsync(ResourceKinds.User, otherUserId.ToString(),
            AuthorizationActions.Update, attrs, CancellationToken.None);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task UserOwner_TenantAdminForTenant_AllowsTenantResource()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        SetMachineContext(ExternalApiKeyOwnerType.User, tenantId, userId,
            ExternalApiKeyScopes.AdminTenant);
        _adminContext.IsInstanceAdminAsync(userId, Arg.Any<CancellationToken>()).Returns(false);
        _adminContext.GetAdminTenantIdsAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new List<Guid> { tenantId });

        var attrs = new Dictionary<string, object> { ["tenantId"] = tenantId };

        bool result = await _sut.IsAllowedAsync(ResourceKinds.Category, Guid.NewGuid().ToString(),
            AuthorizationActions.Create, attrs, CancellationToken.None);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task UserOwner_OrgAdmin_AllowsOrgScopedResource()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        SetMachineContext(ExternalApiKeyOwnerType.User, tenantId, userId,
            ExternalApiKeyScopes.EventsWrite);
        _adminContext.IsInstanceAdminAsync(userId, Arg.Any<CancellationToken>()).Returns(false);
        _adminContext.GetAdminTenantIdsAsync(userId, Arg.Any<CancellationToken>()).Returns(new List<Guid>());
        _adminContext.GetAdminOrganizationIdsAsync(userId, tenantId, Arg.Any<CancellationToken>()).Returns(new List<Guid> { orgId });

        var attrs = new Dictionary<string, object>
        {
            ["tenantId"] = tenantId,
            ["organizationId"] = orgId
        };

        bool result = await _sut.IsAllowedAsync(ResourceKinds.Event, Guid.NewGuid().ToString(),
            AuthorizationActions.Create, attrs, CancellationToken.None);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task UserOwner_IgnoresAmbientOrganizationAdmin()
    {
        var tenantId = Guid.NewGuid();
        var userOwnerId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        SetMachineContext(ExternalApiKeyOwnerType.User, tenantId, userOwnerId, ExternalApiKeyScopes.EventsWrite);
        _adminContext.IsInstanceAdminAsync(userOwnerId, Arg.Any<CancellationToken>()).Returns(false);
        _adminContext.GetAdminTenantIdsAsync(userOwnerId, Arg.Any<CancellationToken>()).Returns([]);
        _adminContext.IsOrganizationAdminAsync(orgId, Arg.Any<CancellationToken>()).Returns(true);

        var attrs = new Dictionary<string, object>
        {
            ["tenantId"] = tenantId,
            ["organizationId"] = orgId
        };

        var result = await _sut.IsAllowedAsync(
            ResourceKinds.Event,
            Guid.NewGuid().ToString("D"),
            AuthorizationActions.Create,
            attrs,
            CancellationToken.None);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task UserOwner_GroupAdmin_AllowsGroupResource()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        SetMachineContext(ExternalApiKeyOwnerType.User, tenantId, userId,
            ExternalApiKeyScopes.GroupsWrite);
        _adminContext.IsInstanceAdminAsync(userId, Arg.Any<CancellationToken>()).Returns(false);
        _adminContext.GetAdminTenantIdsAsync(userId, Arg.Any<CancellationToken>()).Returns(new List<Guid>());
        _adminContext.GetAdminGroupIdsAsync(userId, tenantId, Arg.Any<CancellationToken>()).Returns(new List<Guid> { groupId });

        var attrs = new Dictionary<string, object>
        {
            ["tenantId"] = tenantId,
            ["groupId"] = groupId
        };

        bool result = await _sut.IsAllowedAsync(ResourceKinds.Group, groupId.ToString(),
            AuthorizationActions.Update, attrs, CancellationToken.None);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task UserOwner_NoAdminAuthority_DeniesOrgScopedResource()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        SetMachineContext(ExternalApiKeyOwnerType.User, tenantId, userId,
            ExternalApiKeyScopes.EventsWrite);
        _adminContext.IsInstanceAdminAsync(userId, Arg.Any<CancellationToken>()).Returns(false);
        _adminContext.GetAdminTenantIdsAsync(userId, Arg.Any<CancellationToken>()).Returns(new List<Guid>());
        _adminContext.IsOrganizationAdminAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);

        var attrs = new Dictionary<string, object>
        {
            ["tenantId"] = tenantId,
            ["organizationId"] = Guid.NewGuid()
        };

        bool result = await _sut.IsAllowedAsync(ResourceKinds.Event, Guid.NewGuid().ToString(),
            AuthorizationActions.Create, attrs, CancellationToken.None);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task UserOwner_NullTenantOnContext_DeniesCrossTenantAccess()
    {
        var userId = Guid.NewGuid();
        SetMachineContext(ExternalApiKeyOwnerType.User, tenantId: null, userId,
            ExternalApiKeyScopes.EventsRead);
        _adminContext.IsInstanceAdminAsync(userId, Arg.Any<CancellationToken>()).Returns(false);

        var attrs = new Dictionary<string, object> { ["tenantId"] = Guid.NewGuid() };

        bool result = await _sut.IsAllowedAsync(ResourceKinds.Event, Guid.NewGuid().ToString(),
            AuthorizationActions.View, attrs, CancellationToken.None);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task MachineCaller_WithoutPrincipalContext_Denies()
    {
        _machinePrincipalAccessor.IsMachineCaller.Returns(true);
        _machinePrincipalAccessor.Current.Returns((ApiKeyPrincipalContext?)null);

        bool result = await _sut.IsAllowedAsync(ResourceKinds.Event, Guid.NewGuid().ToString(),
            AuthorizationActions.View, null, CancellationToken.None);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task MachineCaller_DoesNotUseAmbientInstanceAdminShortcut()
    {
        var tenantId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        SetMachineContext(ExternalApiKeyOwnerType.Organization, tenantId, orgId,
            ExternalApiKeyScopes.EventsRead);

        _adminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(true);

        var attrs = new Dictionary<string, object> { ["tenantId"] = Guid.NewGuid() };

        bool result = await _sut.IsAllowedAsync(ResourceKinds.Tenant, tenantId.ToString(),
            AuthorizationActions.Update, attrs, CancellationToken.None);

        await Assert.That(result).IsFalse();
        await _adminContext.DidNotReceive().IsInstanceAdminAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UserOwner_IgnoresAmbientOrganizationAdminWhenOwnerCollectionsOmitScope()
    {
        var tenantId = Guid.NewGuid();
        var userOwnerId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        SetMachineContext(ExternalApiKeyOwnerType.User, tenantId, userOwnerId, ExternalApiKeyScopes.EventsWrite);
        _adminContext.IsInstanceAdminAsync(userOwnerId, Arg.Any<CancellationToken>()).Returns(false);
        _adminContext.GetAdminTenantIdsAsync(userOwnerId, Arg.Any<CancellationToken>()).Returns([]);
        _adminContext.GetAdminOrganizationIdsAsync(userOwnerId, tenantId, Arg.Any<CancellationToken>()).Returns([]);
        _adminContext.IsOrganizationAdminAsync(orgId, Arg.Any<CancellationToken>()).Returns(true);

        var attrs = new Dictionary<string, object>
        {
            ["tenantId"] = tenantId,
            ["organizationId"] = orgId
        };

        var result = await _sut.IsAllowedAsync(
            ResourceKinds.Event,
            Guid.NewGuid().ToString("D"),
            AuthorizationActions.Create,
            attrs,
            CancellationToken.None);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task UserOwner_IgnoresAmbientGroupAdminWhenOwnerCollectionsOmitScope()
    {
        var tenantId = Guid.NewGuid();
        var userOwnerId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        SetMachineContext(ExternalApiKeyOwnerType.User, tenantId, userOwnerId, ExternalApiKeyScopes.GroupsWrite);
        _adminContext.IsInstanceAdminAsync(userOwnerId, Arg.Any<CancellationToken>()).Returns(false);
        _adminContext.GetAdminTenantIdsAsync(userOwnerId, Arg.Any<CancellationToken>()).Returns([]);
        _adminContext.GetAdminGroupIdsAsync(userOwnerId, tenantId, Arg.Any<CancellationToken>()).Returns([]);
        _adminContext.IsGroupAdminAsync(groupId, Arg.Any<CancellationToken>()).Returns(true);

        var attrs = new Dictionary<string, object>
        {
            ["tenantId"] = tenantId,
            ["groupId"] = groupId
        };

        var result = await _sut.IsAllowedAsync(
            ResourceKinds.Group,
            groupId.ToString("D"),
            AuthorizationActions.Update,
            attrs,
            CancellationToken.None);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task UserOwner_OrganizationOwnerCollectionMatch_AllowsOrgScopedResource()
    {
        var tenantId = Guid.NewGuid();
        var userOwnerId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        SetMachineContext(ExternalApiKeyOwnerType.User, tenantId, userOwnerId, ExternalApiKeyScopes.EventsWrite);
        _adminContext.IsInstanceAdminAsync(userOwnerId, Arg.Any<CancellationToken>()).Returns(false);
        _adminContext.GetAdminTenantIdsAsync(userOwnerId, Arg.Any<CancellationToken>()).Returns([]);
        _adminContext.GetAdminOrganizationIdsAsync(userOwnerId, tenantId, Arg.Any<CancellationToken>()).Returns([orgId]);

        var attrs = new Dictionary<string, object>
        {
            ["tenantId"] = tenantId,
            ["organizationId"] = orgId
        };

        var result = await _sut.IsAllowedAsync(
            ResourceKinds.Event,
            Guid.NewGuid().ToString("D"),
            AuthorizationActions.Create,
            attrs,
            CancellationToken.None);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task UserOwner_GroupOwnerCollectionMatch_AllowsGroupResource()
    {
        var tenantId = Guid.NewGuid();
        var userOwnerId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        SetMachineContext(ExternalApiKeyOwnerType.User, tenantId, userOwnerId, ExternalApiKeyScopes.GroupsWrite);
        _adminContext.IsInstanceAdminAsync(userOwnerId, Arg.Any<CancellationToken>()).Returns(false);
        _adminContext.GetAdminTenantIdsAsync(userOwnerId, Arg.Any<CancellationToken>()).Returns([]);
        _adminContext.GetAdminGroupIdsAsync(userOwnerId, tenantId, Arg.Any<CancellationToken>()).Returns([groupId]);

        var attrs = new Dictionary<string, object>
        {
            ["tenantId"] = tenantId,
            ["groupId"] = groupId
        };

        var result = await _sut.IsAllowedAsync(
            ResourceKinds.Group,
            groupId.ToString("D"),
            AuthorizationActions.Update,
            attrs,
            CancellationToken.None);

        await Assert.That(result).IsTrue();
    }
}
