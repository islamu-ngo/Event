using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.DTOs.Ai;
using Explore.Application.Responses;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class AiAssistantApiFlowTests(PostgreSqlContainerFixture postgres)
{

    [Test]
    public async Task Conversations_WithoutAuthentication_ReturnsUnauthorized()
    {
        await using var factory = await AiAssistantApiFlowFixture.CreateAsync(postgres);
        using var client = factory.Client;

        var response = await client.GetAsync("/api/ai/assistant/conversations");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task CreateConversation_WhenAssistantDisabled_ReturnsForbiddenProblemDetails()
    {
        await using var factory = await AiAssistantApiFlowFixture.CreateAsync(postgres, enabled: false);
        using var client = factory.Client;
        using var request = factory.CreateRequest(HttpMethod.Post, "/api/ai/assistant/conversations");
        request.Content = JsonContent.Create(new CreateAiConversationRequestDto { Title = "Planning" });

        var response = await factory.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden)
            .Because(factory.RequestFailure?.ToString() ?? await response.Content.ReadAsStringAsync());
        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        await Assert.That(json.RootElement.GetProperty("code").GetString()).IsEqualTo("disabled");
    }

    [Test]
    public async Task FakeProviderFlow_CreatesConversationSendsMessageAndReturnsHalDetail()
    {
        await using var factory = await AiAssistantApiFlowFixture.CreateAsync(postgres);
        using var client = factory.Client;
        var userId = factory.UserId;
        var conversationId = await CreateConversationAsync(factory, userId, "Planning");
        var runId = await SendMessageAsync(factory, userId, conversationId, "idem-flow", "Plan the opening night.");

        using var detailRequest = factory.CreateRequest(
            HttpMethod.Get,
            $"/api/ai/assistant/conversations/{conversationId}",
            userId);
        var response = await client.SendAsync(detailRequest);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = json.RootElement;
        await Assert.That(root.GetProperty("id").GetGuid()).IsEqualTo(conversationId);
        await Assert.That(root.GetProperty("messages").GetArrayLength()).IsEqualTo(2);
        await Assert.That(root.GetProperty("runs").GetArrayLength()).IsEqualTo(1);
        await Assert.That(root.GetProperty("runs")[0].GetProperty("id").GetGuid()).IsEqualTo(runId);
        await Assert.That(root.GetProperty("_links").TryGetProperty("send-message", out _)).IsTrue();
    }

    [Test]
    public async Task SendMessage_WithSameIdempotencyKeyAndPayload_ReplaysRunId()
    {
        await using var factory = await AiAssistantApiFlowFixture.CreateAsync(postgres);
        using var client = factory.Client;
        var userId = factory.UserId;
        var conversationId = await CreateConversationAsync(factory, userId, "Planning");
        var firstRunId = await SendMessageAsync(factory, userId, conversationId, "idem-replay", "Plan the event.");

        var replayRunId = await SendMessageAsync(factory, userId, conversationId, "idem-replay", "Plan the event.");

        await Assert.That(replayRunId).IsEqualTo(firstRunId);
    }

    [Test]
    public async Task SendMessage_WithSameIdempotencyKeyAndDifferentPayload_ReturnsConflictProblemDetails()
    {
        await using var factory = await AiAssistantApiFlowFixture.CreateAsync(postgres);
        using var client = factory.Client;
        var userId = factory.UserId;
        var conversationId = await CreateConversationAsync(factory, userId, "Planning");
        await SendMessageAsync(factory, userId, conversationId, "idem-conflict", "Plan the event.");

        using var request = factory.CreateRequest(
            HttpMethod.Post,
            $"/api/ai/assistant/conversations/{conversationId}/messages",
            userId);
        request.Headers.Add("Idempotency-Key", "idem-conflict");
        request.Content = JsonContent.Create(new SendAiMessageRequestDto { Content = "Plan a different event." });

        var response = await factory.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        await Assert.That(json.RootElement.GetProperty("code").GetString()).IsEqualTo("idempotency_key_conflict");
    }

    [Test]
    public async Task ReceiptStore_WhenDescriptorMismatches_DoesNotExposePriorReceipt()
    {
        await AiAssistantApiFlowFixture.AssertReceiptMismatchAsync(postgres);
    }

    [Test]
    public async Task Detail_WhenRequestedByDifferentUser_ReturnsNotFound()
    {
        await using var factory = await AiAssistantApiFlowFixture.CreateAsync(postgres);
        using var client = factory.Client;
        var ownerId = factory.UserId;
        var conversationId = await CreateConversationAsync(factory, ownerId, "Planning");

        using var request = factory.CreateRequest(
            HttpMethod.Get,
            $"/api/ai/assistant/conversations/{conversationId}",
            factory.OtherUserId);

        var response = await factory.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Detail_WhenRequestedFromDifferentTenant_ReturnsNotFound()
    {
        await using var factory = await AiAssistantApiFlowFixture.CreateAsync(postgres);
        using var client = factory.Client;
        var userId = factory.UserId;
        var conversationId = await CreateConversationAsync(factory, userId, "Planning", factory.TenantId);

        using var request = factory.CreateRequest(
            HttpMethod.Get,
            $"/api/ai/assistant/conversations/{conversationId}",
            userId,
            factory.OtherTenantId);

        var response = await factory.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task SendMessage_WhenProviderFails_ReturnsSafeServiceUnavailableProblemDetails()
    {
        await using var factory = await AiAssistantApiFlowFixture.CreateAsync(postgres, providerFails: true);
        using var client = factory.Client;
        var userId = factory.UserId;
        var conversationId = await CreateConversationAsync(factory, userId, "Planning");
        using var request = factory.CreateRequest(
            HttpMethod.Post,
            $"/api/ai/assistant/conversations/{conversationId}/messages",
            userId);
        request.Headers.Add("Idempotency-Key", "provider-failure");
        request.Content = JsonContent.Create(new SendAiMessageRequestDto { Content = "Plan the event." });

        var completed = factory.SubscribeRun(conversationId);
        var response = await factory.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>();
        await Assert.That(body).IsNotNull();

        await factory.AssertRunCompletedAsync(completed, body!.Id);
        var run = await ReadRunStatusAsync(factory, userId, conversationId, body.Id, "Failed");
        await Assert.That(run.GetProperty("failureCode").GetString()).IsEqualTo("provider_unreachable");
        await Assert.That(run.GetProperty("failureMessage").GetString()).DoesNotContain("Plan the event");
        await Assert.That(run.GetProperty("failureMessage").GetString()).DoesNotContain("api_key");
    }

    private static async Task<Guid> CreateConversationAsync(
        AiAssistantApiFlowFixture factory,
        Guid userId,
        string title,
        Guid? tenantId = null)
    {
        using var request = factory.CreateRequest(
            HttpMethod.Post,
            "/api/ai/assistant/conversations",
            userId,
            tenantId);
        request.Content = JsonContent.Create(new CreateAiConversationRequestDto { Title = title });

        var response = await factory.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created)
            .Because(factory.RequestFailure?.ToString() ?? await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>();
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.IsSuccess).IsTrue();
        return body.Id;
    }

    private static async Task<Guid> SendMessageAsync(
        AiAssistantApiFlowFixture factory,
        Guid userId,
        Guid conversationId,
        string idempotencyKey,
        string content,
        Guid? tenantId = null,
        string? expectedRunStatus = "Succeeded")
    {
        using var request = factory.CreateRequest(
            HttpMethod.Post,
            $"/api/ai/assistant/conversations/{conversationId}/messages",
            userId,
            tenantId);
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        request.Content = JsonContent.Create(new SendAiMessageRequestDto { Content = content });

        var completed = factory.SubscribeRun(conversationId);
        var response = await factory.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>();
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.IsSuccess).IsTrue();
        if (!string.IsNullOrWhiteSpace(expectedRunStatus))
        {
            await factory.AssertRunCompletedAsync(completed, body.Id);
            await ReadRunStatusAsync(factory, userId, conversationId, body.Id, expectedRunStatus, tenantId);
        }

        return body.Id;
    }

    private static async Task<JsonElement> ReadRunStatusAsync(
        AiAssistantApiFlowFixture factory, Guid userId, Guid conversationId, Guid runId,
        string expectedStatus, Guid? tenantId = null)
    {
        using var request = factory.CreateRequest(HttpMethod.Get,
            $"/api/ai/assistant/conversations/{conversationId}/runs/{runId}", userId, tenantId);
        using var response = await factory.Client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK)
            .Because(await response.Content.ReadAsStringAsync());
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var run = json.RootElement.Clone();
        await Assert.That(run.GetProperty("status").GetString()).IsEqualTo(expectedStatus);
        return run;
    }
}
