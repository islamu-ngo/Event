using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventTags;
using Explore.Application.DTOs.EventCategories;
using Explore.Application.Features.EventTags.Handlers.Commands;
using Explore.Application.Features.EventTags.Requests.Commands;
using Explore.Application.Features.EventCategories.Handlers.Commands;
using Explore.Application.Features.EventCategories.Requests.Commands;
using Explore.Application.Mappings;
using Explore.Domain;
using NSubstitute;

namespace Event.Application.UnitTests.Profiles;

public sealed class EventAssignmentMapperTests
{
    private static readonly Guid TenantId = Guid.Parse("01900000-0000-7000-8000-000000000001");
    private static readonly Guid EventId = Guid.Parse("01900000-0000-7000-8000-000000000002");
    private static readonly Guid ClassificationId = Guid.Parse("01900000-0000-7000-8000-000000000003");

    [Test]
    public async Task Assignments_ProjectLabelsCodesAndNullNavigationsWithoutGraphs()
    {
        var tag = new EventTags { EventId = EventId, TagId = ClassificationId, TenantId = TenantId, Event = null!, Tag = new Tag { FullName = "Community", MasterCode = "COMMUNITY", Tenant = null! }, Tenant = null! };
        var category = new EventCategories { EventId = EventId, CategoryId = ClassificationId, TenantId = TenantId, Event = null!, Category = new Category { FullName = "Lecture", MasterCode = "LECTURE", Tenant = null! }, Tenant = null! };
        var tagDetail = EventMapper.ToDetail(tag);
        var tagList = EventMapper.ToListItem(tag);
        var categoryDetail = EventMapper.ToDetail(category);
        var categoryList = EventMapper.ToListItem(category);
        tag.Tag.FullName = "Changed";
        category.Category.MasterCode = "CHANGED";
        await Assert.That(tagDetail!.TagFullName).IsEqualTo("Community");
        await Assert.That(tagList.TagMasterCode).IsEqualTo("COMMUNITY");
        await Assert.That(categoryDetail!.CategoryMasterCode).IsEqualTo("LECTURE");
        await Assert.That(categoryList.CategoryFullName).IsEqualTo("Lecture");
        await Assert.That(tagDetail.EventTitle).IsNull();
        await Assert.That(categoryDetail.EventTitle).IsNull();
        await Assert.That(tagDetail.EventId).IsEqualTo(EventId);
        await Assert.That(categoryDetail.TenantId).IsEqualTo(TenantId);
        await Assert.That(EventMapper.ToDetail((EventTags?)null)).IsNull();
        await Assert.That(EventMapper.ToDetail((EventCategories?)null)).IsNull();
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(categoryList));
        await Assert.That(json.RootElement.EnumerateObject().Count()).IsEqualTo(8);
        await Assert.That(json.RootElement.TryGetProperty("Category", out _)).IsFalse();
    }

    [Test]
    public async Task CreateAssignments_UseTrustedTenantAndOnlyValidatedRelationshipIds()
    {
        var events = Substitute.For<IEventRepository>();
        var tags = Substitute.For<ITagRepository>();
        var categories = Substitute.For<ICategoryRepository>();
        var tagAssignments = Substitute.For<IEventTagsRepository>();
        var categoryAssignments = Substitute.For<IEventCategoriesRepository>();
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        events.Exists(EventId).Returns(true);
        tags.Exists(ClassificationId).Returns(true);
        categories.Exists(ClassificationId).Returns(true);
        EventTags? savedTag = null;
        EventCategories? savedCategory = null;
        tagAssignments.Create(Arg.Any<EventTags>()).Returns(call => { var entity = call.Arg<EventTags>(); savedTag = entity; return entity; });
        categoryAssignments.Create(Arg.Any<EventCategories>()).Returns(call => { var entity = call.Arg<EventCategories>(); savedCategory = entity; return entity; });
        var tagResult = await new CreateEventTagsCommandHandler(tagAssignments, events, tags, tenant).Handle(new CreateEventTagsCommand { EventTagsDto = new CreateEventTagsDto { EventId = EventId, TagId = ClassificationId, TenantId = ClassificationId } }, CancellationToken.None);
        var categoryResult = await new CreateEventCategoriesCommandHandler(categoryAssignments, events, categories, tenant).ExecuteAsync(new CreateEventCategoriesCommand { EventCategoriesDto = new CreateEventCategoriesDto { EventId = EventId, CategoryId = ClassificationId, TenantId = ClassificationId } }, CancellationToken.None);
        await Assert.That(savedTag).IsNotNull();
        await Assert.That(savedCategory).IsNotNull();
        await Assert.That(savedTag!.TenantId).IsEqualTo(TenantId);
        await Assert.That(savedCategory!.TenantId).IsEqualTo(TenantId);
        await Assert.That(savedTag.TagId).IsEqualTo(ClassificationId);
        await Assert.That(savedCategory.CategoryId).IsEqualTo(ClassificationId);
        await Assert.That(savedTag.EventId).IsEqualTo(EventId);
        await Assert.That(savedCategory.EventId).IsEqualTo(EventId);
        await Assert.That(savedTag.CreatedAt).IsEqualTo(default(DateTime));
        await Assert.That(savedCategory.ConcurrencyStamp).IsEqualTo(Guid.Empty);
        await Assert.That(savedTag.Event).IsNull();
        await Assert.That(savedCategory.Category).IsNull();
    }
}
