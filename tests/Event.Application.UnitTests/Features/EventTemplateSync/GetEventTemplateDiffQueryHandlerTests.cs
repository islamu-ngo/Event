using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventTemplateSync;
using Explore.Application.Features.EventTemplateSync.Queries.GetEventTemplateDiff;
using NSubstitute;

namespace Event.Application.UnitTests.Features.EventTemplateSync;

public class GetEventTemplateDiffQueryHandlerTests
{
    [Test]
    public async Task Handle_DelegatesToDiffServiceAndWrapsResponse()
    {
        var service = Substitute.For<IEventTemplateDiffService>();
        var diff = new TemplateDiffDto(2, 1, [], [], [], [], [], [], []);
        service.ComputeDiffAsync(Arg.Any<Guid>(), 2, Arg.Any<CancellationToken>()).Returns(diff);
        var handler = new GetEventTemplateDiffQueryHandler(service);

        var result = await handler.Handle(new GetEventTemplateDiffQuery(Guid.NewGuid(), 2), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Id).IsEqualTo(diff);
    }
}
