using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Authorization;
using Explore.Application.DTOs.TenantSettingsDocuments;
using Explore.Application.Hateoas;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features.Hateoas;

public sealed class TenantBrandingSettingsDocumentLinkPolicyTests
{
    [Test]
    public async Task GetLinks_ShouldEmitSelfAndPermissionBoundEditLinks()
    {
        var tenantId = Guid.NewGuid();
        var dto = new TenantBrandingSettingsDocumentDto
        {
            DocumentKey = "tenant.branding",
            SchemaVersion = 1,
            DefaultsVersion = "2026-05-branding",
            Payload = new TenantBrandingSettingsPayloadDto(),
            Source = "Tenant",
            SourceScopeId = tenantId,
            ConcurrencyStamp = Guid.NewGuid(),
            IsLockedByInstance = true,
            CanChangeDisplayName = true
        };
        var policy = new TenantBrandingSettingsDocumentLinkPolicy();

        var links = policy.GetLinks(dto, user: null).ToArray();

        var self = links.Single(link => link.Rel == LinkRelations.Self);
        await Assert.That(self.RouteName).IsEqualTo(RouteNames.GetTenantBrandingSettingsDocument);
        await Assert.That(self.Method).IsEqualTo("GET");

        var edit = links.Single(link => link.Rel == LinkRelations.Edit);
        await Assert.That(edit.RouteName).IsEqualTo(RouteNames.PatchTenantBrandingSettingsDocument);
        await Assert.That(edit.Method).IsEqualTo("PATCH");
        await Assert.That(edit.RequiresAuth).IsTrue();
        await Assert.That(edit.PermissionResourceKind).IsEqualTo(ResourceKinds.TenantSetting);
        await Assert.That(edit.PermissionAction).IsEqualTo(AuthorizationActions.Update);
        await Assert.That(edit.PermissionResourceId).IsEqualTo($"{tenantId}:tenant.branding");
        // The branding document key and its instance lock are both decision-relevant: the tenant-setting
        // policy allows branding edits even while other settings are locked by the instance.
        await Assert.That(edit.PermissionFacts)
            .IsEqualTo(new TenantSettingAuthorizationFacts(tenantId, "tenant.branding", IsLockedByInstance: true));
        await Assert.That(edit.PermissionScope).IsNotNull();
        await Assert.That(edit.PermissionScope!.TenantId).IsEqualTo(tenantId.ToString());
    }

    [Test]
    public async Task GetLinks_WhenAllFieldCapabilitiesAreLocked_ShouldOmitEdit()
    {
        var dto = new TenantBrandingSettingsDocumentDto
        {
            DocumentKey = "tenant.branding",
            SchemaVersion = 1,
            DefaultsVersion = "2026-05-branding",
            Payload = new TenantBrandingSettingsPayloadDto(),
            Source = "Tenant",
            SourceScopeId = Guid.NewGuid(),
            ConcurrencyStamp = Guid.NewGuid(),
            IsLockedByInstance = true
        };
        var policy = new TenantBrandingSettingsDocumentLinkPolicy();

        var links = policy.GetLinks(dto, user: null).ToArray();

        await Assert.That(links.Any(link => link.Rel == LinkRelations.Edit)).IsFalse();
    }
}
