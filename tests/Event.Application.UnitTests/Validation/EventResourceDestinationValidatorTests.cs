using Explore.Application.Validation;
using Explore.Domain.ValueObjects;

namespace Event.Application.UnitTests.Validation;

public sealed class EventResourceDestinationValidatorTests
{
    private static readonly EventResourceGovernancePolicy Policy = EventResourceGovernancePolicy.Create(
        Enum.GetValues<Explore.Domain.Enums.EventResourceDeliveryTypeEnum>(),
        Enum.GetValues<Explore.Domain.Enums.EventResourceAudienceKindEnum>(),
        [EventResourceGovernancePolicy.PdfMediaType],
        1024,
        false,
        ["https://files.example.com", "https://files.example.com:8443"],
        30,
        500,
        1024);

    [Test]
    public async Task ValidatedDestinationRetainsPathAndQueryWhileExposingOnlyItsSafeOrigin()
    {
        string secret = Guid.CreateVersion7().ToString("N");
        string input = $"https://files.example.com:8443/share/file.pdf?token={secret}#page=2";

        bool accepted = EventResourceDestinationValidator.TryValidate(
            input, Policy, out string? destination, out string? safeOrigin);

        await Assert.That(accepted).IsTrue();
        await Assert.That(destination).IsEqualTo(input);
        await Assert.That(safeOrigin).IsEqualTo("https://files.example.com:8443");
        await Assert.That(safeOrigin).DoesNotContain(secret);
    }

    [Test]
    [Arguments("http://files.example.com/share")]
    [Arguments("javascript:alert(1)")]
    [Arguments("//files.example.com/share")]
    [Arguments("https://reader:password@files.example.com/share")]
    [Arguments("https://files.example.com.evil.test/share")]
    [Arguments("https://sub.files.example.com/share")]
    [Arguments("https://files.example.com:9443/share")]
    [Arguments("https://files.example.com./share")]
    [Arguments("https://files.example.com\\@evil.test/share")]
    [Arguments("https://127.0.0.1/share")]
    [Arguments("https://localhost/share")]
    [Arguments("https://files.example.com/path\r\nheader: value")]
    [Arguments("https://files.example.com/\u202Edesu")]
    [Arguments("https://files.example.com/\u200Bdesu")]
    public async Task HostileDestinationIsRejectedWithoutEcho(string input)
    {
        bool accepted = EventResourceDestinationValidator.TryValidate(
            input, Policy, out string? destination, out string? safeOrigin);

        await Assert.That(accepted).IsFalse();
        await Assert.That(destination).IsNull();
        await Assert.That(safeOrigin).IsNull();
    }

    [Test]
    public async Task OversizedDestinationIsRejected()
    {
        bool accepted = EventResourceDestinationValidator.TryValidate(
            $"https://files.example.com/{new string('a', 4097)}", Policy,
            out string? destination, out string? safeOrigin);

        await Assert.That(accepted).IsFalse();
        await Assert.That(destination).IsNull();
        await Assert.That(safeOrigin).IsNull();
    }
}
