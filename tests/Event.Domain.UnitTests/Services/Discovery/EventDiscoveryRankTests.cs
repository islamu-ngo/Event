using Explore.Domain.Enums;
using Explore.Domain.Federation;
using Explore.Domain.Services.Discovery;

namespace Event.Domain.UnitTests.Services.Discovery;

public sealed class EventDiscoveryRankTests
{
    [Test]
    [Arguments("a z ", "00410020005A0020")]
    [Arguments("\u00e9", "00C9")]
    [Arguments("\ud83d\ude00", "D83DDE00")]
    [Arguments("\ue000", "E000")]
    public async Task Title_encoding_preserves_the_exact_uppercase_utf16_ordinal_contract(string title, string expected)
    {
        await Assert.That(EventDiscoveryRank.TitleKey(title)).IsEqualTo(expected);
    }

    [Test]
    public async Task Case_equivalence_does_not_discard_trailing_spaces_or_supplementary_order()
    {
        await Assert.That(EventDiscoveryRank.TitleKey("\u00e9")).IsEqualTo(EventDiscoveryRank.TitleKey("\u00c9"));
        await Assert.That(StringComparer.Ordinal.Compare(EventDiscoveryRank.TitleKey("A"), EventDiscoveryRank.TitleKey("A ")))
            .IsLessThan(0);
        await Assert.That(StringComparer.Ordinal.Compare(EventDiscoveryRank.TitleKey("\ud83d\ude00"), EventDiscoveryRank.TitleKey("\ue000")))
            .IsLessThan(0);
    }

    [Test]
    public async Task Event_keys_follow_both_assignment_orders_and_later_source_changes()
    {
        var first = Guid.Parse("00000000-0000-7000-8000-000000000011");
        var second = Guid.Parse("00000000-0000-7000-8000-000000000012");
        var entity = new Explore.Domain.Event(EventStatusEnum.Published)
        {
            Id = first,
            Title = "a",
            Actor = null!,
            Tenant = null!,
            VisibilityType = null!,
            EventFormat = null!,
            EventStatus = null!
        };
        await Assert.That(entity.DiscoverySourceSortKey).IsEqualTo("00000000000070008000000000000011");
        await Assert.That(entity.DiscoveryTitleSortKey).IsEqualTo("0041");
        entity.Title = "\u00e9";
        entity.Id = second;
        await Assert.That(entity.DiscoveryTitleSortKey).IsEqualTo("00C9");
        await Assert.That(entity.DiscoverySourceSortKey).IsEqualTo("00000000000070008000000000000012");
        var projection = new AtprotoEventProjection { Name = "a ", AtprotoRecordId = first };
        await Assert.That(projection.DiscoveryTitleSortKey).IsEqualTo("00410020");
        projection.Name = "Z";
        projection.AtprotoRecordId = second;
        await Assert.That(projection.DiscoveryTitleSortKey).IsEqualTo("005A");
        await Assert.That(projection.DiscoverySourceSortKey).IsEqualTo(entity.DiscoverySourceSortKey);
    }
}
