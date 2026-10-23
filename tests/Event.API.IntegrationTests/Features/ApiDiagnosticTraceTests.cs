using System.Collections.Concurrent;
using System.Diagnostics;
using Explore.API.Telemetry;
using Explore.Application.Contracts.Infrastructure.Ai;
using Explore.Domain.Ai;
using Explore.Infrastructure.Ai;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel]
public sealed class ApiDiagnosticTraceTests
{
    [Test]
    public async Task NativeAiTraceExportKeepsCountsWithoutInheritedPrivateContext()
    {
        string canary = Guid.CreateVersion7().ToString("N");
        var exporter = new Capture();
        using var provider = Sdk.CreateTracerProviderBuilder()
            .AddSource(AiProviderTelemetry.ActivitySourceName)
            .AddProcessor(new ApiDiagnosticTraceProcessor())
            .AddProcessor(new SimpleActivityExportProcessor(exporter)).Build();
        using var parent = new Activity("request-parent").Start();
        parent.SetBaggage("private", canary);
        parent.TraceStateString = $"private={canary}";
        var request = new AiChatPayload("model",
            [new AiChatMessage(AiMessageRole.User, canary)], canary,
            new AiChatOptions(100, 20, 0, 10, false, false));
        Task signal = exporter.Completed;
        using (Activity? activity = AiProviderTelemetry.StartRequest(AiProviderDefaults.ProviderOpenAi, request))
        {
            if (activity is null) throw new InvalidOperationException("AI activity listener was not registered.");
            await Assert.That(activity.GetBaggageItem("private")).IsEqualTo(canary);
            AiProviderTelemetry.MarkSuccess(activity,
                new AiChatResponse(canary, [], new AiTokenUsage(5, 3, 8), canary, "stop"));
        }
        await signal.WaitAsync(TimeSpan.FromSeconds(10));

        string exported = string.Join('\n', exporter.Values);
        await Assert.That(exported).DoesNotContain(canary);
        await Assert.That(exported).Contains("ai.provider=openai");
        await Assert.That(exported).Contains("ai.message.count=1");
        await Assert.That(exported).Contains("ai.tokens.total=8");
        await Assert.That(request.Messages.Single().Content).IsEqualTo(canary);
        await Assert.That(request.SystemPrompt).IsEqualTo(canary);
    }

    [Test]
    public async Task NativeHttpDependencyExportExcludesUrlsBaggageAndFailureContent()
    {
        string canary = Guid.CreateVersion7().ToString("N");
        var exporter = new Capture();
        using var provider = Sdk.CreateTracerProviderBuilder().AddSource("System.Net.Http")
            .AddProcessor(new ApiDiagnosticTraceProcessor())
            .AddProcessor(new SimpleActivityExportProcessor(exporter)).Build();
        using var source = new ActivitySource("System.Net.Http");
        Task signal = exporter.Completed;
        using (Activity? activity = source.StartActivity($"GET https://private.example.test/{canary}", ActivityKind.Client))
        {
            if (activity is null) throw new InvalidOperationException("HTTP activity listener was not registered.");
            activity.SetTag("http.request.method", "GET");
            activity.SetTag("http.response.status_code", 503);
            activity.SetTag("url.full", $"https://private.example.test/?token={canary}");
            activity.SetTag("exception.message", $"{canary}\r\nforged-event");
            activity.SetBaggage("private", canary);
            activity.TraceStateString = $"private={canary}";
            activity.SetStatus(ActivityStatusCode.Error, canary);
        }
        await signal.WaitAsync(TimeSpan.FromSeconds(10));
        string exported = string.Join('\n', exporter.Values);
        await Assert.That(exported).DoesNotContain(canary);
        await Assert.That(exported).DoesNotContain("private.example.test");
        await Assert.That(exported).Contains("http.response.status_code=503");
        await Assert.That(exported).Contains("http-dependency");
    }

    private sealed class Capture : BaseExporter<Activity>
    {
        private readonly ConcurrentQueue<string> _values = new();
        private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyCollection<string> Values => _values.ToArray();
        public Task Completed => _completed.Task;

        public override ExportResult Export(in Batch<Activity> batch)
        {
            foreach (Activity activity in batch)
            {
                _values.Enqueue(activity.DisplayName);
                _values.Enqueue(activity.StatusDescription ?? string.Empty);
                _values.Enqueue(activity.TraceStateString ?? string.Empty);
                foreach (var tag in activity.TagObjects)
                    _values.Enqueue($"{tag.Key}={tag.Value}");
                foreach (var baggage in activity.Baggage)
                    _values.Enqueue($"baggage:{baggage.Key}={baggage.Value}");
                foreach (var activityEvent in activity.Events)
                {
                    _values.Enqueue(activityEvent.Name);
                    foreach (var tag in activityEvent.Tags)
                        _values.Enqueue($"event:{tag.Key}={tag.Value}");
                }
                _completed.TrySetResult();
            }
            return ExportResult.Success;
        }
    }
}
