using Explore.Application.Notifications;
using Explore.Domain;

namespace Event.Application.UnitTests.Features.Users;

public sealed class IdentityEmailResolutionTests
{
    private static readonly DateTime ObservedAt = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task RawProviderVerificationCannotAuthorizeAnUnsupportedContactAddress(bool verified)
    {
        User user = CreateUser(verified);

        RecipientEmailAddressResolution result = RecipientEmailAddressResolver.Resolve(user, user.Id);

        await Assert.That(result.HasVerifiedEmail).IsFalse();
        await Assert.That(user.EmailVerified).IsEqualTo(verified);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task IndependentProofSurvivesConflictingContactAndDifferentProviderVerification(bool rawVerified)
    {
        User user = CreateUser(rawVerified);
        user.IdentityEmailClaims = [SupportedClaim(user.Id, "supported@example.test")];

        RecipientEmailAddressResolution result = RecipientEmailAddressResolver.Resolve(user, user.Id);

        await Assert.That(result.Email).IsEqualTo("supported@example.test");
        await Assert.That(result.HasVerifiedEmail).IsTrue();
        await Assert.That(user.EmailVerified).IsEqualTo(rawVerified);
    }

    [Test]
    public async Task InvalidatingTheLastProofStopsDeliveryWithoutRewritingTheProviderFact()
    {
        User user = CreateUser(true);
        UserIdentityEmailClaim claim = SupportedClaim(user.Id, "supported@example.test");
        user.IdentityEmailClaims = [claim];
        await Assert.That(RecipientEmailAddressResolver.Resolve(user, user.Id).HasVerifiedEmail).IsTrue();

        claim.Evidence.Single().Invalidate();

        await Assert.That(RecipientEmailAddressResolver.Resolve(user, user.Id).HasVerifiedEmail).IsFalse();
        await Assert.That(user.EmailVerified).IsEqualTo(true);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task AnotherAccountsClaimOrProofCannotAuthorizeTheRecipient(bool differentClaimOwner)
    {
        User user = CreateUser(true);
        Guid otherId = Guid.CreateVersion7();
        UserIdentityEmailClaim claim = UserIdentityEmailClaim.Create(
            differentClaimOwner ? otherId : user.Id, "another@example.test");
        claim.Evidence = [UserIdentityEmailEvidence.Create(otherId, claim.Id, Guid.CreateVersion7(), ObservedAt)];
        user.IdentityEmailClaims = [claim];

        await Assert.That(RecipientEmailAddressResolver.Resolve(user, user.Id).HasVerifiedEmail).IsFalse();
    }

    [Test]
    public async Task AnOwnershipReservationWithoutProofCannotAuthorizeDelivery()
    {
        User user = CreateUser(true);
        user.IdentityEmailClaims = [UserIdentityEmailClaim.Create(user.Id, "reserved@example.test")];

        await Assert.That(RecipientEmailAddressResolver.Resolve(user, user.Id).HasVerifiedEmail).IsFalse();
    }

    [Test]
    public async Task RecipientProjectionPrefersContactMatchAndRejectsDeletedMissingOrUnverified()
    {
        Guid userId = Guid.CreateVersion7();
        Guid lowerClaimId = Guid.Parse("01900000-0000-7000-8000-000000000001");
        Guid higherClaimId = Guid.Parse("01900000-0000-7000-8000-000000000002");

        await Assert.That(RecipientEmailAddressResolver.Resolve((EmailRecipient?)null, userId).SkipReason)
            .IsEqualTo(RecipientEmailAddressResolver.RecipientDeletedOrMissing);
        await Assert.That(RecipientEmailAddressResolver.Resolve(
            new EmailRecipient(Guid.CreateVersion7(), false, "a@example.test", [new(lowerClaimId, "a@example.test")]), userId).SkipReason)
            .IsEqualTo(RecipientEmailAddressResolver.RecipientDeletedOrMissing);
        await Assert.That(RecipientEmailAddressResolver.Resolve(
            new EmailRecipient(userId, true, "a@example.test", [new(lowerClaimId, "a@example.test")]), userId).SkipReason)
            .IsEqualTo(RecipientEmailAddressResolver.RecipientDeletedOrMissing);
        await Assert.That(RecipientEmailAddressResolver.Resolve(
            new EmailRecipient(userId, false, "a@example.test", []), userId).SkipReason)
            .IsEqualTo(RecipientEmailAddressResolver.RecipientEmailUnverified);

        RecipientEmailAddressResolution contactPreferred = RecipientEmailAddressResolver.Resolve(
            new EmailRecipient(userId, false, "  PREFERRED@Example.Test ",
            [
                new(lowerClaimId, "other@example.test"),
                new(higherClaimId, "preferred@example.test")
            ]),
            userId);
        await Assert.That(contactPreferred.HasVerifiedEmail).IsTrue();
        await Assert.That(contactPreferred.Email).IsEqualTo("preferred@example.test");

        RecipientEmailAddressResolution lowestIdFallback = RecipientEmailAddressResolver.Resolve(
            new EmailRecipient(userId, false, "unmatched@example.test",
            [
                new(higherClaimId, "second@example.test"),
                new(lowerClaimId, "first@example.test")
            ]),
            userId);
        await Assert.That(lowestIdFallback.HasVerifiedEmail).IsTrue();
        await Assert.That(lowestIdFallback.Email).IsEqualTo("first@example.test");
    }

    private static User CreateUser(bool verified) => new()
    {
        Id = Guid.CreateVersion7(),
        Pii = new UserPii
        {
            Email = "unsupported-contact@example.test",
            FirstName = "Identity",
            LastName = "Owner"
        },
        EmailVerified = verified
    };

    private static UserIdentityEmailClaim SupportedClaim(Guid userId, string address)
    {
        UserIdentityEmailClaim claim = UserIdentityEmailClaim.Create(userId, address);
        claim.Evidence = [UserIdentityEmailEvidence.Create(userId, claim.Id, Guid.CreateVersion7(), ObservedAt)];
        return claim;
    }
}
