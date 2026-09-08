using System.Diagnostics;
using ISLAMU.Wire.Contracts.Admissions;

namespace ISLAMU.Wire.Contracts.UnitTests.Admissions;

public sealed class AdmissionQrPayloadCodecTests
{
    private static readonly string Bearer = Convert
        .ToBase64String(Enumerable.Range(0, 32).Select(value => (byte)value).ToArray())
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
    private static readonly string PayloadText =
        "islamu-admission:v1:" + Bearer;

    [Test]
    public async Task ExactV1PayloadRoundTrips()
    {
        bool bearerAccepted = AdmissionCredentialBearer.TryCreate(Bearer, out AdmissionCredentialBearer? bearer);
        string encoded = AdmissionQrPayloadCodec.Encode(bearer!);
        bool payloadAccepted = AdmissionQrPayloadCodec.TryDecode(encoded, out AdmissionQrPayload? payload);

        await Assert.That(bearerAccepted).IsTrue();
        await Assert.That(encoded).IsEqualTo(PayloadText);
        await Assert.That(encoded.Length).IsEqualTo(63);
        await Assert.That(payloadAccepted).IsTrue();
        await Assert.That(payload!.Bearer.Value).IsEqualTo(Bearer);
    }

    [Test]
    [MethodDataSource(nameof(MalformedPayloads))]
    public async Task MalformedAndUnknownPayloadsFailClosed(string candidate)
    {
        bool accepted = AdmissionQrPayloadCodec.TryDecode(candidate, out AdmissionQrPayload? payload);

        await Assert.That(accepted).IsFalse();
        await Assert.That(payload).IsNull();
    }

    public static IEnumerable<Func<string>> MalformedPayloads()
    {
        yield return () => string.Empty;
        yield return () => $"islamu-admission:v2:{Bearer}";
        yield return () => $"ISLAMU-admission:v1:{Bearer}";
        yield return () => $"islamu-admission:v1:{Bearer}=";
        yield return () => $"islamu-admission:v1: {Bearer}";
        yield return () => $"islamu-admission:v1:{Bearer[..^1]}+";
        yield return () => $"islamu-admission:v1:{Bearer[..^1]}";
        yield return () => $"islamu-admission:v1:{new string('_', Bearer.Length)}";
    }

    [Test]
    public async Task TokenBearingValuesRedactStringRepresentations()
    {
        AdmissionCredentialBearer.TryCreate(Bearer, out AdmissionCredentialBearer? bearer);
        AdmissionQrPayloadCodec.TryDecode(PayloadText, out AdmissionQrPayload? payload);

        string bearerDebugger = typeof(AdmissionCredentialBearer)
            .GetCustomAttributes(typeof(DebuggerDisplayAttribute), false)
            .Cast<DebuggerDisplayAttribute>()
            .Single().Value;
        string payloadDebugger = typeof(AdmissionQrPayload)
            .GetCustomAttributes(typeof(DebuggerDisplayAttribute), false)
            .Cast<DebuggerDisplayAttribute>()
            .Single().Value;

        await Assert.That(bearer!.ToString()).DoesNotContain(Bearer);
        await Assert.That(payload!.ToString()).DoesNotContain(Bearer);
        await Assert.That(bearerDebugger).DoesNotContain(Bearer);
        await Assert.That(payloadDebugger).DoesNotContain(Bearer);
    }
}
