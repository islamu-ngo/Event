using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Features.Moderation.Handlers.Queries;
using ISLAMU.Moderation.Domain.Policies;

namespace Event.Application.UnitTests.Moderation;

public sealed class ModerationReferenceCatalogueQueryTests
{
    [Test]
    public async Task AuthoringReadPreservesEveryReferenceAndCompleteConsequencePlan()
    {
        var result = await new GetModerationReferenceCatalogueQueryHandler().QueryAsync(new());
        var references = ModerationReferenceCatalogue.Profiles.Values.OrderBy(profile => profile.Id).ToArray();

        await Assert.That(result.Revision).IsEqualTo(ModerationReferenceCatalogue.Revision);
        await Assert.That(result.Profiles.Select(profile => profile.Id).SequenceEqual(
            Enum.GetValues<ModerationReferenceProfileId>().Order())).IsTrue();
        await Assert.That(JsonNode.DeepEquals(
            JsonSerializer.SerializeToNode(result.Profiles),
            JsonSerializer.SerializeToNode(references))).IsTrue();
        await Assert.That(result.Profiles.Single(profile => profile.Id == ModerationReferenceProfileId.CFG09)
            .Effects.Single().Execution).IsEqualTo(ModerationReferenceExecution.NativeOwner);
    }
}
