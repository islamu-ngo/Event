using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.User;
using Explore.Application.Features.Users.Handlers.Commands;
using Explore.Application.Features.Users.Requests.Commands;
using Explore.Application.Notifications;
using Explore.Application.Responses;
using Explore.Domain;
using Microsoft.Extensions.Caching.Hybrid;
using NSubstitute;

namespace Event.Application.UnitTests.Features.Users;

public sealed class UserProfileOwnershipTests
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task EditingNamesCannotManufactureOrReplaceVerifiedIdentityAuthority(bool supported)
    {
        Guid userId = Guid.CreateVersion7();
        var user = new User
        {
            Id = userId,
            ConcurrencyStamp = Guid.CreateVersion7(),
            Pii = new UserPii
            {
                FirstName = "Original",
                LastName = "Profile",
                Email = "shared-contact@example.test"
            },
            EmailVerified = false
        };
        UserIdentityEmailClaim claim = UserIdentityEmailClaim.Create(userId, "authority@example.test");
        UserIdentityEmailEvidence proof = UserIdentityEmailEvidence.Create(
            userId, claim.Id, Guid.CreateVersion7(), new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc));
        if (!supported)
            proof.Invalidate();
        claim.Evidence = [proof];
        user.IdentityEmailClaims = [claim];
        var users = Substitute.For<IUserRepository>();
        users.GetById(userId).Returns(user);
        var transactions = Substitute.For<IUnitOfWork>();
        transactions.ExecuteInTransactionAsync(
            Arg.Any<Func<CancellationToken, Task<BaseCommandResponse<Guid>>>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<Func<CancellationToken, Task<BaseCommandResponse<Guid>>>>(0)(
                call.ArgAt<CancellationToken>(1)));
        var handler = new UpdateUserCommandHandler(
            users, Substitute.For<IActorRepository>(), Substitute.For<IStorageObjectRepository>(),
            Substitute.For<IPrivacyErasureStateRepository>(), Substitute.For<ITenantContext>(),
            transactions, Substitute.For<HybridCache>());
        UpdateUserDto input = JsonSerializer.Deserialize<UpdateUserDto>("""
            {"Names":{"FirstName":"Chosen","LastName":"Family","Email":"forged@example.test","EmailVerified":true},
             "Email":"forged@example.test","EmailVerified":true}
            """)!;

        BaseCommandResponse<Guid> result = await handler.ExecuteAsync(new UpdateUserCommand
        {
            UserId = userId,
            ExpectedConcurrencyStamp = user.ConcurrencyStamp,
            UpdateUserDto = input
        }, CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(user.FirstName).IsEqualTo("Chosen");
        await Assert.That(user.LastName).IsEqualTo("Family");
        await Assert.That(user.Email).IsEqualTo("shared-contact@example.test");
        await Assert.That(user.EmailVerified).IsEqualTo(false);
        RecipientEmailAddressResolution recipient = RecipientEmailAddressResolver.Resolve(user, userId);
        await Assert.That(recipient.HasVerifiedEmail).IsEqualTo(supported);
        await Assert.That(recipient.Email).IsEqualTo(supported ? "authority@example.test" : null);
    }
}
