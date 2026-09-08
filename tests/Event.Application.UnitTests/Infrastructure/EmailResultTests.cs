// property initialization, and success/failure behavior.

using Explore.Application.Models;

namespace Event.Application.UnitTests.Infrastructure;

public class EmailResultTests
{
    [Test]
    public async Task Ok_DefaultDuration_SetsSuccess()
    {
        var result = EmailResult.Ok("Sent successfully");

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Message).IsEqualTo("Sent successfully");
        await Assert.That(result.ErrorMessage).IsNull();
        await Assert.That(result.Duration).IsEqualTo(TimeSpan.Zero);
    }

    [Test]
    public async Task Ok_WithDuration_SetsDuration()
    {
        var duration = TimeSpan.FromMilliseconds(450);
        var result = EmailResult.Ok("Delivered", duration);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Duration).IsEqualTo(duration);
    }

    [Test]
    public async Task Ok_NullMessage_SetsSuccessWithNullMessage()
    {
        var result = EmailResult.Ok();

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Message).IsNull();
    }

    [Test]
    public async Task Fail_SetsErrorMessage()
    {
        var result = EmailResult.Fail("Connection refused");

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.ErrorMessage).IsEqualTo("Connection refused");
        await Assert.That(result.Message).IsNull();
    }

    [Test]
    public async Task Fail_WithDuration_SetsDuration()
    {
        var duration = TimeSpan.FromSeconds(5);
        var result = EmailResult.Fail("Timeout", duration);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Duration).IsEqualTo(duration);
    }
}
