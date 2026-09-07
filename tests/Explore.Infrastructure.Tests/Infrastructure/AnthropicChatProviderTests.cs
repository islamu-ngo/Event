using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure.Ai;
using Explore.Application.Telemetry;
using Explore.Domain.Ai;
using Explore.Infrastructure.Ai;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class AnthropicChatProviderTests
{
    [Test]
    public async Task SendAsync_WhenUserMessageHasImage_PostsOfficialAnthropicMessagesRequest()
    {
        var handler = new RecordingMessageHandler(_ => JsonResponse("""
            {
              "id": "msg_test",
              "type": "message",
              "role": "assistant",
              "content": [
                { "type": "text", "text": "This is an event flyer." }
              ],
              "model": "claude-test",
              "stop_reason": "end_turn",
              "usage": { "input_tokens": 20, "output_tokens": 8 }
            }
            """, response => response.Headers.Add("x-request-id", "req_test")));
        var provider = CreateProvider(handler, CreateSettings(apiKey: "secret-key"));
        const string imageData = "aW1hZ2UtYnl0ZXM=";

        var result = await provider.SendAsync(CreateRequest(
        [
            new AiChatMessage(
                AiMessageRole.User,
                "Describe this picture:",
                images:
                [
                    new AiChatImage("image/jpeg", imageData)
                ])
        ]));

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(handler.RequestUri).IsEqualTo(new Uri("https://api.anthropic.com/v1/messages"));
        await Assert.That(handler.RequestHeaders!.GetValues("x-api-key").Single()).IsEqualTo("secret-key");
        await Assert.That(handler.RequestHeaders.GetValues("anthropic-version").Single()).IsEqualTo("2023-06-01");
        await Assert.That(result.Response!.ProviderRequestId).IsEqualTo("req_test");
        await Assert.That(result.Response.AssistantMessage).IsEqualTo("This is an event flyer.");

        using var document = JsonDocument.Parse(handler.RequestBody!);
        var root = document.RootElement;
        await Assert.That(root.GetProperty("model").GetString()).IsEqualTo("claude-test");
        await Assert.That(root.GetProperty("system").GetString()).IsEqualTo("You are a safe assistant.");
        await Assert.That(root.TryGetProperty("chat_template_kwargs", out _)).IsFalse();

        var content = root.GetProperty("messages")[0].GetProperty("content");
        await Assert.That(content[0].GetProperty("type").GetString()).IsEqualTo("text");
        await Assert.That(content[0].GetProperty("text").GetString()).IsEqualTo("Describe this picture:");

        var source = content[1].GetProperty("source");
        await Assert.That(content[1].GetProperty("type").GetString()).IsEqualTo("image");
        await Assert.That(source.GetProperty("type").GetString()).IsEqualTo("base64");
        await Assert.That(source.GetProperty("media_type").GetString()).IsEqualTo("image/jpeg");
        await Assert.That(source.GetProperty("data").GetString()).IsEqualTo(imageData);
    }

    private static AnthropicChatProvider CreateProvider(
        HttpMessageHandler handler,
        AiProviderSettings settings)
    {
        var client = new HttpClient(handler);
        var factory = new StaticHttpClientFactory(client);
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter(BusinessMetrics.MeterName));

        return new AnthropicChatProvider(
            factory,
            Options.Create(settings),
            new AiProviderSettingsValidator(),
            new BusinessMetrics(meterFactory));
    }

    private static AiProviderSettings CreateSettings(string apiKey = "test-key") => new()
    {
        Enabled = true,
        Provider = AiProviderSettings.ProviderAnthropic,
        ApiKey = apiKey,
        ModelId = "claude-test",
        MaxInputTokens = 8000,
        MaxOutputTokens = 1024,
        TimeoutSeconds = 30
    };

    private static AiChatPayload CreateRequest(IReadOnlyList<AiChatMessage> messages) => new(
        "claude-test",
        messages,
        "You are a safe assistant.",
        new AiChatOptions(
            8000,
            1024,
            0.2m,
            30,
            ToolProposalsEnabled: false,
            StreamingEnabled: false));

    private static HttpResponseMessage JsonResponse(
        string json,
        Action<HttpResponseMessage>? configure = null,
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        configure?.Invoke(response);
        return response;
    }

    private sealed class StaticHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public System.Net.Http.Headers.HttpRequestHeaders? RequestHeaders { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestHeaders = request.Headers;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return responseFactory(request);
        }
    }
}
