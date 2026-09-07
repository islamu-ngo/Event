using AutoMapper;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Category;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.Tag;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Event.Application.UnitTests.Services;

public sealed class EventDetailsProjectionServiceTests
{
    private readonly IEventRepository _eventRepository = Substitute.For<IEventRepository>();
    private readonly IEventModerationRecordRepository _eventModerationRecordRepository = Substitute.For<IEventModerationRecordRepository>();
    private readonly IEventTagsRepository _eventTagsRepository = Substitute.For<IEventTagsRepository>();
    private readonly IEventCategoriesRepository _eventCategoriesRepository = Substitute.For<IEventCategoriesRepository>();
    private readonly IMapper _mapper = Substitute.For<IMapper>();
    private readonly IObjectStorageService _objectStorageService = Substitute.For<IObjectStorageService>();
    private readonly EventDetailsProjectionService _service;

    public EventDetailsProjectionServiceTests()
    {
        _service = new EventDetailsProjectionService(
            _eventRepository,
            _eventModerationRecordRepository,
            _eventTagsRepository,
            _eventCategoriesRepository,
            _mapper,
            _objectStorageService,
            Substitute.For<ILogger<EventDetailsProjectionService>>());
    }

    [Test]
    public async Task BuildAsync_WithReversibleLightModeration_EnrichesEventDetails()
    {
        var tenantId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var eventEntity = new Explore.Domain.Event
        {
            Id = eventId,
            TenantId = tenantId,
            Title = "Moderated Event",
            Actor = null!,
            Tenant = null!,
            VisibilityType = null!,
            EventStatus = null!,
            EventFormat = null!
        };
        var eventDto = new EventDto
        {
            Id = eventId,
            TenantId = tenantId,
            Title = "Moderated Event",
            ActorDisplayName = string.Empty,
            ActorTypeFullName = string.Empty,
            EventStatusId = (int)EventStatusEnum.Moderated,
            EventStatusFullName = "Moderated",
            EventStatusMasterCode = "MODERATED",
            VisibilityTypeFullName = string.Empty,
            VisibilityTypeMasterCode = string.Empty,
            EventFormatFullName = string.Empty,
            EventFormatMasterCode = string.Empty
        };
        var latestModerationRecord = EventModerationRecord.CreateLightModeration(
            Guid.CreateVersion7(),
            tenantId,
            eventId,
            Guid.NewGuid(),
            "policy_review",
            (int)EventStatusEnum.Published,
            null,
            DateTimeOffset.UtcNow);
        var tags = new List<Tag>
        {
            new()
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                MasterCode = "COMMUNITY",
                FullName = "Community",
                Tenant = null!
            }
        };
        var categories = new List<Category>
        {
            new()
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                MasterCode = "LECTURE",
                FullName = "Lecture",
                Tenant = null!
            }
        };
        var tagDtos = new List<TagListDto>
        {
            new()
            {
                Id = tags[0].Id,
                MasterCode = tags[0].MasterCode,
                FullName = tags[0].FullName
            }
        };
        var categoryDtos = new List<CategoryListDto>
        {
            new()
            {
                Id = categories[0].Id,
                MasterCode = categories[0].MasterCode,
                FullName = categories[0].FullName
            }
        };
        var expectedTagDtos = tagDtos.ToArray();
        var expectedCategoryDtos = categoryDtos.ToArray();

        _eventRepository.GetEventWithDetails(eventId).Returns(eventEntity);
        _mapper.Map<EventDto>(eventEntity).Returns(eventDto);
        _eventModerationRecordRepository.GetLatestByEventAsync(tenantId, eventId, Arg.Any<CancellationToken>())
            .Returns(latestModerationRecord);
        _eventTagsRepository.GetTagsByEvent(eventId).Returns(tags);
        _eventCategoriesRepository.GetCategoriesByEvent(eventId).Returns(categories);
        _mapper.Map<List<TagListDto>>(tags).Returns(tagDtos);
        _mapper.Map<List<CategoryListDto>>(categories).Returns(categoryDtos);

        var result = await _service.BuildAsync(eventId, CancellationToken.None);

        await Assert.That(result).IsNotNull();
        await Assert.That(result.IsUnmoderationEligible).IsTrue();
        await Assert.That(result.Tags.SequenceEqual(expectedTagDtos)).IsTrue();
        await Assert.That(result.Categories.SequenceEqual(expectedCategoryDtos)).IsTrue();

        tagDtos.Clear();
        categoryDtos.Clear();

        await Assert.That(result.Tags.SequenceEqual(expectedTagDtos)).IsTrue();
        await Assert.That(result.Categories.SequenceEqual(expectedCategoryDtos)).IsTrue();
    }
}
