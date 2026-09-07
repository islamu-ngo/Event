// Tests that timeouts, connection errors, and SMTP 421/451/452 are retryable while permanent errors are not.

using Explore.Infrastructure.Mail;

namespace Explore.Infrastructure.Tests.Infrastructure;

public class EmailResiliencePipelinesTests
{
    // === Transient (retryable) errors ===

    [Test]
    public async Task IsTransient_Timeout_ReturnsTrue()
    {
        var result = EmailResiliencePipelines.IsTransient("Connection timeout after 30s");
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsTransient_TimeoutCaseInsensitive_ReturnsTrue()
    {
        var result = EmailResiliencePipelines.IsTransient("TIMEOUT waiting for server");
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsTransient_ConnectionError_ReturnsTrue()
    {
        var result = EmailResiliencePipelines.IsTransient("Connection refused by remote host");
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsTransient_Smtp421_ReturnsTrue()
    {
        var result = EmailResiliencePipelines.IsTransient("SMTP error (421): Service not available");
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsTransient_Smtp451_ReturnsTrue()
    {
        var result = EmailResiliencePipelines.IsTransient("SMTP error (451): Temporary failure");
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsTransient_Smtp452_ReturnsTrue()
    {
        var result = EmailResiliencePipelines.IsTransient("SMTP error (452): Insufficient storage");
        await Assert.That(result).IsTrue();
    }

    // === Permanent (non-retryable) errors ===

    [Test]
    public async Task IsTransient_AuthFailure_ReturnsFalse()
    {
        var result = EmailResiliencePipelines.IsTransient("Authentication failed: invalid credentials");
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsTransient_Smtp550_ReturnsFalse()
    {
        var result = EmailResiliencePipelines.IsTransient("SMTP error (550): Mailbox not found");
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsTransient_Smtp553_ReturnsFalse()
    {
        var result = EmailResiliencePipelines.IsTransient("SMTP error (553): Mailbox name not allowed");
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsTransient_GenericProtocolError_ReturnsFalse()
    {
        var result = EmailResiliencePipelines.IsTransient("SMTP protocol error: unexpected response");
        await Assert.That(result).IsFalse();
    }

    // === Edge cases ===

    [Test]
    public async Task IsTransient_NullMessage_ReturnsFalse()
    {
        var result = EmailResiliencePipelines.IsTransient(null);
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsTransient_EmptyMessage_ReturnsFalse()
    {
        var result = EmailResiliencePipelines.IsTransient("");
        await Assert.That(result).IsFalse();
    }

    // === Pipeline creation ===

    [Test]
    public async Task CreateSendPipeline_ReturnsNonNullPipeline()
    {
        var pipeline = EmailResiliencePipelines.CreateSendPipeline();
        await Assert.That(pipeline).IsNotNull();
    }
}
