using Explore.API.Hateoas.Policies;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.EventResource;
using Explore.Domain.Enums;
using NSubstitute;

namespace Event.API.IntegrationTests.Features.Hateoas;

public sealed class EventMaterialHateoasTests
{
    [Test]
    public async Task DraftActionsCarryExactResourceAuthorityWithoutPublicationOrDelivery()
    {
        Guid tenantId = Guid.CreateVersion7();
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var resource = Resource(EventResourcePublicationStateEnum.Draft);
        var links = new EventMaterialDetailLinkPolicy(tenant).GetLinks(resource, null).ToArray();
        var edit = links.Single(link => link.Rel == "edit");
        await Assert.That(edit.PermissionFacts).IsEqualTo(new EventResourceTargetAuthorizationFacts(tenantId, resource.Id));
        await Assert.That(edit.PermissionAction).IsEqualTo("update");
        await Assert.That(edit.RequiresAuth).IsTrue();
        await Assert.That(links.Any(link => link.Rel is "publish" or "download" or "access")).IsFalse();
        await Assert.That(links.Single(link => link.Rel == "collection").PermissionFacts)
            .IsEqualTo(new EventResourceCollectionAuthorizationFacts(tenantId, resource.EventId));
    }

    [Test]
    [Arguments(EventResourcePublicationStateEnum.Draft, EventResourceDeliveryTypeEnum.StoredFile, true)]
    [Arguments(EventResourcePublicationStateEnum.Withdrawn, EventResourceDeliveryTypeEnum.StoredFile, true)]
    [Arguments(EventResourcePublicationStateEnum.Published, EventResourceDeliveryTypeEnum.StoredFile, true)]
    [Arguments(EventResourcePublicationStateEnum.Archived, EventResourceDeliveryTypeEnum.StoredFile, false)]
    [Arguments(EventResourcePublicationStateEnum.Draft, EventResourceDeliveryTypeEnum.ExternalLink, false)]
    public async Task FileUploadRequiresExactUpdateAuthorityAndMutableStoredFileIntent(
        EventResourcePublicationStateEnum state, EventResourceDeliveryTypeEnum delivery, bool expected)
    {
        Guid tenantId = Guid.CreateVersion7();
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var resource = Resource(state);
        resource = resource with { Draft = resource.Draft with { DeliveryType = delivery } };
        var uploads = new EventMaterialDetailLinkPolicy(tenant).GetLinks(resource, null)
            .Where(link => link.Rel == "upload-file").ToArray();
        await Assert.That(uploads.Length).IsEqualTo(expected ? 1 : 0);
        if (expected)
        {
            await Assert.That(uploads[0].PermissionAction).IsEqualTo("update");
            await Assert.That(uploads[0].PermissionFacts)
                .IsEqualTo(new EventResourceTargetAuthorizationFacts(tenantId, resource.Id));
            await Assert.That(uploads[0].RequiresAuth).IsTrue();
        }
    }

    [Test]
    public async Task ArchivedResourcesKeepReadAndDeleteButNeverMutableOrDeliveryAffordances()
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(Guid.CreateVersion7());
        var links = new EventMaterialDetailLinkPolicy(tenant)
            .GetLinks(Resource(EventResourcePublicationStateEnum.Archived), null).ToArray();
        await Assert.That(links.Any(link => link.Rel == "delete" && link.PermissionAction == "delete")).IsTrue();
        await Assert.That(links.Any(link => link.Rel is "edit" or "archive" or "unpublish" or "publish" or "download" or "access")).IsFalse();
    }

    [Test]
    public async Task CreationRequiresTrustedParentContextRatherThanAResourceOrClaim()
    {
        Guid tenantId = Guid.CreateVersion7(), eventId = Guid.CreateVersion7();
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var policy = new EventMaterialCollectionLinkPolicy(tenant);
        await Assert.That(policy.GetCollectionLinks(null, null)).IsEmpty();
        var create = policy.GetCollectionLinks(null, new EventMaterialCollectionAuthorizationContext(tenantId, eventId))
            .Single(link => link.Rel == "create-resource");
        await Assert.That(create.PermissionFacts).IsEqualTo(new EventResourceTargetAuthorizationFacts(tenantId, eventId));
        await Assert.That(create.PermissionAction).IsEqualTo("create");
        await Assert.That(create.RequiresAuth).IsTrue();
    }

    [Test]
    public async Task MetadataExportRequiresItsOwnExactParentAuthority()
    {
        Guid tenantId = Guid.CreateVersion7(), eventId = Guid.CreateVersion7();
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var policy = new EventMaterialCollectionLinkPolicy(tenant);
        var export = policy.GetCollectionLinks(null, new EventMaterialCollectionAuthorizationContext(tenantId, eventId))
            .Single(link => link.Rel == "export");
        await Assert.That(export.PermissionAction).IsEqualTo("export");
        await Assert.That(export.PermissionFacts).IsEqualTo(new EventResourceCollectionAuthorizationFacts(tenantId, eventId));
        await Assert.That(export.RequiresAuth).IsTrue();
        await Assert.That(policy.GetCollectionLinks(null, new EventMaterialCollectionAuthorizationContext(Guid.CreateVersion7(), eventId))).IsEmpty();
    }

    private static EventResourceManagementDto Resource(EventResourcePublicationStateEnum state) => new(
        Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), state,
        new EventResourceDraftDto
        {
            Title = "Private resource", Kind = EventResourceKindEnum.GeneralDocument,
            DisclosureMode = EventResourceDisclosureModeEnum.EligibleOnly,
            DeliveryType = EventResourceDeliveryTypeEnum.StoredFile
        }, DateTime.UtcNow, null);
}
