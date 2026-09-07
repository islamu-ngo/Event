using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventSessionTemplateSync;
using Explore.Application.Features.EventSessionTemplateSync.Queries.GetEventSessionTemplateDiff;
using NSubstitute;

namespace Event.Application.UnitTests.Features.EventSessionTemplateSync;

public class GetEventSessionTemplateDiffQueryHandlerTests
{
    [Test]
    public async Task Handle_DelegatesToDiffServiceAndWrapsResponse()
    {
        var service = Substitute.For<IEventSessionTemplateDiffService>();
        var diff = new TemplateDiffDto(2, 1, [], [], [], [], [], [], []);
        service.ComputeDiffAsync(Arg.Any<Guid>(), 2, Arg.Any<CancellationToken>()).Returns(diff);
        var handler = new GetEventSessionTemplateDiffQueryHandler(service);

        var result = await handler.Handle(new GetEventSessionTemplateDiffQuery(Guid.NewGuid(), 2), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Id).IsEqualTo(diff);
    }
}
