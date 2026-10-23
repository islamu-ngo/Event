using Explore.Application.Contracts.Identity;
using Explore.Application.Exceptions;
using Explore.Application.Features.Users.Handlers.Commands;

namespace Event.Application.UnitTests.Operations;

public sealed class ProfileImageMutationTests
{
    [Test]
    public async Task UnauthenticatedImageMutationCannotUseAClaimedTargetOrReachRepositories()
    {
        var handler = new UpdateUserProfileImageCommandHandler(null!, null!, null!, null!, null!,
            null!, null!, new UnauthenticatedCaller(), null!, null!, null!, null!);
        await Assert.That(async () => await handler.ExecuteAsync(new()
        {
            UserId = Guid.CreateVersion7(), ExpectedConcurrencyStamp = Guid.CreateVersion7(), ProfileImage = new()
        })).Throws<AuthorizationException>();
    }

    private sealed class UnauthenticatedCaller : IUserContext
    {
        public Guid? UserId { get; } = Guid.CreateVersion7();
        public bool IsAuthenticated => false;
        public string? Email => null;
        public string? Username => null;
        public Guid GetRequiredUserId() => throw new UnauthorizedAccessException();
    }
}
