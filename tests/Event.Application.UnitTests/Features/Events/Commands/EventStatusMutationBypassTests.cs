using Explore.Application.DTOs.Event;
using Explore.Application.Features.Events.Requests.Commands;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Application.UnitTests.Features.Events.Commands;

public sealed class EventStatusMutationBypassTests
{
    [Test]
    public async Task UpdateEventDto_DoesNotExposeEventStatusId()
    {
        var statusProperty = typeof(UpdateEventDto).GetProperty("EventStatusId");

        await Assert.That(statusProperty).IsNull();
    }

    [Test]
    public async Task UpdateEventCommand_DoesNotExposeGenericStatusDto()
    {
        var statusDtoProperty = typeof(UpdateEventCommand).GetProperty("EventStatusDto");

        await Assert.That(statusDtoProperty).IsNull();
    }
}
