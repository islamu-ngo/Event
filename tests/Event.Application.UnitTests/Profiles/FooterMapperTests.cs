using System.Reflection;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.Footer.Handlers.Queries;
using Explore.Application.Mappings;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using Explore.Domain;
using NSubstitute;

namespace Event.Application.UnitTests.Profiles;

public sealed class FooterMapperTests
{
    private static readonly Guid TenantId = Guid.Parse("01990000-0000-7000-8000-000000000020");
    private static readonly Guid GroupId = Guid.Parse("01990000-0000-7000-8000-000000000021");

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task ProjectionsPreserveDisclosureAndNestedSnapshots(int shape)
    {
        var (group, links) = Graph(TenantId);
        object[] expectedLinks = links.Select(link => (object)new
        {
            link.Id, link.Label, link.Url, link.OpenInNewTab, link.IsActive, link.Order
        }).ToArray();
        object expected = shape switch
        {
            0 => expectedLinks[0],
            1 => new { group.Id, group.Title, group.Order, Links = expectedLinks },
            2 => new { group.Id, group.TenantId, group.Title, group.Order, group.IsActive, Links = expectedLinks },
            3 => new { group.Id, group.TenantId, group.Title, group.Order, group.IsActive, LinkCount = 2 },
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
        object result = shape switch
        {
            0 => FooterMapper.ToLinkItem(links[0]),
            1 => FooterMapper.ToPublicGroup(group),
            2 => FooterMapper.ToDetail(group),
            3 => FooterMapper.ToListItem(group),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
        group.Title = "Changed source";
        links[0].Label = "Changed source";
        links.Clear();

        await Assert.That(JsonSerializer.Serialize(result)).IsEqualTo(JsonSerializer.Serialize(expected));
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task AdminDetailRejectsMissingForeignAndInstanceDefaultGroups(int shape)
    {
        var (group, _) = Graph(shape == 2 ? null : Guid.Parse("01990000-0000-7000-8000-000000000099"));
        var repository = Substitute.For<IFooterLinkGroupRepository>();
        repository.GetWithLinksAsync(GroupId, default).Returns(shape == 0 ? null : group);
        var handler = new GetFooterLinkGroupDetailsQueryHandler(repository, Context());

        await Assert.That(async () => await handler.QueryAsync(new(GroupId), default)).Throws<NotFoundException>();
    }

    [Test]
    public async Task AdminHandlersProjectOwnedGroupAndStoredLinkCount()
    {
        var (group, _) = Graph(TenantId);
        var repository = Substitute.For<IFooterLinkGroupRepository>();
        repository.GetWithLinksAsync(GroupId, default).Returns(group);
        repository.GetByTenantIdAsync(TenantId, default).Returns(new List<TenantFooterLinkGroup> { group });

        var detail = await new GetFooterLinkGroupDetailsQueryHandler(repository, Context())
            .QueryAsync(new(GroupId), default);
        var list = await new GetFooterLinkGroupListQueryHandler(repository, Context())
            .QueryAsync(new(), default);

        await Assert.That(detail.TenantId).IsEqualTo(TenantId);
        await Assert.That(detail.Links.Count).IsEqualTo(2);
        await Assert.That(list.Single().Id).IsEqualTo(GroupId);
        await Assert.That(list.Single().LinkCount).IsEqualTo(2);
    }

    [Test]
    public async Task PublicConfigUsesResolvedDefaultsWithoutExposingOwnership()
    {
        var (group, links) = Graph(null);
        var repository = Substitute.For<IFooterLinkGroupRepository>();
        repository.GetResolvedGroupsForTenantAsync(TenantId, default)
            .Returns(new List<TenantFooterLinkGroup> { group });
        var settings = Substitute.For<IHierarchicalSettingsResolver>();
        settings.ResolveGroupAsync<FooterSettingGroup>(Arg.Any<SettingContext>(), default)
            .Returns(new FooterSettingGroup());

        var result = await new GetFooterConfigQueryHandler(settings, repository, Context())
            .QueryAsync(new(), default);
        links.Clear();

        await Assert.That(result.Settings.Enabled).IsTrue();
        await Assert.That(result.LinkGroups.Single().Id).IsEqualTo(GroupId);
        await Assert.That(result.LinkGroups.Single().Links.Count).IsEqualTo(2);
    }

    private static ITenantContext Context()
    {
        var context = Substitute.For<ITenantContext>();
        context.TenantId.Returns(TenantId);
        return context;
    }

    private static (TenantFooterLinkGroup Group, List<TenantFooterLink> Links) Graph(Guid? tenantId)
    {
        var group = new TenantFooterLinkGroup
        {
            Id = GroupId, TenantId = tenantId, Title = "Community", Order = 3,
            IsActive = true, CreatedAt = DateTime.UnixEpoch, CreatedBy = TenantId
        };
        // Hydrate the EF-managed relationship for this in-memory projection fixture only.
        var links = (List<TenantFooterLink>)typeof(TenantFooterLinkGroup)
            .GetField("_links", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(group)!;
        links.Add(new TenantFooterLink
        {
            Id = Guid.Parse("01990000-0000-7000-8000-000000000022"), FooterLinkGroupId = GroupId,
            Label = "Events", Url = "/events", Order = 9, OpenInNewTab = true, IsActive = false,
            CreatedAt = DateTime.UnixEpoch, CreatedBy = TenantId
        });
        links.Add(new TenantFooterLink
        {
            Id = Guid.Parse("01990000-0000-7000-8000-000000000023"), FooterLinkGroupId = GroupId,
            Label = "Home", Url = "/", Order = 1, OpenInNewTab = false, IsActive = true
        });
        return (group, links);
    }
}
