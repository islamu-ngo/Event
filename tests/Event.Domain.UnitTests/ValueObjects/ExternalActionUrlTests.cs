using Explore.Domain.ValueObjects;

namespace Event.Domain.UnitTests.ValueObjects;

public sealed class ExternalActionUrlTests
{
    [Test]
    public async Task Create_ValidHttpsUrl_NormalizesAndCapturesDestinationDomain()
    {
        var result = ExternalActionUrl.Create("  https://Events.Example.org:443/register?source=islamu  ");

        await Assert.That(result.Value).IsEqualTo("https://events.example.org/register?source=islamu");
        await Assert.That(result.DestinationDomain).IsEqualTo("events.example.org");
    }

    [Test]
    [Arguments("javascript:alert(1)")]
    [Arguments("data:text/html,unsafe")]
    [Arguments("file:///etc/passwd")]
    [Arguments("//events.example.org/register")]
    [Arguments("https://user:secret@events.example.org/register")]
    [Arguments("https://events.example.org/register#token")]
    public async Task Create_UnsafeUrl_Throws(string value)
    {
        await Assert.That(() => ExternalActionUrl.Create(value)).Throws<ArgumentException>();
    }
}
