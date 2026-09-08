using Explore.Application.Features.Authentication.Atproto.Models;
using Explore.Application.Features.Authentication.Atproto.Requests.Commands;
using Explore.Application.Features.Authentication.Atproto.Validators;
using Explore.Domain.ValueObjects;

namespace Event.Application.UnitTests.Features.Authentication.Atproto;

public sealed class BootstrapAtprotoSessionCommandValidatorTests
{
    [Test]
    public async Task ValidateAcceptsOmittedOrCompleteCanonicalActorTargetPair()
    {
        var validator = new BootstrapAtprotoSessionCommandValidator();

        var omitted = await validator.ValidateAsync(CreateCommand());
        var complete = await validator.ValidateAsync(CreateCommand(Guid.NewGuid(), Guid.NewGuid()));

        await Assert.That(omitted.IsValid).IsTrue();
        await Assert.That(complete.IsValid).IsTrue();
    }

    [Test]
    [Arguments(true, false)]
    [Arguments(false, true)]
    public async Task ValidateRejectsEmptyOrHalfCanonicalActorTargetPair(bool includeActorId, bool includeStamp)
    {
        var validator = new BootstrapAtprotoSessionCommandValidator();
        var result = await validator.ValidateAsync(CreateCommand(
            includeActorId ? Guid.NewGuid() : null,
            includeStamp ? Guid.NewGuid() : null));

        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async Task ValidateRejectsEmptyCanonicalActorId()
    {
        var result = await new BootstrapAtprotoSessionCommandValidator().ValidateAsync(CreateCommand(Guid.Empty, Guid.NewGuid()));

        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async Task ValidateRejectsDefaultTypedDid()
    {
        BootstrapAtprotoSessionCommand command = CreateCommand();
        var invalid = new BootstrapAtprotoSessionCommand(
            default,
            command.ExpectedPdsUri,
            command.OAuthClientKeyId,
            command.Classification,
            command.OAuthSessionPayload);

        var result = await new BootstrapAtprotoSessionCommandValidator().ValidateAsync(invalid);

        await Assert.That(result.IsValid).IsFalse();
    }

    private static BootstrapAtprotoSessionCommand CreateCommand(
        Guid? canonicalActorId = null,
        Guid? expectedCanonicalActorConcurrencyStamp = null) => new(
        AtprotoDid.Parse("did:plc:alice"),
        "https://pds.example/",
        "oauth-active",
        AtprotoSubjectClassification.Person,
        new byte[] { 1 },
        canonicalActorId,
        expectedCanonicalActorConcurrencyStamp);
}
