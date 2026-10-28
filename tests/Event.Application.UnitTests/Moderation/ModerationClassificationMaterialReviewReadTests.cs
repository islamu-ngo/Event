using System.Text.Json;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Handlers.Queries;
using NSubstitute;

namespace Event.Application.UnitTests.Moderation;

public sealed class ModerationClassificationMaterialReviewReadTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task EmptyOrNonNativeSourceCannotAcquireReviewerMetadata(bool nonNative)
    {
        await using var result = await new GetModerationClassificationMaterialReviewQueryHandler(null!, null!, null!)
            .QueryAsync(new(nonNative ? Guid.NewGuid() : Guid.Empty));
        await Assert.That(result.Outcome).IsEqualTo(ModerationCurrentPolicyReadOutcome.Invalid);
        await Assert.That(result.Projection).IsNull();
    }

    [Test]
    [Arguments(ModerationCurrentPolicyReadOutcome.Invalid)]
    [Arguments(ModerationCurrentPolicyReadOutcome.Unauthenticated)]
    [Arguments(ModerationCurrentPolicyReadOutcome.Denied)]
    [Arguments(ModerationCurrentPolicyReadOutcome.Unavailable)]
    public async Task FailedNativeReadUsesServerFrameAndCannotRevealProposal(ModerationCurrentPolicyReadOutcome outcome)
    {
        var human = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var sourceId = Guid.CreateVersion7();
        var identity = Substitute.For<IAdminContext>();
        identity.ResolveUserIdAsync(Arg.Any<CancellationToken>()).Returns(human);
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var owner = new ReadOwner(outcome);
        await using var result = await new GetModerationClassificationMaterialReviewQueryHandler(owner, identity, tenant)
            .QueryAsync(new(sourceId));
        await Assert.That(owner.Request).IsNull();
        await Assert.That(await result.Projection!.DiscloseAsync<byte[]>(
            (_, _) => throw new InvalidOperationException("Failed qualification cannot prepare a proposal."),
            (_, _) => throw new InvalidOperationException("Failed qualification cannot deliver a proposal."))).IsEqualTo(outcome);
        await Assert.That(owner.Request).IsEqualTo(new ModerationClassificationMaterialReviewReadRequest(tenantId, human, sourceId));
        using var json = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(result,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        await Assert.That(json.RootElement.EnumerateObject().Select(property => property.Name).SequenceEqual(["outcome"])).IsTrue();
    }

    [Test]
    public async Task AllowedWithoutOriginalLeaseCannotRevealProposal()
    {
        await using var result = await new GetModerationClassificationMaterialReviewQueryHandler(
            new ReadOwner(ModerationCurrentPolicyReadOutcome.Allowed),
            Substitute.For<IAdminContext>(), Substitute.For<ITenantContext>()).QueryAsync(new(Guid.CreateVersion7()));
        await Assert.That(await result.Projection!.DiscloseAsync<byte[]>(
            (_, _) => throw new InvalidOperationException(), (_, _) => throw new InvalidOperationException()))
            .IsEqualTo(ModerationCurrentPolicyReadOutcome.Unavailable);
    }

    [Test]
    public async Task DisposedProposalReadCannotReplaceOriginalAuthority()
    {
        var owner = new ReadOwner(ModerationCurrentPolicyReadOutcome.Denied);
        var result = await new GetModerationClassificationMaterialReviewQueryHandler(owner,
            Substitute.For<IAdminContext>(), Substitute.For<ITenantContext>()).QueryAsync(new(Guid.CreateVersion7()));
        await result.DisposeAsync();
        await Assert.That(await result.Projection!.DiscloseAsync<byte[]>(
            (_, _) => throw new InvalidOperationException(), (_, _) => throw new InvalidOperationException()))
            .IsEqualTo(ModerationCurrentPolicyReadOutcome.Unavailable);
        await Assert.That(owner.Request).IsNull();
    }

    private sealed class ReadOwner(ModerationCurrentPolicyReadOutcome outcome) : IModerationClassificationMaterialReviewReadRepository
    {
        public ModerationClassificationMaterialReviewReadRequest? Request { get; private set; }
        public Task<ModerationClassificationMaterialReviewReadAcquisition> AcquireReviewReadAsync(
            ModerationClassificationMaterialReviewReadRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(new ModerationClassificationMaterialReviewReadAcquisition(outcome));
        }
    }
}
