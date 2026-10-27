using Explore.Domain.Enums;
using ISLAMU.Moderation.Domain.Policies;

namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationRecognitionTokenTests
{
    [Test]
    [Arguments("trust")]
    [Arguments("provider")]
    [Arguments("purpose")]
    [Arguments("key")]
    [Arguments("generation")]
    [Arguments("version")]
    [Arguments("digest")]
    public async Task Unsupported_or_ambiguous_retained_token_coordinates_reject(string change)
    {
        await Assert.That(() => new ModerationRecognitionToken(
            change == "trust" ? Guid.Empty : Guid.CreateVersion7(),
            change == "provider" ? AuthenticationProviderKind.Development : AuthenticationProviderKind.Keycloak,
            change == "purpose" ? ModerationReferenceRecognition.Never : ModerationReferenceRecognition.SeparatelyQualifiedFinalAction,
            change == "key" ? "key:ambiguous" : "key-one",
            change == "generation" ? 0 : 7,
            change == "version" ? 2 : 1,
            change == "digest" ? new string('A', 64) : new string('a', 64))).Throws<ArgumentException>();
    }
}
