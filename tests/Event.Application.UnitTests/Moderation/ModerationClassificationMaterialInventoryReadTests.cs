using System.Text.Json;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Handlers.Queries;
using Explore.Application.Features.Moderation.Requests.Queries;
using Explore.Application.Services.Moderation;
using NSubstitute;

namespace Event.Application.UnitTests.Moderation;

public sealed class ModerationClassificationMaterialInventoryReadTests
{
    [Test]
    [Arguments("event-generation")]
    [Arguments("taxonomy")]
    [Arguments("profile")]
    [Arguments("mode")]
    [Arguments("null-mandate")]
    [Arguments("owner-assignment")]
    [Arguments("shared-controller")]
    public async Task InvalidInventoryIntentCannotAcquireOrExposeNativeData(string defect)
    {
        var query = Query();
        query = defect switch
        {
            "event-generation" => query with { EventGenerationId = Guid.NewGuid() },
            "taxonomy" => query with { TaxonomyId = Guid.Empty },
            "profile" => query with { ProfileId = Guid.Empty },
            "mode" => query with { Mode = (ModerationNativeMode)999 },
            "null-mandate" => query with { Mandate = null! },
            "owner-assignment" => query with { Mandate = query.Mandate with { OwnerAssignmentRevision = 0 } },
            "shared-controller" => query with
            {
                Mandate = query.Mandate with { ReviewerControllerHumanId = query.Mandate.OwnerControllerHumanId }
            },
            _ => throw new ArgumentOutOfRangeException(nameof(defect))
        };
        await using var pending = await new GetModerationClassificationMaterialInventoryQueryHandler(null!, null!, null!)
            .QueryAsync(query);
        await Assert.That(pending.Outcome).IsEqualTo(ModerationCurrentPolicyReadOutcome.Invalid);
        await Assert.That(pending.Projection).IsNull();
    }

    [Test]
    [Arguments(ModerationCurrentPolicyReadOutcome.Invalid)]
    [Arguments(ModerationCurrentPolicyReadOutcome.Unauthenticated)]
    [Arguments(ModerationCurrentPolicyReadOutcome.Denied)]
    [Arguments(ModerationCurrentPolicyReadOutcome.Unavailable)]
    public async Task TerminalFailureUsesServerTenantAndHumanAndExposesNoInventory(ModerationCurrentPolicyReadOutcome outcome)
    {
        var query = Query();
        var tenantId = Guid.NewGuid();
        var actualHuman = Guid.NewGuid();
        var identity = Substitute.For<IAdminContext>();
        identity.ResolveUserIdAsync(Arg.Any<CancellationToken>()).Returns(actualHuman);
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var owner = new ReadOwner(outcome);
        await using var pending = await new GetModerationClassificationMaterialInventoryQueryHandler(owner, identity, tenant)
            .QueryAsync(query);
        await Assert.That(owner.Request).IsNull();
        var result = await pending.Projection!.DiscloseAsync<byte[]>(
            (_, _) => throw new InvalidOperationException("Failed native acquisition cannot prepare inventory."),
            (_, _) => throw new InvalidOperationException("Failed native acquisition cannot deliver inventory."));
        await Assert.That(result).IsEqualTo(outcome);
        await Assert.That(owner.Request!.RequestTenantId).IsEqualTo(tenantId);
        await Assert.That(owner.Request.HumanId).IsEqualTo(actualHuman);
        await Assert.That(owner.Request.Mandate.OwnerHumanId).IsEqualTo(query.Mandate.OwnerHumanId);
        using var json = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(pending,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        await Assert.That(json.RootElement.EnumerateObject().Select(property => property.Name).SequenceEqual(["outcome"])).IsTrue();
    }

    [Test]
    public async Task AllowedStatusWithoutNativeLeaseCannotPrepareOrDeliverInventory()
    {
        var identity = Substitute.For<IAdminContext>();
        var tenant = Substitute.For<ITenantContext>();
        await using var pending = await new GetModerationClassificationMaterialInventoryQueryHandler(
            new ReadOwner(ModerationCurrentPolicyReadOutcome.Allowed), identity, tenant).QueryAsync(Query());
        await Assert.That(await pending.Projection!.DiscloseAsync<byte[]>(
            (_, _) => throw new InvalidOperationException("No lease can prepare inventory."),
            (_, _) => throw new InvalidOperationException("No lease can release inventory.")))
            .IsEqualTo(ModerationCurrentPolicyReadOutcome.Unavailable);
    }

    [Test]
    public async Task DisposedPendingReadCannotAcquireReplacementAuthority()
    {
        var identity = Substitute.For<IAdminContext>();
        var tenant = Substitute.For<ITenantContext>();
        var owner = new ReadOwner(ModerationCurrentPolicyReadOutcome.Denied);
        var pending = await new GetModerationClassificationMaterialInventoryQueryHandler(owner, identity, tenant).QueryAsync(Query());
        await pending.DisposeAsync();
        await Assert.That(await pending.Projection!.DiscloseAsync<byte[]>(
            (_, _) => throw new InvalidOperationException(), (_, _) => throw new InvalidOperationException()))
            .IsEqualTo(ModerationCurrentPolicyReadOutcome.Unavailable);
        await Assert.That(owner.Request).IsNull();
    }

    private static GetModerationClassificationMaterialInventoryQuery Query()
    {
        var owner = Guid.NewGuid();
        var reviewer = Guid.NewGuid();
        return new()
        {
            EventGenerationId = Guid.CreateVersion7(), TaxonomyId = Guid.CreateVersion7(), ProfileId = Guid.CreateVersion7(),
            Mode = ModerationNativeMode.Embedded,
            Mandate = new(owner, owner, Guid.NewGuid(), 1, reviewer, reviewer,
                "Current event ownership.", "Independent material review.")
        };
    }

    private sealed class ReadOwner(ModerationCurrentPolicyReadOutcome outcome) : IModerationClassificationMaterialInventoryReadRepository
    {
        public ModerationClassificationMaterialInventoryReadRequest? Request { get; private set; }
        public Task<ModerationClassificationMaterialInventoryReadAcquisition> AcquireInventoryReadAsync(
            ModerationClassificationMaterialInventoryReadRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(new ModerationClassificationMaterialInventoryReadAcquisition(outcome));
        }
    }
}
