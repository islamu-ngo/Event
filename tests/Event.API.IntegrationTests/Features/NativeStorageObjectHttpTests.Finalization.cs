using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.StorageObjects.Requests.Commands;
using Explore.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Event.Api.IntegrationTests.Seeds;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.OrganizationTenantEvidence;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
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
    public async Task EvidencePdfFinalizationBelongsToReservationOwnerAndReplaysWithoutWritingAgain(
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
        await Assert.That(result.Status).IsEqualTo(StorageUploadSessionStates.Finalized);
        await Assert.That(result.UsedBytes).IsEqualTo(5);
        await Assert.That(result.TotalReservedBytes).IsEqualTo(0);
        await Assert.That(await finalized.Content.ReadAsStringAsync()).DoesNotContain("tenants/");
        using var replay = await PutAsync(client, reserved.Id, "%PDF-"u8.ToArray());
        await Assert.That(replay.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await replay.Content.ReadFromJsonAsync<BaseCommandResponse<StorageUploadSessionDto>>())!.Id!.StorageObjectId)
            .IsEqualTo(result.StorageObjectId);
        await Assert.That(factory.Objects.Values.Single()).IsEquivalentTo("%PDF-"u8.ToArray());
        await Assert.That(factory.WriteCount).IsEqualTo(1);
        await Assert.That(factory.Objects.Count).IsEqualTo(1);
        await Assert.That(factory.Signings).IsEmpty();
    }

    [Test]
    public async Task EvidencePdfForeignUserTenantAndMissingSessionCannotWriteOrReleaseReservation()
    {
        await using var factory = await StorageFactory.CreateAsync(useProductionAuthorization: true);
        var owner = await SeedFinalizationOwnerAsync(factory, alsoTenantAdmin: true);
        using var client = Client(factory, owner.UserId);
        using var stranger = Client(factory, factory.OwnerId);
        var reserved = await ReserveEvidencePdfAsync(client, owner.OrganizationId);
        using (var denied = await PutAsync(stranger, reserved.Id, "%PDF-"u8.ToArray()))
            await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        using (var missing = await PutAsync(client, Guid.CreateVersion7(), "%PDF-"u8.ToArray()))
            await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(factory.OtherTenantId);
            var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            accessor.HttpContext = FinalizationPrincipal(owner.UserId);
            try
            {
                using var bytes = new MemoryStream("%PDF-"u8.ToArray());
                var command = new FinalizeStorageUploadSessionCommand
                {
                    UploadSessionId = reserved.Id,
                    Content = bytes,
                    TenantId = owner.TenantId
                };
                var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<FinalizeStorageUploadSessionCommand, BaseCommandResponse<StorageUploadSessionDto>>>();
                await Assert.That(async () => await handler.ExecuteAsync(command, default))
                    .Throws<Explore.Application.Exceptions.AuthorizationException>();
            }
            finally { accessor.HttpContext = null; }
        }
        await AssertFinalizationReservationAsync(factory, reserved.Id, StorageUploadSessionStates.Reserved, 5);
        await Assert.That(factory.WriteCount).IsEqualTo(0);
        await Assert.That(factory.Objects).IsEmpty();
    }

    [Test]
    [Arguments("expired", FailureCodes.StorageUploadSessionExpired, 0L)]
    [Arguments("canceled", FailureCodes.StorageUploadSessionInvalidState, 0L)]
    [Arguments("failed", FailureCodes.StorageUploadSessionInvalidState, 0L)]
    [Arguments("uploading", FailureCodes.StorageUploadSessionInvalidState, 5L)]
    public async Task EvidencePdfFinalizationRetainsHandlerStateAndExpiryChecks(string state, string code, long remaining)
    {
        await using var factory = await StorageFactory.CreateAsync(useProductionAuthorization: true);
        var owner = await SeedFinalizationOwnerAsync(factory);
        using var client = Client(factory, owner.UserId);
        var reserved = await ReserveEvidencePdfAsync(client, owner.OrganizationId);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var session = await db.StorageUploadSessions.SingleAsync(item => item.Id == reserved.Id);
            if (state == "expired") session.ExpiresAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            else session.Status = state;
            if (state is "canceled" or "failed")
            {
                var counter = await db.StorageUsageCounters.SingleAsync();
                counter.ReleaseReservation(session.ReservedBytes);
            }
            await db.SaveChangesAsync();
        }
        using var response = await PutAsync(client, reserved.Id, "%PDF-"u8.ToArray());
        await ProblemAsync(response, HttpStatusCode.Conflict, code);
        await AssertFinalizationReservationAsync(factory, reserved.Id, state, remaining);
        await Assert.That(factory.WriteCount).IsEqualTo(0);
        await Assert.That(factory.Objects).IsEmpty();
    }

    [Test]
    public async Task EvidencePdfTransportMetadataCannotChangeReservedSizeOrContentType()
    {
        await using var factory = await StorageFactory.CreateAsync(useProductionAuthorization: true);
        var owner = await SeedFinalizationOwnerAsync(factory);
        using var client = Client(factory, owner.UserId);
        var reserved = await ReserveEvidencePdfAsync(client, owner.OrganizationId);
        using (var wrongSize = await PutAsync(client, reserved.Id, "%PDF-X"u8.ToArray()))
            await ProblemAsync(wrongSize, HttpStatusCode.BadRequest, FailureCodes.StorageUploadSizeMismatch);
        using var content = new ByteArrayContent("%PDF-"u8.ToArray());
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        using (var wrongType = await client.PutAsync($"{Root}/upload-sessions/{reserved.Id}/content", content))
            await ProblemAsync(wrongType, HttpStatusCode.BadRequest, FailureCodes.StorageUploadContentTypeMismatch);
        await AssertFinalizationReservationAsync(factory, reserved.Id, StorageUploadSessionStates.Reserved, 5);
        await Assert.That(factory.WriteCount).IsEqualTo(0);
        using var accepted = await PutAsync(client, reserved.Id, "%PDF-"u8.ToArray());
        await Assert.That(accepted.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(factory.WriteCount).IsEqualTo(1);
    }

    [Test]
    public async Task EvidencePdfInvalidContentAndPrivacyFenceRemainAuthoritative()
    {
        await using var factory = await StorageFactory.CreateAsync(useProductionAuthorization: true);
        var owner = await SeedFinalizationOwnerAsync(factory);
        using var client = Client(factory, owner.UserId);
        var invalid = await ReserveEvidencePdfAsync(client, owner.OrganizationId);
        using (var rejected = await PutAsync(client, invalid.Id, "wrong"u8.ToArray()))
            await ProblemAsync(rejected, HttpStatusCode.BadRequest, FailureCodes.StorageUploadContentSignatureMismatch);
        await AssertFinalizationReservationAsync(factory, invalid.Id, StorageUploadSessionStates.Failed, 0);
        var fenced = await ReserveEvidencePdfAsync(client, owner.OrganizationId);
        using (var scope = factory.Services.CreateScope())
        {
            var now = DateTime.UtcNow;
            var intent = PrivacyErasureIntent.Record(Guid.CreateVersion7(), 1, PrivacyErasureSubjectKind.User,
                owner.UserId, PrivacyErasureReasonCode.AccountDeletion, 1, now, now);
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            db.PrivacyErasureSagas.Add(PrivacyErasureSaga.Start(intent, 1, SHA256.HashData("receipt"u8), now.AddHours(1), now));
            await db.SaveChangesAsync();
        }
        using var response = await PutAsync(client, fenced.Id, "%PDF-"u8.ToArray());
        await ProblemAsync(response, HttpStatusCode.BadRequest, "privacy_erasure_fenced");
        await AssertFinalizationReservationAsync(factory, fenced.Id, StorageUploadSessionStates.Reserved, 5);
        await Assert.That(factory.WriteCount).IsEqualTo(0);
        await Assert.That(factory.Objects).IsEmpty();
    }

    [Test]
    public async Task EvidencePdfAuthorizationLoadsPersistedFactsAndRejectsForgedSingleAndBatchChecks()
    {
        await using var factory = await StorageFactory.CreateAsync(useProductionAuthorization: true);
        var owner = await SeedFinalizationOwnerAsync(factory, alsoTenantAdmin: true);
        using var client = Client(factory, owner.UserId);
        var reserved = await ReserveEvidencePdfAsync(client, owner.OrganizationId);
        using var scope = factory.Services.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = FinalizationPrincipal(owner.UserId);
        try
        {
            using var content = new MemoryStream("%PDF-"u8.ToArray());
            var command = new FinalizeStorageUploadSessionCommand
            {
                UploadSessionId = reserved.Id,
                TenantId = factory.OtherTenantId,
                Content = content,
                ContentType = "text/plain",
                ContentLength = 999
            };
            var resolver = scope.ServiceProvider.GetRequiredService<AuthorizationResourceContextResolver>();
            var resolved = await resolver.ResolveAsync(command, ResourceKinds.StorageObject, AuthorizationActions.Create,
                "forged-resource", new StorageObjectCollectionAuthorizationFacts(factory.OtherTenantId), default);
            var facts = (StorageUploadFinalizationFacts)resolved.Facts!;
            await Assert.That(resolved.ResourceId).IsEqualTo(reserved.Id.ToString("D"));
            await Assert.That(facts.OwnerUserId).IsEqualTo(owner.UserId);
            await Assert.That(facts.TenantId).IsEqualTo(owner.TenantId);
            await Assert.That(facts.ContentType).IsEqualTo("application/pdf");
            await Assert.That(facts.ExpectedSizeBytes).IsEqualTo(5);
            await Assert.That(scope.ServiceProvider.GetRequiredService<ExploreDbContext>().ChangeTracker.Entries<StorageUploadSession>()).IsEmpty();
            var provider = scope.ServiceProvider.GetRequiredService<FallbackAuthorizationService>();
            var valid = new AuthorizationRequest(AuthorizationCapabilityCatalog.Require(ResourceKinds.StorageObject, AuthorizationActions.Create),
                reserved.Id.ToString("D"), Facts: facts);
            await Assert.That((await provider.AuthorizeAsync(valid)).IsAllowed).IsTrue();
            await Assert.That((await provider.AuthorizeBatchAsync([valid])).Single().IsAllowed).IsTrue();
            AuthorizationRequest[] invalid =
            [
                valid with { ResourceId = nameof(CreateStorageUploadSessionCommand) },
                valid with { ResourceId = Guid.CreateVersion7().ToString("D") },
                valid with { ResourceId = "malformed" },
                valid with { Facts = null },
                valid with { Capability = AuthorizationCapabilityCatalog.Require(ResourceKinds.StorageObject, AuthorizationActions.StorageObjects.Download) },
                valid with { Facts = new StorageObjectCollectionAuthorizationFacts(owner.TenantId) },
                valid with { Facts = facts with { UploadSessionId = Guid.Empty } },
                valid with { Facts = facts with { TenantId = factory.OtherTenantId } },
                valid with { Facts = facts with { OwnerUserId = factory.OwnerId } },
                valid with { Facts = facts with { OwnerUserId = Guid.Empty } },
                valid with { Facts = facts with { Purpose = StorageObjectPurposes.Attachment } },
                valid with { Facts = facts with { Visibility = StorageObjectVisibilities.PublicImage } },
                valid with { Facts = facts with { OwningResourceKind = null } },
                valid with { Facts = facts with { OwningResourceId = Guid.Empty } },
                valid with { Facts = facts with { ContentType = "image/svg+xml" } },
                valid with { Facts = facts with { Extension = "txt" } },
                valid with { Facts = facts with { ExpectedSizeBytes = 0 } }
            ];
            foreach (var check in invalid) await Assert.That((await provider.AuthorizeAsync(check)).IsAllowed).IsFalse();
            await Assert.That((await provider.AuthorizeBatchAsync(invalid)).All(result => !result.IsAllowed)).IsTrue();
        }
        finally { accessor.HttpContext = null; }
        await AssertFinalizationReservationAsync(factory, reserved.Id, StorageUploadSessionStates.Reserved, 5);
        await Assert.That(factory.WriteCount).IsEqualTo(0);
    }

    private static DefaultHttpContext FinalizationPrincipal(Guid userId) => new()
    {
        User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId.ToString("D"))], "Test"))
    };

    private static async Task AssertFinalizationReservationAsync(StorageFactory factory, Guid id, string state, long reserved)
    {
        using var scope = factory.Services.CreateScope();
        var session = await scope.ServiceProvider.GetRequiredService<IStorageUploadSessionRepository>().GetForAuthorizationAsync(id, default);
        await Assert.That(session!.Status).IsEqualTo(state);
        var counters = await scope.ServiceProvider.GetRequiredService<IStorageUsageCounterRepository>().GetByTenantAsync(PlatformDefaults.DefaultTenantId, default);
        await Assert.That(counters.Single().ReservedBytes).IsEqualTo(reserved);
    }

    private static async Task<TenantScenarioSeed.TenantOrganizationScenarioResult> SeedFinalizationOwnerAsync(
        StorageFactory factory, bool alsoTenantAdmin = false)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var owner = await TenantScenarioSeed.SeedActiveTenantWithOrganizationPublisherAsync(db);
        var participation = await db.OrganizationTenants.SingleAsync(item => item.OrganizationId == owner.OrganizationId);
        participation.ApprovalStatusId = (int)ApprovalStatusEnum.Pending;
        participation.ApprovedAt = null;
        if (alsoTenantAdmin)
        {
            var member = await db.TenantUsers.SingleAsync(item => item.UserId == owner.UserId);
            db.TenantUserRoleGrants.Add(new TenantUserRoleGrant
            {
                Id = Guid.CreateVersion7(),
                TenantId = owner.TenantId,
                Tenant = null!,
                TenantUserId = member.Id,
                TenantUser = member,
                RoleId = (int)RoleEnum.TenantAdmin,
                Role = null!,
                RoleScopeId = (int)RoleScopeEnum.Tenant
            });
        }
        await db.SaveChangesAsync();
        return owner;
    }

    private static async Task<StorageUploadSessionDto> ReserveEvidencePdfAsync(HttpClient client, Guid organizationId)
    {
        using var response = await client.PostAsJsonAsync($"/api/organizations/{organizationId}/legitimacy-evidence/upload-session",
            new CreateOrganizationTenantEvidenceUploadSessionDto
            {
                FileName = "evidence.pdf",
                ContentType = "application/pdf",
                ExpectedSizeBytes = 5
            });
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<BaseCommandResponse<StorageUploadSessionDto>>())!.Id!;
    }
}
