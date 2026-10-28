using System.Net;
using System.Net.Http.Json;
using ISLAMU.Wire.Contracts.Learning;

namespace Event.API.IntegrationTests.Learning;

[NotInParallel]
public sealed class LearningSpaceLifecycleTests
{
    [Test]
    public async Task AdministratorCanTombstoneAbsentSourceAndExplicitlyRecreateOnlyItsCurrentRevision()
    {
        await using var fixture = new LearningHttpFixture();
        await fixture.InitializeAsync();
        using var administrator = await fixture.CreateMemberAsync(administrator: true);
        var absent = await LearningOperationTests.Context(administrator.Client);
        await Assert.That(absent.SpaceStatus).IsEqualTo(LearningSpaceStatus.Absent);
        await Assert.That(absent.Capabilities.CanManageSpace).IsTrue();
        var links = (System.Text.Json.JsonElement)absent.AdditionalProperties["_links"];
        await Assert.That(links.TryGetProperty("remove-learning-space", out _)).IsTrue();
        await Assert.That(links.TryGetProperty("recreate-learning-space", out _)).IsFalse();
        var remove = new LearningManageSpaceRequest
        {
            ExpectedScopeStamp = absent.ScopeStamp, ExpectedRevision = 0, Action = LearningSpaceAction.Remove
        };
        using var removedResponse = await administrator.Client.PostAsJsonAsync("/api/learning/space/actions", remove);
        await Assert.That(removedResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var removed = (await removedResponse.Content.ReadFromJsonAsync<LearningMutationReceiptDto>())!;
        await Assert.That(removed.Space!.Status).IsEqualTo(LearningSpaceStatus.Removed);
        using var stale = await administrator.Client.PostAsJsonAsync("/api/learning/space/actions",
            remove with { Action = LearningSpaceAction.Recreate });
        await Assert.That(stale.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        var tombstone = await LearningOperationTests.Context(administrator.Client);
        await Assert.That(tombstone.SpaceId).IsEqualTo(removed.Space.Id);
        await Assert.That(tombstone.SpaceStatus).IsEqualTo(LearningSpaceStatus.Removed);
        await Assert.That(tombstone.ResourceRevision).IsEqualTo(removed.Space.Revision);
        using var recreatedResponse = await administrator.Client.PostAsJsonAsync("/api/learning/space/actions",
            remove with { Action = LearningSpaceAction.Recreate, ExpectedRevision = removed.Space.Revision });
        await Assert.That(recreatedResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var recreated = (await recreatedResponse.Content.ReadFromJsonAsync<LearningMutationReceiptDto>())!;
        await Assert.That(recreated.Space!.Id).IsEqualTo(removed.Space.Id);
        await Assert.That(recreated.Space.Revision).IsEqualTo(removed.Space.Revision + 1);
        await Assert.That(recreated.Space.Status).IsEqualTo(LearningSpaceStatus.Active);
        await Assert.That((await LearningOperationTests.Context(administrator.Client)).StateId).IsNull();
    }

    [Test]
    public async Task RemovedSourceCannotRenewATourButStillAllowsItsOwnerToReleaseTheClaim()
    {
        await using var fixture = new LearningHttpFixture();
        await fixture.InitializeAsync();
        using var member = await fixture.CreateMemberAsync();
        using var administrator = await fixture.CreateMemberAsync(administrator: true);
        var before = await LearningOperationTests.Context(member.Client);
        using var claimResponse = await member.Client.PostAsJsonAsync("/api/learning/tours/claim",
            new LearningClaimTourRequest
            {
                ExpectedScopeStamp = before.ScopeStamp, ExpectedRevision = 0, IntentId = Guid.CreateVersion7(),
                ContextKey = "self", Family = LearningTourFamily.Shell, MaterialVersion = 1
            });
        await Assert.That(claimResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var claim = (await claimResponse.Content.ReadFromJsonAsync<LearningMutationReceiptDto>())!;
        var source = await LearningOperationTests.Context(administrator.Client);
        using var removal = await administrator.Client.PostAsJsonAsync("/api/learning/space/actions",
            new LearningManageSpaceRequest
            {
                ExpectedScopeStamp = source.ScopeStamp, ExpectedRevision = source.ResourceRevision, Action = LearningSpaceAction.Remove
            });
        await Assert.That(removal.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var outcome = new LearningRecordTourOutcomeRequest
        {
            ExpectedScopeStamp = before.ScopeStamp, ExpectedRevision = claim.LearnerRevision,
            IntentId = Guid.CreateVersion7(), ClaimToken = claim.Claim!.Token,
            Family = LearningTourFamily.Shell, MaterialVersion = 1, Outcome = LearningTourOutcome.InProgress, StepIndex = 1
        };
        using var progress = await member.Client.PostAsJsonAsync("/api/learning/tours/outcome", outcome);
        await Assert.That(progress.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        using var release = await member.Client.PostAsJsonAsync("/api/learning/tours/outcome",
            outcome with { IntentId = Guid.CreateVersion7(), Outcome = LearningTourOutcome.Interrupted, StepIndex = 0 });
        await Assert.That(release.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var final = await LearningOperationTests.Context(member.Client);
        await Assert.That(final.SpaceStatus).IsEqualTo(LearningSpaceStatus.Removed);
        await Assert.That(final.Claim).IsNull();
        await Assert.That(final.Capabilities.CanSimulate).IsFalse();
    }
}
