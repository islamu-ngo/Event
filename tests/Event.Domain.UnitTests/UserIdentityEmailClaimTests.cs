namespace Event.Domain.UnitTests;

public sealed class UserIdentityEmailClaimTests
{
    [Test]
    public async Task IdentityAddressPreservesAliasesAndHasOneImmutableOwner()
    {
        Guid ownerId = Guid.CreateVersion7();
        UserIdentityEmailClaim claim = UserIdentityEmailClaim.Create(ownerId, "first.last+events@example.test");

        await Assert.That(claim.UserId).IsEqualTo(ownerId);
        await Assert.That(claim.NormalizedEmail).IsEqualTo("first.last+events@example.test");
        await Assert.That(claim.Id.Version).IsEqualTo(7);
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("person@example.test ")]
    [Arguments("Person@example.test")]
    public async Task NoncanonicalContactTextCannotBecomeAnIdentityClaim(string address)
    {
        await Assert.That(() => UserIdentityEmailClaim.Create(Guid.CreateVersion7(), address))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task ClaimRequiresAnAccountOwner()
    {
        await Assert.That(() => UserIdentityEmailClaim.Create(Guid.Empty, "person@example.test"))
            .Throws<ArgumentException>();
    }
}
