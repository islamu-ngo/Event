using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Application.Services.Moderation;

namespace Event.Application.UnitTests.Moderation;

public sealed class ObserveModerationProfileCollectionSubjectCommandTests
{
    [Test]
    [Arguments("00000000-0000-0000-0000-000000000000")]
    [Arguments("01234567-89ab-4cde-8fab-0123456789ab")]
    [Arguments("019a0000-0000-7000-0000-000000000001")]
    public async Task InvalidAggregateProfileCannotReachNativeObservation(string profile)
    {
        var response = await new ObserveModerationProfileCollectionSubjectCommandHandler(null!)
            .ExecuteAsync(Command() with { ProfileId = Guid.Parse(profile) });
        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.Errors!.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task UnknownModeCannotReachNativeObservation()
    {
        var response = await new ObserveModerationProfileCollectionSubjectCommandHandler(null!)
            .ExecuteAsync(Command() with { Mode = (ModerationNativeMode)int.MaxValue });
        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.Errors!.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task CancellationBeforeEntryCannotProduceAnObservation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.That(async () => await new ObserveModerationProfileCollectionSubjectCommandHandler(null!)
            .ExecuteAsync(Command(), cancellation.Token)).Throws<OperationCanceledException>();
    }

    private static ObserveModerationProfileCollectionSubjectCommand Command() => new()
    {
        ProfileId = Guid.Parse("019a0000-0000-7000-8000-000000000001"),
        Mode = ModerationNativeMode.Embedded
    };
}
