using System.Data;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Event.Api.IntegrationTests.Seeds;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.OrganizationTenantEvidence;
using Explore.Application.Features.OrganizationTenantEvidence.Requests.Commands;
using Explore.Application.Features.OrganizationTenantEvidence.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Operations;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeStorageObjectHttpTests
{
    [Test]
    [Arguments(OrganizationTenantEvidenceReviewDecisionDto.Approve, ApprovalStatusEnum.Approved)]
    [Arguments(OrganizationTenantEvidenceReviewDecisionDto.Reject, ApprovalStatusEnum.Rejected)]
    public async Task EvidenceReviewAndPrivateReadbackPreserveAuthorityAndTerminalState(
        OrganizationTenantEvidenceReviewDecisionDto decision, ApprovalStatusEnum expected)
    {
        await using var factory = await StorageFactory.CreateAsync(useProductionAuthorization: true);
        var scenario = await SeedEvidenceAsync(factory);
        using var owner = Client(factory, scenario.UserId);
        using var reviewer = Client(factory, factory.OwnerId);
        using var anonymous = factory.CreateClient();
        string root = EvidenceRoot(scenario.OrganizationId);
        Guid documentId = await SeedEvidenceDocumentAsync(factory, scenario);
        var submission = new SubmitOrganizationTenantEvidenceDto { DocumentStorageObjectId = documentId };
        Guid evidenceId = await SeedPendingEvidenceAsync(factory, scenario, documentId);
        string detailPath = $"{root}/{evidenceId}";
        var pending = await ReadEvidenceAsync(reviewer, detailPath, canReview: true);
        await Assert.That(pending.ReviewStatusId).IsEqualTo((int)ApprovalStatusEnum.Pending);
        await Assert.That(pending.DocumentStorageObjectId).IsEqualTo(documentId);
        await Assert.That(pending.DocumentDisplayName).IsEqualTo("evidence.pdf");
        await Assert.That(pending.ReviewedAt).IsNull();
        await ReadEvidenceAsync(reviewer, detailPath, canReview: true);
        await AssertEvidenceCountAsync(owner, root, 1);
        using (var denied = await anonymous.GetAsync(detailPath))
            await AssertEvidenceProblemAsync(denied, HttpStatusCode.Unauthorized);
        using (var denied = await anonymous.GetAsync(root))
            await AssertEvidenceProblemAsync(denied, HttpStatusCode.Unauthorized);
        using (var denied = await reviewer.PostAsJsonAsync(root, submission))
            await AssertEvidenceProblemAsync(denied, HttpStatusCode.Forbidden);

        var review = new ReviewOrganizationTenantEvidenceDto
        {
            Decision = decision,
            ExpectedConcurrencyStamp = pending.ConcurrencyStamp,
            Notes = "  verified document  "
        };
        using (var denied = await owner.PostAsJsonAsync(detailPath + "/review", review))
            await AssertEvidenceProblemAsync(denied, HttpStatusCode.Forbidden);
        using (var invalid = await reviewer.PostAsJsonAsync(detailPath + "/review", review with { Decision = (OrganizationTenantEvidenceReviewDecisionDto)99 }))
            await AssertEvidenceProblemAsync(invalid, HttpStatusCode.BadRequest);
        using (var stale = await reviewer.PostAsJsonAsync(detailPath + "/review", review with { ExpectedConcurrencyStamp = Guid.CreateVersion7() }))
            await AssertEvidenceProblemAsync(stale, HttpStatusCode.BadRequest);
        await Assert.That((await ReadEvidenceAsync(reviewer, detailPath, true)).ReviewStatusId).IsEqualTo((int)ApprovalStatusEnum.Pending);
        using (var applied = await reviewer.PostAsJsonAsync(detailPath + "/review", review))
            await Assert.That(applied.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var reviewed = await ReadEvidenceAsync(reviewer, detailPath, canReview: false);
        await Assert.That(reviewed.ReviewStatusId).IsEqualTo((int)expected);
        await Assert.That(reviewed.ReviewNotes).IsEqualTo("verified document");
        await Assert.That(reviewed.ReviewedAt).IsNotNull();
        await Assert.That(reviewed.ConcurrencyStamp).IsNotEqualTo(pending.ConcurrencyStamp);
        using (var replay = await reviewer.PostAsJsonAsync(detailPath + "/review", review))
            await Assert.That(replay.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using (var terminal = await reviewer.PostAsJsonAsync(detailPath + "/review", review with
        {
            Decision = decision == OrganizationTenantEvidenceReviewDecisionDto.Approve
                ? OrganizationTenantEvidenceReviewDecisionDto.Reject : OrganizationTenantEvidenceReviewDecisionDto.Approve,
            ExpectedConcurrencyStamp = reviewed.ConcurrencyStamp
        }))
            await AssertEvidenceProblemAsync(terminal, HttpStatusCode.BadRequest);
        await Assert.That((await ReadEvidenceAsync(owner, detailPath, false)).ConcurrencyStamp).IsEqualTo(reviewed.ConcurrencyStamp);
        // Evidence approval must not approve the Organization's participation.
        using var blocked = await owner.PostAsJsonAsync(root + "/upload-session", EvidenceUpload());
        await Assert.That(blocked.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await AssertEvidenceCountAsync(reviewer, root, 1);
        await Assert.That(factory.WriteCount).IsEqualTo(0);
        await Assert.That(factory.Signings).IsEmpty();
        await factory.Services.ValidateNativeOperationsDeepAsync();
    }

    [Test]
    public async Task EvidenceOwnerCannotAdvertiseTenantReviewAuthority()
    {
        await using var factory = await StorageFactory.CreateAsync(useProductionAuthorization: true);
        var scenario = await SeedEvidenceAsync(factory);
        using var owner = Client(factory, scenario.UserId);
        Guid documentId = await SeedEvidenceDocumentAsync(factory, scenario);
        Guid id = await SeedPendingEvidenceAsync(factory, scenario, documentId);
        await ReadEvidenceAsync(owner, $"{EvidenceRoot(scenario.OrganizationId)}/{id}", canReview: false);
    }

    [Test]
    public async Task EvidenceForeignAndWrongOrganizationReadsAndWritesDoNotDiscloseOrMutate()
    {
        await using var factory = await StorageFactory.CreateAsync(useProductionAuthorization: true);
        var scenario = await SeedEvidenceAsync(factory);
        TenantScenarioSeed.TenantOrganizationScenarioResult other;
        using (var scope = factory.Services.CreateScope())
            other = await TenantScenarioSeed.SeedActiveTenantWithOrganizationPublisherAsync(scope.ServiceProvider.GetRequiredService<ExploreDbContext>());
        using var owner = Client(factory, scenario.UserId);
        using var stranger = Client(factory, other.UserId);
        using var reviewer = Client(factory, factory.OwnerId);
        string root = EvidenceRoot(scenario.OrganizationId);
        Guid documentId = await SeedEvidenceDocumentAsync(factory, scenario);
        Guid id = await SeedPendingEvidenceAsync(factory, scenario, documentId);
        var detail = await ReadEvidenceAsync(reviewer, $"{root}/{id}", true);
        using (var denied = await stranger.GetAsync(root))
            await AssertEvidenceProblemAsync(denied, HttpStatusCode.Forbidden);
        using (var denied = await stranger.GetAsync($"{root}/{id}"))
            await AssertEvidenceProblemAsync(denied, HttpStatusCode.Forbidden);
        using (var denied = await stranger.PostAsJsonAsync(root, new SubmitOrganizationTenantEvidenceDto { DocumentStorageObjectId = documentId }))
            await AssertEvidenceProblemAsync(denied, HttpStatusCode.Forbidden);
        using (var missing = await reviewer.GetAsync($"{EvidenceRoot(other.OrganizationId)}/{id}"))
            await AssertEvidenceProblemAsync(missing, HttpStatusCode.NotFound);
        using (var missing = await reviewer.GetAsync($"{root}/{Guid.CreateVersion7()}"))
            await AssertEvidenceProblemAsync(missing, HttpStatusCode.NotFound);
        using (var wrong = await reviewer.PostAsJsonAsync($"{EvidenceRoot(other.OrganizationId)}/{id}/review", new ReviewOrganizationTenantEvidenceDto
        {
            Decision = OrganizationTenantEvidenceReviewDecisionDto.Approve,
            ExpectedConcurrencyStamp = detail.ConcurrencyStamp
        }))
            await AssertEvidenceProblemAsync(wrong, HttpStatusCode.BadRequest);
        await AssertEvidenceCountAsync(reviewer, EvidenceRoot(Guid.CreateVersion7()), 0);
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(factory.OtherTenantId);
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var actor = await db.Actors.SingleAsync(item => item.UserId == factory.OwnerId);
            var foreignUser = new TenantUser
            {
                Id = Guid.CreateVersion7(),
                TenantId = factory.OtherTenantId,
                Tenant = null!,
                UserId = factory.OwnerId,
                User = null!,
                ActorId = actor.Id,
                Actor = actor,
                StatusId = (int)TenantUserStatusEnum.Active,
                JoinedAt = DateTime.UtcNow
            };
            db.TenantUsers.Add(foreignUser);
            var participation = new OrganizationTenant
            {
                Id = Guid.CreateVersion7(),
                TenantId = factory.OtherTenantId,
                Tenant = null!,
                OrganizationId = scenario.OrganizationId,
                Organization = null!,
                ApprovalStatusId = (int)ApprovalStatusEnum.Pending,
                ApprovalStatus = null!
            };
            db.OrganizationTenants.Add(participation);
            db.OrganizationMembers.Add(new OrganizationMember
            {
                Id = Guid.CreateVersion7(),
                TenantId = factory.OtherTenantId,
                Tenant = null!,
                OrganizationTenantId = participation.Id,
                OrganizationTenant = participation,
                UserId = foreignUser.UserId,
                User = null!,
                RoleId = (int)RoleEnum.OrgAdmin,
                Role = null!
            });
            db.TenantUserRoleGrants.Add(new TenantUserRoleGrant
            {
                Id = Guid.CreateVersion7(),
                TenantId = factory.OtherTenantId,
                Tenant = null!,
                TenantUserId = foreignUser.Id,
                TenantUser = foreignUser,
                RoleId = (int)RoleEnum.TenantAdmin,
                Role = null!,
                RoleScopeId = (int)RoleScopeEnum.Tenant
            });
            await db.SaveChangesAsync();
            var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            accessor.HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", foreignUser.UserId.ToString())], "Test"))
            };
            try
            {
                // Real grants in a second tenant still cannot expose or attach the first tenant's document.
                var query = scope.ServiceProvider.GetRequiredService<IQueryHandler<GetOrganizationTenantEvidenceRequest, OrganizationTenantEvidenceDto?>>();
                await Assert.That(await query.QueryAsync(new(scenario.OrganizationId, id), default)).IsNull();
                var collection = scope.ServiceProvider.GetRequiredService<IQueryHandler<GetOrganizationTenantEvidenceCollectionRequest, IReadOnlyList<OrganizationTenantEvidenceDto>>>();
                await Assert.That(await collection.QueryAsync(new(scenario.OrganizationId), default)).IsEmpty();
                var submit = scope.ServiceProvider.GetRequiredService<ICommandHandler<SubmitOrganizationTenantEvidenceCommand, BaseCommandResponse<Guid>>>();
                await Assert.That((await submit.ExecuteAsync(new()
                {
                    OrganizationId = scenario.OrganizationId,
                    Evidence = new() { DocumentStorageObjectId = documentId }
                }, default)).IsSuccess).IsFalse();
                var review = scope.ServiceProvider.GetRequiredService<ICommandHandler<ReviewOrganizationTenantEvidenceCommand, BaseCommandResponse<Guid>>>();
                await Assert.That((await review.ExecuteAsync(new()
                {
                    OrganizationId = scenario.OrganizationId,
                    EvidenceId = id,
                    Review = new() { Decision = OrganizationTenantEvidenceReviewDecisionDto.Approve, ExpectedConcurrencyStamp = detail.ConcurrencyStamp }
                }, default)).IsSuccess).IsFalse();
            }
            finally { accessor.HttpContext = null; }
        }
        await Assert.That((await ReadEvidenceAsync(reviewer, $"{root}/{id}", true)).ConcurrencyStamp).IsEqualTo(detail.ConcurrencyStamp);
        await AssertEvidenceCountAsync(owner, root, 1);
    }

    [Test]
    [Arguments("purpose")]
    [Arguments("visibility")]
    [Arguments("owner-kind")]
    [Arguments("owner-id")]
    [Arguments("lifecycle")]
    [Arguments("deleted")]
    [Arguments("file-type")]
    [Arguments("object-key")]
    public async Task EvidenceSubmissionRejectsIneligibleDocumentsWithoutAttaching(string defect)
    {
        await using var factory = await StorageFactory.CreateAsync(useProductionAuthorization: true);
        var scenario = await SeedEvidenceAsync(factory);
        using var owner = Client(factory, scenario.UserId);
        string root = EvidenceRoot(scenario.OrganizationId);
        Guid documentId = await SeedEvidenceDocumentAsync(factory, scenario);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var document = await db.StorageObjects.SingleAsync(item => item.Id == documentId);
            switch (defect)
            {
                case "purpose": document.Purpose = StorageObjectPurposes.Attachment; break;
                case "visibility": document.Visibility = StorageObjectVisibilities.AuthenticatedTenant; break;
                case "owner-kind": document.OwningResourceKind = "organization"; break;
                case "owner-id": document.OwningResourceId = Guid.CreateVersion7(); break;
                case "lifecycle": document.LifecycleState = StorageObjectLifecycleStates.Quarantined; break;
                case "deleted": document.IsDeleted = true; break;
                case "file-type": document.FileTypeId = (int)FileTypeEnum.Image; break;
                case "object-key": document.ObjectKey = null; break;
                default: throw new ArgumentOutOfRangeException(nameof(defect));
            }
            await db.SaveChangesAsync();
        }
        using var denied = await owner.PostAsJsonAsync(root, new SubmitOrganizationTenantEvidenceDto { DocumentStorageObjectId = documentId });
        await AssertEvidenceProblemAsync(denied, HttpStatusCode.BadRequest);
        await AssertEvidenceCountAsync(owner, root, 0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task EvidenceRequiresPendingActiveParticipationForUploadAndSubmission(bool suspended)
    {
        await using var factory = await StorageFactory.CreateAsync(useProductionAuthorization: true);
        var scenario = await SeedEvidenceAsync(factory);
        using var owner = Client(factory, scenario.UserId);
        string root = EvidenceRoot(scenario.OrganizationId);
        Guid documentId = await SeedEvidenceDocumentAsync(factory, scenario);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var participation = await db.OrganizationTenants.SingleAsync(item => item.OrganizationId == scenario.OrganizationId);
            participation.IsSuspended = suspended;
            if (!suspended) participation.ApprovalStatusId = (int)ApprovalStatusEnum.Approved;
            await db.SaveChangesAsync();
        }
        using var upload = await owner.PostAsJsonAsync(root + "/upload-session", EvidenceUpload());
        await AssertEvidenceProblemAsync(upload, HttpStatusCode.BadRequest);
        using var submit = await owner.PostAsJsonAsync(root, new SubmitOrganizationTenantEvidenceDto { DocumentStorageObjectId = documentId });
        await AssertEvidenceProblemAsync(submit, HttpStatusCode.BadRequest);
        await AssertEvidenceCountAsync(owner, root, 0);
        await Assert.That(factory.WriteCount).IsEqualTo(0);
    }

    [Test]
    public async Task EvidenceReviewRejectsDocumentQuarantineWithoutConsumingPendingDecision()
    {
        await using var factory = await StorageFactory.CreateAsync(useProductionAuthorization: true);
        var scenario = await SeedEvidenceAsync(factory);
        using var owner = Client(factory, scenario.UserId);
        using var reviewer = Client(factory, factory.OwnerId);
        string root = EvidenceRoot(scenario.OrganizationId);
        Guid documentId = await SeedEvidenceDocumentAsync(factory, scenario);
        Guid id = await SeedPendingEvidenceAsync(factory, scenario, documentId);
        var pending = await ReadEvidenceAsync(reviewer, $"{root}/{id}", true);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var document = await db.StorageObjects.SingleAsync(item => item.Id == documentId);
            document.LifecycleState = StorageObjectLifecycleStates.Quarantined;
            await db.SaveChangesAsync();
        }
        using var denied = await reviewer.PostAsJsonAsync($"{root}/{id}/review", new ReviewOrganizationTenantEvidenceDto
        {
            Decision = OrganizationTenantEvidenceReviewDecisionDto.Approve,
            ExpectedConcurrencyStamp = pending.ConcurrencyStamp
        });
        await AssertEvidenceProblemAsync(denied, HttpStatusCode.BadRequest);
        var retained = await ReadEvidenceAsync(reviewer, $"{root}/{id}", true);
        await Assert.That(retained.ReviewStatusId).IsEqualTo((int)ApprovalStatusEnum.Pending);
        await Assert.That(retained.ConcurrencyStamp).IsEqualTo(pending.ConcurrencyStamp);
    }

    [Test]
    public async Task EvidenceSubmissionAndReplayAttachExactlyOneDocument()
    {
        await using var factory = await StorageFactory.CreateAsync(useProductionAuthorization: true);
        var scenario = await SeedEvidenceAsync(factory);
        using var owner = Client(factory, scenario.UserId);
        string root = EvidenceRoot(scenario.OrganizationId);
        Guid documentId = await SeedEvidenceDocumentAsync(factory, scenario);
        var submission = new SubmitOrganizationTenantEvidenceDto { DocumentStorageObjectId = documentId };
        Guid id = await SubmitEvidenceAsync(owner, root, submission);
        await Assert.That(await SubmitEvidenceAsync(owner, root, submission)).IsEqualTo(id);
        await Assert.That((await ReadEvidenceAsync(owner, $"{root}/{id}", false)).DocumentStorageObjectId).IsEqualTo(documentId);
        await AssertEvidenceCountAsync(owner, root, 1);
    }

    [Test]
    public async Task EvidenceConcurrentSubmissionsSerializeToOneRetainedDocument()
    {
        var transactions = new EvidenceSubmissionTransactions();
        await using var factory = await StorageFactory.CreateAsync(useProductionAuthorization: true, transactionObserver: transactions);
        var scenario = await SeedEvidenceAsync(factory);
        using var first = Client(factory, scenario.UserId);
        using var second = Client(factory, scenario.UserId);
        string root = EvidenceRoot(scenario.OrganizationId);
        Guid documentId = await SeedEvidenceDocumentAsync(factory, scenario);
        var submission = new SubmitOrganizationTenantEvidenceDto { DocumentStorageObjectId = documentId };
        transactions.Armed = true;
        var winner = SubmitEvidenceAsync(first, root, submission);
        Task<Guid>? contender = null;
        try
        {
            await transactions.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(20));
            contender = SubmitEvidenceAsync(second, root, submission);
            await transactions.SecondStarting.Task.WaitAsync(TimeSpan.FromSeconds(20));
        }
        finally { transactions.Release.TrySetResult(); }
        Guid[] results = await Task.WhenAll(winner, contender!).WaitAsync(TimeSpan.FromSeconds(20));
        await Assert.That(results[1]).IsEqualTo(results[0]);
        await Assert.That(transactions.FirstIsolation).IsEqualTo(IsolationLevel.Serializable);
        await AssertEvidenceCountAsync(first, root, 1);
    }

    private static string EvidenceRoot(Guid organizationId) => $"/api/organizations/{organizationId}/legitimacy-evidence";

    private static CreateOrganizationTenantEvidenceUploadSessionDto EvidenceUpload() => new()
    {
        FileName = "evidence.pdf",
        ContentType = "application/pdf",
        ExpectedSizeBytes = 5
    };

    private static async Task<TenantScenarioSeed.TenantOrganizationScenarioResult> SeedEvidenceAsync(StorageFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var scenario = await TenantScenarioSeed.SeedActiveTenantWithOrganizationPublisherAsync(db);
        var participation = await db.OrganizationTenants.SingleAsync(item => item.OrganizationId == scenario.OrganizationId);
        participation.ApprovalStatusId = (int)ApprovalStatusEnum.Pending;
        participation.ApprovedAt = null;
        var membership = await db.TenantUsers.SingleAsync(item => item.UserId == factory.OwnerId);
        db.TenantUserRoleGrants.Add(new TenantUserRoleGrant
        {
            Id = Guid.CreateVersion7(),
            TenantId = scenario.TenantId,
            Tenant = null!,
            TenantUserId = membership.Id,
            TenantUser = membership,
            RoleId = (int)RoleEnum.TenantAdmin,
            Role = null!,
            RoleScopeId = (int)RoleScopeEnum.Tenant
        });
        await db.SaveChangesAsync();
        return scenario;
    }

    private static async Task<Guid> SeedEvidenceDocumentAsync(
        StorageFactory factory, TenantScenarioSeed.TenantOrganizationScenarioResult scenario)
    {
        // Submission starts from a finalized document; byte finalization belongs to the storage cohort.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var participation = await db.OrganizationTenants.SingleAsync(item => item.OrganizationId == scenario.OrganizationId);
        var document = new StorageObject
        {
            Id = Guid.CreateVersion7(),
            TenantId = scenario.TenantId,
            Tenant = null!,
            FileTypeId = (int)FileTypeEnum.Document,
            FileType = null!,
            Provider = StorageProviders.Local,
            Uri = string.Empty,
            ObjectKey = $"tenants/{scenario.TenantId:N}/private-evidence.pdf",
            FullName = "evidence.pdf",
            SafeDisplayName = "evidence.pdf",
            Extension = "pdf",
            ContentType = "application/pdf",
            Size = 5,
            CreatedBy = scenario.UserId,
            Visibility = StorageObjectVisibilities.PrivateOwner,
            Purpose = StorageObjectPurposes.Document,
            LifecycleState = StorageObjectLifecycleStates.Active,
            OwningResourceKind = StorageOwningResourceKinds.OrganizationTenant,
            OwningResourceId = participation.Id
        };
        db.StorageObjects.Add(document);
        await db.SaveChangesAsync();
        return document.Id;
    }

    private static async Task<Guid> SeedPendingEvidenceAsync(
        StorageFactory factory, TenantScenarioSeed.TenantOrganizationScenarioResult scenario, Guid documentId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var participation = await db.OrganizationTenants.SingleAsync(item => item.OrganizationId == scenario.OrganizationId);
        var document = await db.StorageObjects.SingleAsync(item => item.Id == documentId);
        var evidence = OrganizationTenantEvidence.CreatePending(participation, document);
        db.OrganizationTenantEvidence.Add(evidence);
        await db.SaveChangesAsync();
        return evidence.Id;
    }

    private static async Task<Guid> SubmitEvidenceAsync(HttpClient client, string root, SubmitOrganizationTenantEvidenceDto submission)
    {
        using var response = await client.PostAsJsonAsync(root, submission);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var result = (await response.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>())!;
        await Assert.That(response.Headers.Location!.AbsolutePath).IsEqualTo($"{root}/{result.Id}");
        return result.Id;
    }

    private static async Task<OrganizationTenantEvidenceDto> ReadEvidenceAsync(HttpClient client, string path, bool canReview)
    {
        using var response = await client.GetAsync(path);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.CacheControl!.NoStore).IsTrue();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        var links = root.GetProperty("_links");
        await Assert.That(links.TryGetProperty(LinkRelations.ReviewEvidence, out _)).IsEqualTo(canReview);
        await Assert.That(links.TryGetProperty(LinkRelations.Self, out _)).IsTrue();
        await AssertPrivateEvidenceAsync(root);
        return root.Deserialize<OrganizationTenantEvidenceDto>(JsonSerializerOptions.Web)!;
    }

    private static async Task AssertEvidenceCountAsync(HttpClient client, string path, int expected)
    {
        using var response = await client.GetAsync(path);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.CacheControl!.NoStore).IsTrue();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = json.RootElement.GetProperty("_embedded").EnumerateObject().Single().Value;
        await Assert.That(items.GetArrayLength()).IsEqualTo(expected);
        foreach (var item in items.EnumerateArray()) await AssertPrivateEvidenceAsync(item);
    }

    private static async Task AssertPrivateEvidenceAsync(JsonElement evidence)
    {
        foreach (string field in new[] { "tenantId", "organizationTenantId", "documentCreatedBy", "reviewedByUserId", "objectKey", "uri", "provider" })
            await Assert.That(evidence.TryGetProperty(field, out _)).IsFalse();
    }

    private static async Task AssertEvidenceProblemAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        await Assert.That(response.StatusCode).IsEqualTo(status);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(json.RootElement.GetProperty("status").GetInt32()).IsEqualTo((int)status);
        await AssertPrivateEvidenceAsync(json.RootElement);
        await Assert.That(json.RootElement.TryGetProperty("_links", out _)).IsFalse();
    }

    private sealed class EvidenceSubmissionTransactions : DbTransactionInterceptor
    {
        public bool Armed { get; set; }
        public TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondStarting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IsolationLevel FirstIsolation { get; private set; }
        private int _started;

        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            if (Armed && FirstStarted.Task.IsCompleted) SecondStarting.TrySetResult();
            return ValueTask.FromResult(result);
        }

        public override async ValueTask<DbTransaction> TransactionStartedAsync(
            DbConnection connection, TransactionEndEventData eventData, DbTransaction result,
            CancellationToken cancellationToken = default)
        {
            if (Armed && Interlocked.Increment(ref _started) == 1)
            {
                FirstIsolation = result.IsolationLevel;
                FirstStarted.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }
            return result;
        }
    }
}
