namespace Event.Api.IntegrationTests.Features.Hateoas;

using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Authorization;
using Explore.Application.DTOs.TenantSettingsDocuments;
using Explore.Application.Hateoas;

public sealed class TenantDirectoryOperatorIdentityDocumentLinkPolicyTests
{
    [Test]
    public async Task GetLinks_EditCapabilityEmitsPermissionBoundPatch()
    {
        Guid tenantId = Guid.CreateVersion7();
        var dto = new TenantDirectoryOperatorIdentityDocumentDto
        {
            DocumentKey = "tenant.directory_operator_identity",
            SchemaVersion = 1,
            DefaultsVersion = "2026-08-28",
            Payload = new TenantDirectoryOperatorIdentityPayloadDto(),
            Source = "Tenant",
            SourceScopeId = tenantId,
            ConcurrencyStamp = Guid.CreateVersion7(),
            CanEdit = true
        };
        var policy = new TenantDirectoryOperatorIdentityDocumentLinkPolicy();

        LinkDefinition[] links = policy.GetLinks(dto, user: null).ToArray();

        LinkDefinition self = links.Single(link => link.Rel == LinkRelations.Self);
        await Assert.That(self.RouteName)
            .IsEqualTo(RouteNames.GetTenantDirectoryOperatorIdentityDocument);
        LinkDefinition edit = links.Single(link => link.Rel == LinkRelations.Edit);
        await Assert.That(edit.RouteName)
            .IsEqualTo(RouteNames.PatchTenantDirectoryOperatorIdentityDocument);
        await Assert.That(edit.Method).IsEqualTo("PATCH");
        await Assert.That(edit.RequiresAuth).IsTrue();
        await Assert.That(edit.PermissionAction).IsEqualTo(AuthorizationActions.Update);
        await Assert.That(edit.PermissionResourceKind).IsEqualTo(ResourceKinds.TenantSetting);
        await Assert.That(edit.PermissionResourceId)
            .IsEqualTo($"{tenantId}:tenant.directory_operator_identity");
    }

    [Test]
    public async Task GetLinks_WithoutEditCapabilityOmitsPatch()
    {
        var dto = new TenantDirectoryOperatorIdentityDocumentDto
        {
            DocumentKey = "tenant.directory_operator_identity",
            SchemaVersion = 1,
            DefaultsVersion = "2026-08-28",
            Payload = new TenantDirectoryOperatorIdentityPayloadDto(),
            Source = "Tenant",
            SourceScopeId = Guid.CreateVersion7(),
            ConcurrencyStamp = Guid.CreateVersion7(),
            CanEdit = false
        };
        var policy = new TenantDirectoryOperatorIdentityDocumentLinkPolicy();

        LinkDefinition[] links = policy.GetLinks(dto, user: null).ToArray();

        await Assert.That(links.Any(link => link.Rel == LinkRelations.Edit)).IsFalse();
    }
}
