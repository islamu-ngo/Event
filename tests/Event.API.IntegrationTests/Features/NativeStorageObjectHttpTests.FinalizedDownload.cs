using System.Net;
using System.Net.Http.Json;
using Cerbos.Sdk;
using Cerbos.Sdk.Builder;
using Cerbos.Sdk.Response;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.StorageObjects.Requests.Queries;
using Explore.Application.Models.Storage;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Grpc.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.Responses;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeStorageObjectHttpTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    public async Task EvidencePdfPrivateDownloadRemainsBoundToStoredOwnerAfterFinalization(
        bool alsoTenantAdmin, bool revokeOrganizationGrant)
    {
        await using var factory = await StorageFactory.CreateAsync(useProductionAuthorization: true);
        var owner = await SeedFinalizationOwnerAsync(factory, alsoTenantAdmin);
        using var client = Client(factory, owner.UserId);
        var reserved = await ReserveEvidencePdfAsync(client, owner.OrganizationId);
        if (revokeOrganizationGrant)
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            db.OrganizationMembers.RemoveRange(await db.OrganizationMembers.Where(item => item.UserId == owner.UserId).ToListAsync());
            await db.SaveChangesAsync();
        }
        using var finalized = await PutAsync(client, reserved.Id, "%PDF-"u8.ToArray());
        await Assert.That(finalized.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var result = (await finalized.Content.ReadFromJsonAsync<BaseCommandResponse<StorageUploadSessionDto>>())!.Id!;
        using var download = await client.GetAsync($"{Root}/{result.StorageObjectId}/content");
        await Assert.That(download.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await download.Content.ReadAsByteArrayAsync()).IsEquivalentTo("%PDF-"u8.ToArray());
    }

    [Test]
    public async Task EvidenceDownloadKeepsReviewerPolicySeparateFromPrivateOwnerByteAccess()
    {
        await using var factory = await StorageFactory.CreateAsync(useProductionAuthorization: true);
        var owner = await SeedFinalizationOwnerAsync(factory);
        using var client = Client(factory, owner.UserId);
        Guid id = await FinalizeDownloadPdfAsync(client, owner.OrganizationId);
        using var other = Client(factory, factory.OwnerId);
        using (var forbidden = await other.GetAsync($"{Root}/{id}/content"))
            await Assert.That(forbidden.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await GrantCompatibilityAuthorityAsync(factory, factory.OwnerId, "tenant-admin");
        using (var scope = factory.Services.CreateScope())
        {
            var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            accessor.HttpContext = FinalizationPrincipal(factory.OwnerId);
            try
            {
                var query = new GetStorageObjectContentRequest { StorageObjectId = id, TenantId = factory.OtherTenantId };
                var resolved = await scope.ServiceProvider.GetRequiredService<AuthorizationResourceContextResolver>()
                    .ResolveAsync(query, ResourceKinds.StorageObject, AuthorizationActions.StorageObjects.Download, "forged", new StorageObjectCollectionAuthorizationFacts(factory.OtherTenantId), default);
                await Assert.That(resolved.ResourceId).IsEqualTo(id.ToString("D"));
                var facts = (PersistedStorageObjectAuthorizationFacts)resolved.Facts!;
                await Assert.That(facts.TenantId).IsEqualTo(owner.TenantId);
                await Assert.That(facts.CreatedBy).IsEqualTo(owner.UserId);
                await Assert.That(facts.Visibility).IsEqualTo(StorageObjectVisibilities.PrivateOwner);
                await Assert.That((await scope.ServiceProvider.GetRequiredService<IAuthorizationProvider>().AuthorizeAsync(
                    new AuthorizationRequest(ResourceKinds.StorageObject, resolved.ResourceId!, AuthorizationActions.StorageObjects.Download, Facts: facts))).IsAllowed).IsTrue();
            }
            finally { accessor.HttpContext = null; }
        }
        // The retained reader has always required the creator for PrivateOwner bytes, even after a policy grant.
        using (var unavailable = await other.GetAsync($"{Root}/{id}/content"))
            await Assert.That(unavailable.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        using var anonymous = factory.CreateClient();
        using (var denied = await anonymous.GetAsync($"{Root}/{id}/content"))
            await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        using (var notPublic = await anonymous.GetAsync($"{Root}/{id}/public"))
            await Assert.That(notPublic.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(factory.DisposedReads).IsEqualTo(0);
        using var ownerRead = await client.GetAsync($"{Root}/{id}/content");
        await Assert.That(ownerRead.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await ownerRead.Content.ReadAsByteArrayAsync()).IsEquivalentTo("%PDF-"u8.ToArray());
    }

    [Test]
    public async Task EvidenceDownloadCannotUseCallerFactsToCrossTenantOrInventAnObject()
    {
        await using var factory = await StorageFactory.CreateAsync(useProductionAuthorization: true);
        var owner = await SeedFinalizationOwnerAsync(factory);
        using var client = Client(factory, owner.UserId);
        Guid id = await FinalizeDownloadPdfAsync(client, owner.OrganizationId);
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(factory.OtherTenantId);
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = FinalizationPrincipal(owner.UserId);
        try
        {
            var query = new GetStorageObjectContentRequest { StorageObjectId = id, TenantId = owner.TenantId };
            var forged = new PersistedStorageObjectAuthorizationFacts(factory.OtherTenantId, id,
                StorageObjectVisibilities.PublicImage, StorageObjectLifecycleStates.Active, owner.UserId, null, null);
            var resolver = scope.ServiceProvider.GetRequiredService<AuthorizationResourceContextResolver>();
            var resolved = await resolver.ResolveAsync(query, ResourceKinds.StorageObject, AuthorizationActions.StorageObjects.Download, id.ToString("D"), forged, default);
            await Assert.That(resolved.Facts).IsNull();
            var handler = scope.ServiceProvider.GetRequiredService<IQueryHandler<GetStorageObjectContentRequest, StorageObjectContentResult?>>();
            await Assert.That(async () => await handler.QueryAsync(query, default)).Throws<Explore.Application.Exceptions.AuthorizationException>();
            var missing = await resolver.ResolveAsync(query with { StorageObjectId = Guid.CreateVersion7() }, ResourceKinds.StorageObject,
                AuthorizationActions.StorageObjects.Download, id.ToString("D"), forged, default);
            await Assert.That(missing.Facts).IsNull();
        }
        finally { accessor.HttpContext = null; }
        await Assert.That(factory.DisposedReads).IsEqualTo(0);
    }

    [Test]
    [Arguments("deleted", HttpStatusCode.Forbidden)]
    [Arguments("quarantined", HttpStatusCode.Forbidden)]
    [Arguments("delete_requested", HttpStatusCode.Forbidden)]
    [Arguments("pending", HttpStatusCode.Forbidden)]
    [Arguments("erased-owner", HttpStatusCode.Forbidden)]
    [Arguments("expired-retention", HttpStatusCode.NotFound)]
    public async Task EvidenceDownloadHonorsPersistedDeletionLifecycleAndPrivacyBoundaries(string state, HttpStatusCode expected)
    {
        await using var factory = await StorageFactory.CreateAsync(useProductionAuthorization: true);
        var owner = await SeedFinalizationOwnerAsync(factory);
        using var client = Client(factory, owner.UserId);
        Guid id = await FinalizeDownloadPdfAsync(client, owner.OrganizationId);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var document = await db.StorageObjects.SingleAsync(item => item.Id == id);
            switch (state)
            {
                case "deleted": document.IsDeleted = true; break;
                case "erased-owner": document.CreatedBy = null; break;
                case "expired-retention": document.RegistrationContentRetentionUntilUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc); break;
                default: document.LifecycleState = state; break;
            }
            await db.SaveChangesAsync();
        }
        using var denied = await client.GetAsync($"{Root}/{id}/content");
        await Assert.That(denied.StatusCode).IsEqualTo(expected);
        await Assert.That(await denied.Content.ReadAsStringAsync()).DoesNotContain("tenants/");
        await Assert.That(factory.DisposedReads).IsEqualTo(0);
    }

    [Test]
    public async Task EvidenceDownloadUsesPersistedPdfMetadataAndSanitizesDisposition()
    {
        await using var factory = await StorageFactory.CreateAsync(useProductionAuthorization: true);
        var owner = await SeedFinalizationOwnerAsync(factory);
        using var client = Client(factory, owner.UserId);
        Guid id = await FinalizeDownloadPdfAsync(client, owner.OrganizationId);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var document = await db.StorageObjects.SingleAsync(item => item.Id == id);
            document.SafeDisplayName = "../private.pdf";
            await db.SaveChangesAsync();
        }
        using var response = await client.GetAsync($"{Root}/{id}/content");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentType!.MediaType).IsEqualTo("application/pdf");
        await Assert.That(response.Content.Headers.ContentDisposition!.DispositionType).IsEqualTo("attachment");
        await Assert.That(response.Content.Headers.ContentDisposition.FileNameStar).IsEqualTo("download");
        await Assert.That(response.Content.Headers.ContentLength).IsEqualTo(5);
        await Assert.That(await response.Content.ReadAsByteArrayAsync()).IsEquivalentTo("%PDF-"u8.ToArray());
    }

    [Test]
    [Arguments(StorageObjectPurposes.LegacyImage)]
    [Arguments(StorageObjectPurposes.ProfileImage)]
    [Arguments(StorageObjectPurposes.EventImage)]
    [Arguments(StorageObjectPurposes.Attachment)]
    [Arguments(StorageObjectPurposes.Document)]
    [Arguments(StorageObjectPurposes.SystemAsset)]
    public async Task ExactDownloadRetainsVisibilityAndOwnerRulesAcrossStoragePurposes(string purpose)
    {
        await using var factory = await StorageFactory.CreateAsync(useProductionAuthorization: true);
        Guid otherUserId;
        using (var scope = factory.Services.CreateScope())
            otherUserId = (await TenantScenarioSeed.SeedActiveTenantWithUserAsync(scope.ServiceProvider.GetRequiredService<ExploreDbContext>())).UserId;
        bool image = SafeRasterContentPolicy.IsImagePurpose(purpose);
        byte[] bytes = image ? CompatibilityPng : "%PDF-"u8.ToArray();
        Guid id = Guid.CreateVersion7();
        string key = $"tenants/{PlatformDefaults.DefaultTenantId:N}/{id:N}";
        factory.Objects.Add(key, bytes);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            db.StorageObjects.Add(new StorageObject
            {
                Id = id, TenantId = PlatformDefaults.DefaultTenantId, Tenant = null!, CreatedBy = factory.OwnerId,
                FileTypeId = image ? (int)FileTypeEnum.Image : (int)FileTypeEnum.Document, FileType = null!,
                FullName = "stored", SafeDisplayName = image ? "image.png" : "document.pdf", Uri = string.Empty,
                ObjectKey = key, Provider = StorageProviders.Local, Purpose = purpose, Size = bytes.Length,
                ContentType = image ? "image/png" : "application/pdf", Extension = image ? "png" : "pdf",
                Visibility = image ? StorageObjectVisibilities.PublicImage : StorageObjectVisibilities.AuthenticatedTenant,
                LifecycleState = StorageObjectLifecycleStates.Active
            });
            await db.SaveChangesAsync();
        }
        using var other = Client(factory, otherUserId);
        using (var allowed = await other.GetAsync($"{Root}/{id}/content"))
        {
            await Assert.That(allowed.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(await allowed.Content.ReadAsByteArrayAsync()).IsEquivalentTo(bytes);
        }
        using var anonymous = factory.CreateClient();
        using (var protectedRead = await anonymous.GetAsync($"{Root}/{id}/content"))
            await Assert.That(protectedRead.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        using (var publicRead = await anonymous.GetAsync($"{Root}/{id}/public"))
            await Assert.That(publicRead.StatusCode).IsEqualTo(image ? HttpStatusCode.OK : HttpStatusCode.NotFound);
        int reads = factory.DisposedReads;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            (await db.StorageObjects.SingleAsync(item => item.Id == id)).Visibility = StorageObjectVisibilities.PrivateOwner;
            await db.SaveChangesAsync();
        }
        using (var denied = await other.GetAsync($"{Root}/{id}/content"))
            await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(factory.DisposedReads).IsEqualTo(reads);
        using var ownerClient = Client(factory, factory.OwnerId);
        using var ownerRead = await ownerClient.GetAsync($"{Root}/{id}/content");
        await Assert.That(ownerRead.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await ownerRead.Content.ReadAsByteArrayAsync()).IsEquivalentTo(bytes);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task ExactDownloadKeepsSelectedCerbosAuthorityAndSendsOnlyPersistedFacts(bool byo, bool deny)
    {
        await using var factory = await StorageFactory.CreateAsync(useProductionAuthorization: true);
        var owner = await SeedFinalizationOwnerAsync(factory);
        using var ownerClient = Client(factory, owner.UserId);
        Guid id = await FinalizeDownloadPdfAsync(ownerClient, owner.OrganizationId);
        var grpc = Substitute.For<ICerbosClient>();
        var grpcFactory = Substitute.For<ICerbosClientFactory>();
        grpcFactory.GetOrCreate(Arg.Any<string>()).Returns(grpc);
        var observed = new List<Cerbos.Api.V1.Request.CheckResourcesRequest>();
        grpc.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>()).Returns(call =>
        {
            var request = (call.Arg<CheckResourcesRequest>() ?? throw new InvalidOperationException("Missing PDP request.")).ToCheckResourcesRequest();
            observed.Add(request);
            var response = new Cerbos.Api.V1.Response.CheckResourcesResponse();
            foreach (var resource in request.Resources)
            {
                var result = new Cerbos.Api.V1.Response.CheckResourcesResponse.Types.ResultEntry
                {
                    Resource = new() { Id = resource.Resource.Id, Kind = resource.Resource.Kind }
                };
                foreach (string action in resource.Actions)
                {
                    bool allowed = !deny && resource.Resource.Kind == ResourceKinds.StorageObject && action == AuthorizationActions.StorageObjects.Download
                        && request.Principal.Roles.Contains("islamuevent_authenticated_user")
                        && resource.Resource.Attr["lifecycleState"].StringValue == StorageObjectLifecycleStates.Active
                        && resource.Resource.Attr["visibility"].StringValue == StorageObjectVisibilities.PrivateOwner
                        && resource.Resource.Attr["createdBy"].StringValue == request.Principal.Attr["userId"].StringValue;
                    result.Actions.Add(action, allowed ? Cerbos.Api.V1.Effect.Effect.Allow : Cerbos.Api.V1.Effect.Effect.Deny);
                }
                response.Results.Add(result);
            }
            return new CheckResourcesResponse(response);
        });
        await using var selected = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICerbosClient>(); services.AddSingleton(grpc);
            services.RemoveAll<ICerbosClientFactory>(); services.AddSingleton(grpcFactory);
            services.PostConfigure<AuthorizationProviderDeploymentOptions>(options => options.Provider = "cerbos");
        }));
        if (byo)
        {
            using var scope = selected.Services.CreateScope();
            await ConfigureCompatibilityByoAsync(scope.ServiceProvider, owner.UserId);
        }
        using var client = selected.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(owner.UserId));
        using var response = await client.GetAsync($"{Root}/{id}/content");
        await Assert.That(response.StatusCode).IsEqualTo(deny ? HttpStatusCode.Forbidden : HttpStatusCode.OK);
        var resource = observed.Single().Resources.Single().Resource;
        await Assert.That(resource.Id).IsEqualTo(id.ToString("D"));
        await Assert.That(resource.Attr["tenantId"].StringValue).IsEqualTo(owner.TenantId.ToString("D"));
        await Assert.That(resource.Attr["createdBy"].StringValue).IsEqualTo(owner.UserId.ToString("D"));
        await Assert.That(resource.Attr.ContainsKey("objectKey")).IsFalse();
        await Assert.That(resource.Attr.ContainsKey("uri")).IsFalse();
        await Assert.That(factory.DisposedReads).IsEqualTo(deny ? 0 : 1);
        if (!deny) await Assert.That(await response.Content.ReadAsByteArrayAsync()).IsEquivalentTo("%PDF-"u8.ToArray());
    }

    private static async Task<Guid> FinalizeDownloadPdfAsync(HttpClient client, Guid organizationId)
    {
        var reserved = await ReserveEvidencePdfAsync(client, organizationId);
        using var response = await PutAsync(client, reserved.Id, "%PDF-"u8.ToArray());
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<BaseCommandResponse<StorageUploadSessionDto>>())!.Id!.StorageObjectId!.Value;
    }
}
