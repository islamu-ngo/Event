using System.Net;
using System.Net.Http.Json;
using Explore.Application.DTOs.OrganizationTenantEvidence;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.Responses;
using Explore.Domain.Enums;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeStorageObjectHttpTests
{
    [Test]
    public async Task EvidenceHttpLifecycleKeepsFinalizedDocumentBindingAcrossAllFiveOperations()
    {
        await using var factory = await StorageFactory.CreateAsync(useProductionAuthorization: true);
        var scenario = await SeedEvidenceAsync(factory);
        using var owner = Client(factory, scenario.UserId);
        using var reviewer = Client(factory, factory.OwnerId);
        string root = EvidenceRoot(scenario.OrganizationId);
        var reserved = await ReserveEvidencePdfAsync(owner, scenario.OrganizationId);
        using var upload = await PutAsync(owner, reserved.Id, "%PDF-"u8.ToArray());
        await Assert.That(upload.StatusCode).IsEqualTo(HttpStatusCode.OK);
        Guid documentId = (await upload.Content.ReadFromJsonAsync<BaseCommandResponse<StorageUploadSessionDto>>())!.Id!.StorageObjectId!.Value;
        var submission = new SubmitOrganizationTenantEvidenceDto { DocumentStorageObjectId = documentId };
        Guid id = await SubmitEvidenceAsync(owner, root, submission);
        var pending = await ReadEvidenceAsync(reviewer, $"{root}/{id}", canReview: true);
        await Assert.That(pending.DocumentStorageObjectId).IsEqualTo(documentId);
        await Assert.That(pending.DocumentContentType).IsEqualTo("application/pdf");
        await Assert.That(pending.DocumentSizeBytes).IsEqualTo(5);
        await AssertEvidenceCountAsync(owner, root, 1);
        using var decision = await reviewer.PostAsJsonAsync($"{root}/{id}/review", new ReviewOrganizationTenantEvidenceDto
        {
            Decision = OrganizationTenantEvidenceReviewDecisionDto.Approve,
            ExpectedConcurrencyStamp = pending.ConcurrencyStamp
        });
        await Assert.That(decision.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var approved = await ReadEvidenceAsync(owner, $"{root}/{id}", canReview: false);
        await Assert.That(approved.ReviewStatusId).IsEqualTo((int)ApprovalStatusEnum.Approved);
        await Assert.That(await SubmitEvidenceAsync(owner, root, submission)).IsEqualTo(id);
        await AssertEvidenceCountAsync(reviewer, root, 1);
        using var content = await owner.GetAsync($"{Root}/{documentId}/content");
        await Assert.That(content.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await content.Content.ReadAsByteArrayAsync()).IsEquivalentTo("%PDF-"u8.ToArray());
        await Assert.That(factory.WriteCount).IsEqualTo(1);
        await Assert.That(factory.Signings).IsEmpty();
    }

    [Test]
    public async Task EvidenceSubmissionReplayPreservesOnePendingRecordAndDocumentReadback()
    {
        await using var factory = await StorageFactory.CreateAsync(useProductionAuthorization: true);
        var scenario = await SeedEvidenceAsync(factory);
        using var owner = Client(factory, scenario.UserId);
        using var reviewer = Client(factory, factory.OwnerId);
        string root = EvidenceRoot(scenario.OrganizationId);
        Guid documentId = await SeedEvidenceDocumentAsync(factory, scenario);
        var submission = new SubmitOrganizationTenantEvidenceDto { DocumentStorageObjectId = documentId };

        Guid id = await SubmitEvidenceAsync(owner, root, submission);
        var submitted = await ReadEvidenceAsync(reviewer, $"{root}/{id}", canReview: true);
        Guid replayId = await SubmitEvidenceAsync(owner, root, submission);
        var replayed = await ReadEvidenceAsync(reviewer, $"{root}/{replayId}", canReview: true);

        await Assert.That(replayId).IsEqualTo(id);
        await Assert.That(submitted.DocumentStorageObjectId).IsEqualTo(documentId);
        await Assert.That(submitted.DocumentDisplayName).IsEqualTo("evidence.pdf");
        await Assert.That(submitted.DocumentSizeBytes).IsEqualTo(5);
        await Assert.That(submitted.ReviewStatusId).IsEqualTo((int)ApprovalStatusEnum.Pending);
        await Assert.That(submitted.ReviewedAt).IsNull();
        await Assert.That(replayed).IsEqualTo(submitted);
        await AssertEvidenceCountAsync(owner, root, 1);
        await AssertEvidenceCountAsync(reviewer, root, 1);
        // Submission and replay must not approve the participation or write/sign stored bytes.
        using var reservation = await owner.PostAsJsonAsync(root + "/upload-session", EvidenceUpload());
        await Assert.That(reservation.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(factory.WriteCount).IsEqualTo(0);
        await Assert.That(factory.Signings).IsEmpty();
    }
}
