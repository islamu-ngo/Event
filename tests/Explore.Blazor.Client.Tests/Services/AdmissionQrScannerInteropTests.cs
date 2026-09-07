using System.Diagnostics;
using System.Text.Json;
using Explore.Blazor.Client.Contracts.Interop;
using Explore.Blazor.Client.Extensions;
using Explore.Blazor.Client.Services.Interop;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class AdmissionQrScannerInteropTests
{
    private const string Bearer = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8";
    private const string Payload = "islamu-admission:v1:" + Bearer;

    [Test]
    public async Task CapabilityKeepsHidAndManualAvailableWhenNativeQrIsUnsupported()
    {
        await using var scanner = Scanner(new AdmissionQrNativeResult(AdmissionQrNativeStatus.Unsupported));

        AdmissionQrScannerCapability capability = await scanner.GetCapabilityAsync();

        await Assert.That(capability.NativeQrAvailable).IsFalse();
        await Assert.That(capability.HidInputAvailable).IsTrue();
        await Assert.That(capability.ManualInputAvailable).IsTrue();
    }

    [Test]
    [Arguments(AdmissionQrNativeStatus.Supported, true)]
    [Arguments(AdmissionQrNativeStatus.Unsupported, false)]
    public async Task NativeSupportRequiresExplicitQrCodeFormat(AdmissionQrNativeStatus status, bool expected)
    {
        await using var scanner = Scanner(new AdmissionQrNativeResult(status));

        AdmissionQrScannerCapability capability = await scanner.GetCapabilityAsync();

        await Assert.That(capability.NativeQrAvailable).IsEqualTo(expected);
    }

    [Test]
    public async Task DetectionReturnsNoCodeMultipleInvalidAndSingleValidOutcomes()
    {
        var runtime = new SequencedRuntime(
            new AdmissionQrNativeResult(AdmissionQrNativeStatus.NoCode),
            new AdmissionQrNativeResult(AdmissionQrNativeStatus.Multiple),
            new AdmissionQrNativeResult(AdmissionQrNativeStatus.Single, "not-a-payload"),
            new AdmissionQrNativeResult(AdmissionQrNativeStatus.Single, Payload));
        await using var scanner = Scanner(runtime);
        var imageSource = default(ElementReference);

        AdmissionQrScanResult none = await scanner.DetectAsync(imageSource);
        AdmissionQrScanResult multiple = await scanner.DetectAsync(imageSource);
        AdmissionQrScanResult invalid = await scanner.DetectAsync(imageSource);
        AdmissionQrScanResult valid = await scanner.DetectAsync(imageSource);

        await Assert.That(none.Outcome).IsEqualTo(AdmissionQrScanOutcome.NoCode);
        await Assert.That(multiple.Outcome).IsEqualTo(AdmissionQrScanOutcome.MultipleAmbiguous);
        await Assert.That(invalid.Outcome).IsEqualTo(AdmissionQrScanOutcome.Invalid);
        await Assert.That(invalid.Credential).IsNull();
        await Assert.That(valid.Outcome).IsEqualTo(AdmissionQrScanOutcome.SingleValid);
        await Assert.That(valid.Credential!.Value).IsEqualTo(Bearer);
        string debuggerDisplay = typeof(AdmissionQrScanResult)
            .GetCustomAttributes(typeof(DebuggerDisplayAttribute), false)
            .Cast<DebuggerDisplayAttribute>()
            .Single().Value;
        await Assert.That(valid.ToString()).DoesNotContain(Bearer);
        await Assert.That(debuggerDisplay).DoesNotContain(Bearer);
    }

    [Test]
    public async Task DisconnectAndCancellationFailClosedWithoutCredentialMaterial()
    {
        await using var disconnected = Scanner(new ThrowingRuntime(new JSDisconnectedException("gone")));
        await using var cancelled = Scanner(new ThrowingRuntime(new TaskCanceledException("cancelled")));

        AdmissionQrScanResult disconnectedResult = await disconnected.DetectAsync(default);
        AdmissionQrScanResult cancelledResult = await cancelled.DetectAsync(default);

        await Assert.That(disconnectedResult.Outcome).IsEqualTo(AdmissionQrScanOutcome.Failure);
        await Assert.That(cancelledResult.Outcome).IsEqualTo(AdmissionQrScanOutcome.Failure);
        await Assert.That(disconnectedResult.Credential).IsNull();
        await Assert.That(cancelledResult.Credential).IsNull();
    }

    [Test]
    public async Task NativeFailureFailsClosedAndSharedProductionRegistrationUsesTypedScanner()
    {
        await using var scanner = Scanner(new AdmissionQrNativeResult(AdmissionQrNativeStatus.Failure));
        AdmissionQrScanResult result = await scanner.DetectAsync(default);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedApplicationServices();
        ServiceDescriptor registration = services.Single(descriptor =>
            descriptor.ServiceType == typeof(IAdmissionQrScanner));

        await Assert.That(result.Outcome).IsEqualTo(AdmissionQrScanOutcome.Failure);
        await Assert.That(result.Credential).IsNull();
        await Assert.That(registration.ImplementationType).IsEqualTo(typeof(AdmissionQrScannerInterop));
        await Assert.That(registration.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
    }

    [Test]
    public async Task MalformedAndNullNativeRepliesFailClosedWithoutCredentialMaterial()
    {
        AdmissionQrNativeResult missingStatus =
            JsonSerializer.Deserialize<AdmissionQrNativeResult>("{}")!;
        AdmissionQrNativeResult canonicalStatus =
            JsonSerializer.Deserialize<AdmissionQrNativeResult>("{\"status\":\"noCode\"}")!;
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<AdmissionQrNativeResult>("{\"status\":\"unknown\"}"));
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<AdmissionQrNativeResult>("{\"status\":1}"));
        await using var malformed = Scanner(new ThrowingRuntime(new JsonException("unknown native status")));
        await using var nullReply = Scanner(new NullReplyRuntime());

        AdmissionQrScannerCapability malformedCapability = await malformed.GetCapabilityAsync();
        AdmissionQrScanResult malformedResult = await malformed.DetectAsync(default);
        AdmissionQrScannerCapability nullCapability = await nullReply.GetCapabilityAsync();
        AdmissionQrScanResult nullResult = await nullReply.DetectAsync(default);

        await Assert.That(missingStatus.Status).IsEqualTo(AdmissionQrNativeStatus.Unknown);
        await Assert.That(canonicalStatus.Status).IsEqualTo(AdmissionQrNativeStatus.NoCode);
        await Assert.That(malformedCapability.NativeQrAvailable).IsFalse();
        await Assert.That(malformedResult.Outcome).IsEqualTo(AdmissionQrScanOutcome.Failure);
        await Assert.That(malformedResult.Credential).IsNull();
        await Assert.That(nullCapability.NativeQrAvailable).IsFalse();
        await Assert.That(nullResult.Outcome).IsEqualTo(AdmissionQrScanOutcome.Failure);
        await Assert.That(nullResult.Credential).IsNull();
    }

    private static AdmissionQrScannerInterop Scanner(AdmissionQrNativeResult reply) => Scanner(new SequencedRuntime(reply));

    private static AdmissionQrScannerInterop Scanner(IJSRuntime runtime) =>
        new(runtime, Substitute.For<ILogger<AdmissionQrScannerInterop>>());

    private sealed class SequencedRuntime(params AdmissionQrNativeResult[] replies) : IJSRuntime
    {
        private readonly Queue<AdmissionQrNativeResult> queue = new(replies);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            ValueTask.FromResult((TValue)(object)new ReplyModule(queue));
    }

    private sealed class ReplyModule(Queue<AdmissionQrNativeResult> queue) : IJSObjectReference
    {

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            ValueTask.FromResult((TValue)(object)queue.Dequeue());
    }

    private sealed class ThrowingRuntime(Exception exception) : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromException<TValue>(exception);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            ValueTask.FromException<TValue>(exception);
    }

    private sealed class NullReplyRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args) =>
            ValueTask.FromResult((TValue)(object)new NullReplyModule());
    }

    private sealed class NullReplyModule : IJSObjectReference
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);
    }
}
