using System.Text;
using Explore.Application.Contracts.Webhooks;
using Explore.Infrastructure.Webhooks;

namespace Explore.Infrastructure.Tests.Infrastructure.Webhooks;

public sealed class WebhookSignatureServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Verify_WhenSignatureMatchesRawBody_ReturnsSuccess()
    {
        var service = CreateService();
        var secret = CreateSecret("current-secret");
        const string payload = "{\"id\":\"msg_1\",\"data\":{\"value\":1}}";

        var headers = service.Sign("msg_1", FixedNow, System.Text.Encoding.UTF8.GetBytes(payload), secret);

        var result = service.Verify(Encoding.UTF8.GetBytes(payload), ToDictionary(headers), secret);

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Timestamp).IsEqualTo(FixedNow);
    }

    [Test]
    public async Task Sign_UsesStandardWebhookCanonicalBytes()
    {
        var service = CreateService();
        var secret = CreateSecret("current-secret");
        const string payload = "{\"id\":\"msg_1\",\"data\":{\"value\":1}}";

        var headers = service.Sign("msg_1", FixedNow, Encoding.UTF8.GetBytes(payload), secret);

        await Assert.That(headers.SvixId).IsEqualTo("msg_1");
        await Assert.That(headers.SvixTimestamp).IsEqualTo("1782993600");
        await Assert.That(headers.SvixSignature)
            .IsEqualTo("v1,Ba91+A0Gdt3Exnu5f11xNNtUMMdJYow1vbqdxMMrrM8=");
    }

    [Test]
    public async Task Verify_WhenPayloadChanges_ReturnsSignatureMismatch()
    {
        var service = CreateService();
        var secret = CreateSecret("current-secret");
        var headers = service.Sign("msg_1", FixedNow, "{\"value\":1}"u8, secret);

        var result = service.Verify(Encoding.UTF8.GetBytes("{\"value\":2}"), ToDictionary(headers), secret);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.FailureCategory).IsEqualTo("signature_mismatch");
    }

    [Test]
    public async Task Verify_PreservesUtf8WhitespaceAndNewlineBytesExactly()
    {
        var service = CreateService();
        var secret = CreateSecret("utf8-secret");
        var payload = Encoding.UTF8.GetBytes("{\"message\":\"السلام عليكم\",\"line\":\"one\\ntwo\"}\n");
        var headers = service.Sign("msg_utf8", FixedNow, payload, secret);
        var exact = service.Verify(payload, ToDictionary(headers), secret);
        var withTrailingSpace = service.Verify(
            [.. payload, (byte)' '],
            ToDictionary(headers),
            secret);
        var withoutFinalNewline = service.Verify(
            payload.AsSpan(0, payload.Length - 1),
            ToDictionary(headers),
            secret);

        await Assert.That(exact.IsValid).IsTrue();
        await Assert.That(withTrailingSpace.FailureCategory).IsEqualTo("signature_mismatch");
        await Assert.That(withoutFinalNewline.FailureCategory).IsEqualTo("signature_mismatch");
    }

    [Test]
    public async Task Verify_WhenSignedFieldsChange_ReturnsSignatureMismatch()
    {
        var service = CreateService();
        var secret = CreateSecret("field-secret");
        var headers = service.Sign("msg_original", FixedNow, "{}"u8, secret);
        var alteredIdHeaders = ToDictionary(headers);
        alteredIdHeaders["svix-id"] = "msg_changed";
        var alteredTimestampHeaders = ToDictionary(headers);
        alteredTimestampHeaders["svix-timestamp"] = FixedNow.AddSeconds(1)
            .ToUnixTimeSeconds()
            .ToString(System.Globalization.CultureInfo.InvariantCulture);

        var alteredId = service.Verify("{}"u8, alteredIdHeaders, secret);
        var alteredTimestamp = service.Verify("{}"u8, alteredTimestampHeaders, secret);

        await Assert.That(alteredId.FailureCategory).IsEqualTo("signature_mismatch");
        await Assert.That(alteredTimestamp.FailureCategory).IsEqualTo("signature_mismatch");
    }

    [Test]
    public async Task Verify_WhenTimestampOutsideTolerance_ReturnsTimestampFailure()
    {
        var service = CreateService();
        var secret = CreateSecret("current-secret");
        var headers = service.Sign("msg_1", FixedNow.AddMinutes(-10), "{}"u8, secret);

        var result = service.Verify(Encoding.UTF8.GetBytes("{}"), ToDictionary(headers), secret);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.FailureCategory).IsEqualTo("timestamp_outside_tolerance");
    }

    [Test]
    public async Task Verify_TimestampToleranceBoundariesAreInclusive()
    {
        var service = CreateService();
        var secret = CreateSecret("boundary-secret");
        var oldestAccepted = service.Sign("msg_oldest", FixedNow.Subtract(WebhookSignatureService.TimestampTolerance), "{}"u8, secret);
        var newestAccepted = service.Sign("msg_newest", FixedNow.Add(WebhookSignatureService.TimestampTolerance), "{}"u8, secret);
        var stale = service.Sign("msg_stale", FixedNow.Subtract(WebhookSignatureService.TimestampTolerance).AddSeconds(-1), "{}"u8, secret);
        var future = service.Sign("msg_future", FixedNow.Add(WebhookSignatureService.TimestampTolerance).AddSeconds(1), "{}"u8, secret);

        await Assert.That(service.Verify("{}"u8, ToDictionary(oldestAccepted), secret).IsValid).IsTrue();
        await Assert.That(service.Verify("{}"u8, ToDictionary(newestAccepted), secret).IsValid).IsTrue();
        await Assert.That(service.Verify("{}"u8, ToDictionary(stale), secret).FailureCategory)
            .IsEqualTo("timestamp_outside_tolerance");
        await Assert.That(service.Verify("{}"u8, ToDictionary(future), secret).FailureCategory)
            .IsEqualTo("timestamp_outside_tolerance");
    }

    [Test]
    public async Task Verify_IgnoresMalformedAndUnknownSignaturesWhenOneV1SignatureMatches()
    {
        var service = CreateService();
        var secret = CreateSecret("multiple-secret");
        var signed = service.Sign("msg_multiple", FixedNow, "{}"u8, secret);
        var headers = ToDictionary(signed);
        headers["svix-signature"] = $"v2,not-supported v1,%%% v1,{Convert.ToBase64String(new byte[32])} {signed.SvixSignature}";

        var result = service.Verify("{}"u8, headers, secret);

        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task Verify_WhenEverySignatureHasMalformedBase64_ReturnsMismatch()
    {
        var service = CreateService();
        var secret = CreateSecret("malformed-secret");
        var headers = ToDictionary(service.Sign("msg_malformed", FixedNow, "{}"u8, secret));
        headers["svix-signature"] = "v1,%%% v1,not-base64";

        var result = service.Verify("{}"u8, headers, secret);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.FailureCategory).IsEqualTo("signature_mismatch");
    }

    [Test]
    public async Task Verify_WhenPreviousSecretStillValid_AcceptsSignature()
    {
        var service = CreateService();
        var previousOnly = CreateSecret("previous-secret");
        var rotatedSecret = new WebhookSecretMaterial(
            CurrentSecret: CreateWhsec("current-secret"),
            CurrentSecretVersion: 2,
            PreviousSecret: previousOnly.CurrentSecret,
            PreviousSecretValidUntil: FixedNow.AddMinutes(1));

        var headers = service.Sign("msg_1", FixedNow, "{}"u8, previousOnly);

        var result = service.Verify(Encoding.UTF8.GetBytes("{}"), ToDictionary(headers), rotatedSecret);

        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task Sign_DuringRotationEmitsCurrentAndPreviousSignatures()
    {
        var service = CreateService();
        var currentOnly = CreateSecret("current-secret");
        var previousOnly = CreateSecret("previous-secret");
        var rotated = new WebhookSecretMaterial(
            currentOnly.CurrentSecret,
            CurrentSecretVersion: 2,
            PreviousSecret: previousOnly.CurrentSecret,
            PreviousSecretValidUntil: FixedNow.AddMinutes(1));

        var headers = service.Sign("msg_rotation", FixedNow, "{}"u8, rotated);

        await Assert.That(headers.SvixSignature.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length)
            .IsEqualTo(2);
        await Assert.That(service.Verify("{}"u8, ToDictionary(headers), currentOnly).IsValid).IsTrue();
        await Assert.That(service.Verify("{}"u8, ToDictionary(headers), previousOnly).IsValid).IsTrue();
    }

    [Test]
    public async Task Verify_WhenPreviousSecretExpired_RejectsItsSignature()
    {
        var service = CreateService();
        var previousOnly = CreateSecret("previous-secret");
        var rotated = new WebhookSecretMaterial(
            CurrentSecret: CreateWhsec("current-secret"),
            CurrentSecretVersion: 2,
            PreviousSecret: previousOnly.CurrentSecret,
            PreviousSecretValidUntil: FixedNow.AddSeconds(-1));
        var headers = service.Sign("msg_expired", FixedNow, "{}"u8, previousOnly);

        var result = service.Verify("{}"u8, ToDictionary(headers), rotated);

        await Assert.That(result.FailureCategory).IsEqualTo("signature_mismatch");
    }

    private static WebhookSignatureService CreateService() =>
        new(new FixedTimeProvider(FixedNow));

    private static WebhookSecretMaterial CreateSecret(string value) =>
        new(CreateWhsec(value), CurrentSecretVersion: 1);

    private static string CreateWhsec(string value) =>
        $"whsec_{Convert.ToBase64String(Encoding.UTF8.GetBytes(value))}";

    private static Dictionary<string, string> ToDictionary(WebhookSignatureHeaders headers) => new(StringComparer.OrdinalIgnoreCase)
    {
        ["svix-id"] = headers.SvixId,
        ["svix-timestamp"] = headers.SvixTimestamp,
        ["svix-signature"] = headers.SvixSignature
    };

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
