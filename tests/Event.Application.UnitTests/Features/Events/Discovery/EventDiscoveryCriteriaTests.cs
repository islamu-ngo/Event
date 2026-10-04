using Explore.Application.Features.Events.Discovery;
using Explore.Application.Features.Events.Requests.Queries;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Event.Application.UnitTests.Features.Events.Discovery;

public sealed class EventDiscoveryCriteriaTests
{
    [Test]
    public async Task Pre_rank_contract_cursors_cannot_reuse_current_discovery_membership()
    {
        var criteria = new GetEventListRequest { SortBy = "date", PageNumber = 1, PageSize = 20 };
        // Reconstruct the previous machine contract for a scalar-only request, not
        // the new rank encoding. Existing protected cursors contain this old digest.
        var oldObject = JsonSerializer.SerializeToNode(criteria)!.AsObject();
        var ordered = new JsonObject();
        foreach (var property in oldObject.OrderBy(property => property.Key, StringComparer.Ordinal))
            ordered.Add(property.Key, property.Value?.DeepClone());
        string previousDigest = Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes(ordered.ToJsonString())));

        await Assert.That(EventDiscoveryCriteria.Digest(criteria)).IsNotEqualTo(previousDigest);
    }

    [Test]
    public async Task PaginationAndServerClockDoNotChangePublicSearchIdentity()
    {
        var criteria = new GetEventListRequest { SearchTerm = "lesson", PageSize = 20 };
        string digest = EventDiscoveryCriteria.Digest(criteria);

        await Assert.That(EventDiscoveryCriteria.Digest(criteria with
        {
            PageSize = 100,
            PageNumber = 7,
            OperationNow = new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero)
        })).IsEqualTo(digest);
    }

    [Test]
    public async Task SetOrderAndRepeatedValuesDoNotCreateSeparateAnonymousSnapshots()
    {
        Guid first = Guid.CreateVersion7();
        Guid second = Guid.CreateVersion7();
        var criteria = new GetEventListRequest
        {
            IncludedTagIds = [first, second],
            FormatIds = [1, 2]
        };

        await Assert.That(EventDiscoveryCriteria.Digest(criteria with
        {
            IncludedTagIds = [second, first, first],
            FormatIds = [2, 1, 2]
        })).IsEqualTo(EventDiscoveryCriteria.Digest(criteria));
    }

    [Test]
    public async Task ChangedMatchingOrRankingCriteriaCannotReuseMembership()
    {
        var criteria = new GetEventListRequest { SearchTerm = "lesson", SortBy = "date" };
        string digest = EventDiscoveryCriteria.Digest(criteria);

        await Assert.That(EventDiscoveryCriteria.Digest(criteria with { SearchTerm = "seminar" }))
            .IsNotEqualTo(digest);
        await Assert.That(EventDiscoveryCriteria.Digest(criteria with { SortDescending = false }))
            .IsNotEqualTo(digest);
        await Assert.That(EventDiscoveryCriteria.Digest(criteria with { AreaId = Guid.CreateVersion7() }))
            .IsNotEqualTo(digest);
    }
}
