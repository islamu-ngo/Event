using Explore.Blazor.Client.Helpers;

namespace Explore.Blazor.Client.Tests.Helpers;

public class EventFilterUrlHelperTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task BuildUrl_WithOwnershipState_UsesLowerCamelQueryKeys()
    {
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        var actorId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        var url = EventFilterUrlHelper.BuildUrl(navigation, new EventFilterUrlState
        {
            SearchTerm = "lecture",
            ActorId = actorId,
            OrganizationId = organizationId,
            GroupId = groupId
        });

        await Assert.That(url).Contains("q=lecture");
        await Assert.That(url).Contains($"actorId={actorId}");
        await Assert.That(url).Contains($"organizationId={organizationId}");
        await Assert.That(url).Contains($"groupId={groupId}");
        await Assert.That(url).DoesNotContain("ActorId=");
        await Assert.That(url).DoesNotContain("OrganizationId=");
        await Assert.That(url).DoesNotContain("GroupId=");
    }

    [Test]
    public async Task ImmutableFilterStateSupportsVariantsAndSnapshotsInputLists()
    {
        var formatIds = new List<int> { 1 };
        var original = new EventFilterUrlState { SearchTerm = "lecture", FormatIds = formatIds };
        var revised = original with { SearchTerm = "workshop" };
        formatIds.Add(2);

        await Assert.That(original.SearchTerm).IsEqualTo("lecture");
        await Assert.That(revised.SearchTerm).IsEqualTo("workshop");
        await Assert.That(original.FormatIds).IsEquivalentTo([1]);
    }
}
