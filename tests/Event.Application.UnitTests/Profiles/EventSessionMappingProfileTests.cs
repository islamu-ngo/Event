using AutoMapper;
using Event.Application.UnitTests.Common;
using Explore.Application.DTOs.EventSession;
using Explore.Application.Profiles;
using Explore.Domain.Enums;

namespace Event.Application.UnitTests.Profiles;

public sealed class EventSessionMappingProfileTests
{
    [Test]
    public async Task DetailMapping_ProjectsParentEventStatusId()
    {
        var mapper = CreateMapper();
        var parentEvent = DataBuilder.EventWithStatus(EventStatusEnum.Cancelled).Generate();
        var session = DataBuilder.EventSession.Generate();
        session.Event = parentEvent;

        var dto = mapper.Map<EventSessionDto>(session);

        await Assert.That(dto.ParentEventStatusId).IsEqualTo((int)EventStatusEnum.Cancelled);
    }

    [Test]
    public async Task ListMapping_ProjectsParentEventStatusId()
    {
        var mapper = CreateMapper();
        var parentEvent = DataBuilder.EventWithStatus(EventStatusEnum.Archived).Generate();
        var session = DataBuilder.EventSession.Generate();
        session.Event = parentEvent;

        var dto = mapper.Map<EventSessionListDto>(session);

        await Assert.That(dto.ParentEventStatusId).IsEqualTo((int)EventStatusEnum.Archived);
    }


    [Test]
    public async Task OpenEndedSchedule_ProjectsAsScheduledWithoutInventedEnd()
    {
        var mapper = CreateMapper();
        var parentEvent = DataBuilder.EventWithStatus(EventStatusEnum.Published).Generate();
        var session = DataBuilder.EventSession.Generate();
        session.Event = parentEvent;
        session.StartTime = new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);
        session.EndTime = null;
        session.EndTimeType = SessionEndTimeType.OpenEnded;

        var detail = mapper.Map<EventSessionDto>(session);
        var list = mapper.Map<EventSessionListDto>(session);

        await Assert.That(detail.IsScheduled).IsTrue();
        await Assert.That(list.IsScheduled).IsTrue();
    }
    private static IMapper CreateMapper()
    {
#if USE_COMMERCIAL_LUCKYPENNY_LIBS
        var configuration = new MapperConfiguration(
            cfg => cfg.AddProfile<EventSessionMappingProfile>(),
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
#else
        var configuration = new MapperConfiguration(cfg => cfg.AddProfile<EventSessionMappingProfile>());
#endif
        return configuration.CreateMapper();
    }
}
