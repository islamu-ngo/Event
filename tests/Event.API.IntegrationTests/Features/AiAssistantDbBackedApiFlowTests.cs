using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.Application.Contracts.Infrastructure.Ai;
using ExploreDbContext = Explore.Persistence.ExploreDbContext;
using IHierarchicalSettingsResolver = Explore.Application.Contracts.Infrastructure.IHierarchicalSettingsResolver;
using ISettingGroup = Explore.Application.Contracts.Infrastructure.ISettingGroup;
using ResolvedSetting = Explore.Application.Contracts.Infrastructure.ResolvedSetting;
using SettingSource = Explore.Application.Contracts.Infrastructure.SettingSource;
using Explore.Application.DTOs.Ai;
using Explore.Application.Features.AiAssistant.Actors;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using Explore.Domain.Ai;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Explore.Infrastructure.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Features;

[ClassDataSource<AiAssistantDbBackedApiFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("RealRuntimeDb")]
public sealed class AiAssistantDbBackedApiFlowTests(AiAssistantDbBackedApiFixture fixture)
{
    private readonly AiAssistantDbBackedApiFixture _fixture = fixture;

    [Test]
    public async Task CreateSendAndDetail_WithPostgreSqlPersistence_PersistsConversationMessagesAndRun()
    {
        var seeded = await ResetAndSeedAsync();
        var conversationId = await CreateConversationAsync(seeded.UserId, "DB-backed planning");
        var runId = await SendMessageAsync(seeded.UserId, conversationId, "db-flow", "Plan the opening night.");

        using var detailRequest = _fixture.CreateAuthenticatedRequest(
            HttpMethod.Get,
            $"/api/ai/assistant/conversations/{conversationId}",
            seeded.UserId);
        var response = await _fixture.Client.SendAsync(detailRequest);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = json.RootElement;
        await Assert.That(root.GetProperty("id").GetGuid()).IsEqualTo(conversationId);
        await Assert.That(root.GetProperty("messages").GetArrayLength()).IsEqualTo(2);
        await Assert.That(root.GetProperty("runs").GetArrayLength()).IsEqualTo(1);
        await Assert.That(root.GetProperty("runs")[0].GetProperty("id").GetGuid()).IsEqualTo(runId);
        await Assert.That(root.GetProperty("_links").TryGetProperty("send-message", out _)).IsTrue();

        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var persistedConversation = await db.AiConversations.FindAsync(conversationId);
        await Assert.That(persistedConversation).IsNotNull();
        await Assert.That(persistedConversation!.TenantId).IsEqualTo(seeded.TenantId);
    }

    [Test]
    public async Task SendMessage_WithSameIdempotencyKeyAndPayload_ReplaysRunIdFromRepository()
    {
        var seeded = await ResetAndSeedAsync();
        var conversationId = await CreateConversationAsync(seeded.UserId, "Idempotent planning");
        var firstRunId = await SendMessageAsync(seeded.UserId, conversationId, "db-replay", "Plan the event.");

        var replayRunId = await SendMessageAsync(seeded.UserId, conversationId, "db-replay", "Plan the event.");

        await Assert.That(replayRunId).IsEqualTo(firstRunId);
    }

    [Test]
    public async Task SendMessage_WithSameIdempotencyKeyAndDifferentPayload_ReturnsApplicationConflict()
    {
        var seeded = await ResetAndSeedAsync();
        var conversationId = await CreateConversationAsync(seeded.UserId, "Conflict planning");
        await SendMessageAsync(seeded.UserId, conversationId, "db-conflict", "Plan the event.");
        using var request = _fixture.CreateAuthenticatedRequest(
            HttpMethod.Post,
            $"/api/ai/assistant/conversations/{conversationId}/messages",
            seeded.UserId);
        request.Headers.Add("Idempotency-Key", "db-conflict");
        request.Content = JsonContent.Create(new SendAiMessageRequestDto { Content = "Plan another event." });

        var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        await Assert.That(json.RootElement.GetProperty("code").GetString()).IsEqualTo("idempotency_key_conflict");
    }

    [Test]
    public async Task Detail_WhenRequestedByDifferentUser_ReturnsNotFoundWithPostgreSqlPersistence()
    {
        var seeded = await ResetAndSeedAsync();
        var conversationId = await CreateConversationAsync(seeded.UserId, "Private planning");
        using var request = _fixture.CreateAuthenticatedRequest(
            HttpMethod.Get,
            $"/api/ai/assistant/conversations/{conversationId}",
            Guid.NewGuid());

        var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task ConfirmProposedCreateEventDraft_CreatesExactlyOneDraftEventWithPostgreSqlPersistence()
    {
        var seeded = await ResetAndSeedAsync();
        var conversationId = await CreateConversationAsync(
            seeded.UserId,
            "Confirm draft planning",
            seeded.OrganizationActorId);
        var eventCountBeforeSend = await CountEventsAsync();

        await SendMessageAsync(seeded.UserId, conversationId, "db-confirm-proposal", "Draft an event.");

        await Assert.That(await CountEventsAsync()).IsEqualTo(eventCountBeforeSend);
        var proposedActionId = await GetOnlyProposedActionIdAsync(seeded.UserId, conversationId);

        var firstEventId = await ConfirmProposedActionAsync(seeded.UserId, conversationId, proposedActionId, "db-confirm-action");
        var duplicateEventId = await ConfirmProposedActionAsync(seeded.UserId, conversationId, proposedActionId, "db-confirm-action-duplicate");

        await Assert.That(duplicateEventId).IsEqualTo(firstEventId);
        await Assert.That(await CountEventsAsync()).IsEqualTo(eventCountBeforeSend + 1);
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var created = await db.Events.SingleAsync(e => e.Id == firstEventId);
        await Assert.That(created.TenantId).IsEqualTo(seeded.TenantId);
        await Assert.That(created.EventStatusId).IsEqualTo(1);
    }

    private async Task<TenantScenarioSeed.TenantOrganizationScenarioResult> ResetAndSeedAsync()
    {
        await _fixture.ResetDatabaseAsync();
        TenantScenarioSeed.TenantOrganizationScenarioResult seeded;
        await using (var scope = _fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            seeded = await TenantScenarioSeed.SeedActiveTenantWithOrganizationPublisherAsync(db);
        }

        await using (var scope = _fixture.Factory.Services.CreateAsyncScope())
        {
            var actorContexts = await scope.ServiceProvider
                .GetRequiredService<IAiAssistantActorContextService>()
                .ListAuthorizedActorContextsAsync(seeded.TenantId, seeded.UserId, CancellationToken.None);
            await Assert.That(actorContexts.Any(context => context.ActorId == seeded.OrganizationActorId))
                .IsTrue()
                .Because("the seeded organization publisher must be selectable as an explicit AI actor context");
        }

        _fixture.SetProposedOrganizationId(seeded.OrganizationId);
        return seeded;
    }

    private async Task<Guid> CreateConversationAsync(Guid userId, string title, Guid? actorId = null)
    {
        using var request = _fixture.CreateAuthenticatedRequest(HttpMethod.Post, "/api/ai/assistant/conversations", userId);
        request.Content = JsonContent.Create(new CreateAiConversationRequestDto { Title = title, ActorId = actorId });

        var response = await _fixture.Client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created).Because(content);
        var body = JsonSerializer.Deserialize<BaseCommandResponse<Guid>>(
            content,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.IsSuccess).IsTrue();
        return body.Id;
    }

    private async Task<Guid> SendMessageAsync(Guid userId, Guid conversationId, string idempotencyKey, string content)
    {
        using var request = _fixture.CreateAuthenticatedRequest(
            HttpMethod.Post,
            $"/api/ai/assistant/conversations/{conversationId}/messages",
            userId);
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        request.Content = JsonContent.Create(new SendAiMessageRequestDto { Content = content });

        var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>();
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.IsSuccess).IsTrue();
        await WaitForRunStatusAsync(userId, conversationId, body.Id, "Succeeded");
        return body.Id;
    }

    private async Task<JsonElement> WaitForRunStatusAsync(
        Guid userId,
        Guid conversationId,
        Guid runId,
        string expectedStatus)
    {
        string? lastResponse = null;
        for (var attempt = 0; attempt < 120; attempt++)
        {
            using var request = _fixture.CreateAuthenticatedRequest(
                HttpMethod.Get,
                $"/api/ai/assistant/conversations/{conversationId}/runs/{runId}",
                userId);
            var response = await _fixture.Client.SendAsync(request);
            lastResponse = await response.Content.ReadAsStringAsync();
            if (response.StatusCode == HttpStatusCode.OK)
            {
                using var json = JsonDocument.Parse(lastResponse);
                var root = json.RootElement.Clone();
                if (string.Equals(root.GetProperty("status").GetString(), expectedStatus, StringComparison.Ordinal))
                {
                    return root;
                }
            }

            await Task.Delay(100);
        }

        throw new TimeoutException(
            $"AI run {runId} did not reach status {expectedStatus}. Last response: {lastResponse}");
    }

    private async Task<Guid> ConfirmProposedActionAsync(
        Guid userId,
        Guid conversationId,
        Guid proposedActionId,
        string idempotencyKey)
    {
        using var request = _fixture.CreateAuthenticatedRequest(
            HttpMethod.Post,
            $"/api/ai/assistant/conversations/{conversationId}/proposed-actions/{proposedActionId}/confirm",
            userId);
        request.Headers.Add("Idempotency-Key", idempotencyKey);

        var response = await _fixture.Client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK).Because(content);
        var body = JsonSerializer.Deserialize<BaseCommandResponse<Guid>>(content, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.IsSuccess).IsTrue().Because(content);
        return body.Id;
    }

    private async Task<Guid> GetOnlyProposedActionIdAsync(Guid userId, Guid conversationId)
    {
        using var request = _fixture.CreateAuthenticatedRequest(
            HttpMethod.Get,
            $"/api/ai/assistant/conversations/{conversationId}",
            userId);

        var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var proposedActions = json.RootElement.GetProperty("proposedActions");
        await Assert.That(proposedActions.GetArrayLength()).IsEqualTo(1);
        var proposedAction = proposedActions[0];
        await Assert.That(proposedAction.GetProperty("status").GetString()).IsEqualTo("Proposed");
        await Assert.That(proposedAction.GetProperty("_links").TryGetProperty("confirm-action", out _)).IsTrue();
        await Assert.That(proposedAction.GetProperty("_links").TryGetProperty("reject-action", out _)).IsTrue();
        return proposedAction.GetProperty("id").GetGuid();
    }

    private async Task<int> CountEventsAsync()
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        return await db.Events.CountAsync();
    }
}

public sealed class AiAssistantDbBackedApiFixture : RealRuntimeApiFixture
{
    public void SetProposedOrganizationId(Guid organizationId)
    {
        Factory.Services.GetRequiredService<OrganizationScopedFakeAiChatProvider>().OrganizationId = organizationId;
    }

    protected override void ConfigureAdditionalTestServices(IServiceCollection services)
    {
        services.RemoveAll<IHierarchicalSettingsResolver>();
        services.RemoveAll<IAiChatProvider>();
        services.RemoveAll<IAiModelCatalog>();

        services.AddSingleton<IHierarchicalSettingsResolver>(new FixedAiSettingsResolver(CreateFakeSettings()));
        services.AddSingleton<OrganizationScopedFakeAiChatProvider>();
        services.AddSingleton<IAiChatProvider>(sp => sp.GetRequiredService<OrganizationScopedFakeAiChatProvider>());
        services.AddSingleton<IAiModelCatalog>(sp => sp.GetRequiredService<OrganizationScopedFakeAiChatProvider>());
    }

    private static AiAssistantSettingGroup CreateFakeSettings()
    {
        var values = new Dictionary<string, object?>
        {
            [GovernanceSettingKeys.AiAssistant.Enabled] = true,
            [GovernanceSettingKeys.AiAssistant.Provider] = AiProviderDefaults.ProviderFake,
            [GovernanceSettingKeys.AiAssistant.DailyMessageLimit] = 50,
            [GovernanceSettingKeys.AiAssistant.ToolProposalsEnabled] = true
        };

        var resolved = values.ToDictionary(
            pair => pair.Key,
            pair => new ResolvedSetting
            {
                Key = pair.Key,
                Value = JsonSerializer.Serialize(pair.Value),
                Source = SettingSource.SystemDefault,
                IsLocked = false
            });

        var group = new AiAssistantSettingGroup();
        group.Populate(resolved);
        return group;
    }

    private sealed class FixedAiSettingsResolver(AiAssistantSettingGroup settings) : IHierarchicalSettingsResolver
    {
        public Task<T?> ResolveAsync<T>(string key, SettingContext context, CancellationToken ct = default)
        {
            if (typeof(T) == typeof(bool)
                && key is GovernanceSettingKeys.Events.UserSubmissionEnabled
                    or GovernanceSettingKeys.Events.OrganizationSubmissionEnabled
                    or GovernanceSettingKeys.Events.GroupSubmissionEnabled)
            {
                return Task.FromResult((T?)(object)true);
            }

            return Task.FromResult(default(T));
        }

        public Task<ResolvedSetting?> ResolveWithMetadataAsync(string key, SettingContext context, CancellationToken ct = default)
            => Task.FromResult<ResolvedSetting?>(null);

        public Task<IReadOnlyList<ResolvedSetting>> ResolveBatchAsync(
            IEnumerable<string> keys,
            SettingContext context,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ResolvedSetting>>([]);

        public Task<TGroup> ResolveGroupAsync<TGroup>(SettingContext context, CancellationToken ct = default)
            where TGroup : ISettingGroup, new()
        {
            if (typeof(TGroup) == typeof(AiAssistantSettingGroup))
            {
                return Task.FromResult((TGroup)(object)settings);
            }

            return Task.FromResult(new TGroup());
        }

        public Task SetValueAsync(
            string key,
            string value,
            SettingScope scope,
            Guid scopeId,
            Guid actorId,
            CancellationToken ct = default)
            => Task.CompletedTask;

        public Task RemoveOverrideAsync(
            string key,
            SettingScope scope,
            Guid scopeId,
            Guid actorId,
            CancellationToken ct = default)
            => Task.CompletedTask;

        public Task LockAsync(
            string key,
            SettingScope scope,
            Guid scopeId,
            Guid actorId,
            CancellationToken ct = default)
            => Task.CompletedTask;

        public Task UnlockAsync(
            string key,
            SettingScope scope,
            Guid scopeId,
            Guid actorId,
            CancellationToken ct = default)
            => Task.CompletedTask;

        public void InvalidateCache(SettingScope? scope = null, Guid? scopeId = null)
        {
        }

        public void InvalidateOrganizationCache(Guid tenantId, Guid organizationId)
        {
        }

        public void InvalidateUserCache(Guid tenantId, Guid userId)
        {
        }
    }

    private sealed class OrganizationScopedFakeAiChatProvider : IAiChatProvider, IAiModelCatalog
    {
        private static readonly IReadOnlyList<AiModelDescriptor> Models =
        [
            new AiModelDescriptor(
                AiProviderDefaults.FakeModelId,
                AiProviderDefaults.FakeModelDisplayName,
                AiProviderDefaults.DefaultMaxInputTokens,
                AiProviderDefaults.DefaultMaxOutputTokens,
                SupportsToolProposals: true)
        ];

        public Guid? OrganizationId { get; set; }

        public Task<IReadOnlyList<AiModelDescriptor>> ListAvailableModelsAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Models);
        }

        public Task<AiChatProviderResult> SendAsync(AiChatPayload request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (request.Messages.Count == 0)
            {
                return Task.FromResult(AiChatProviderResult.Failure(
                    "empty_messages",
                    "At least one message is required."));
            }

            var lastUserMessage = request.Messages.LastOrDefault(message => message.Role == AiMessageRole.User)?.Content
                ?? request.Messages[^1].Content;
            var boundedMessage = lastUserMessage.Length <= 240 ? lastUserMessage : lastUserMessage[..240];
            var response = new AiChatResponse(
                $"Fake assistant response for: {boundedMessage}",
                BuildProposedActions(request),
                new AiTokenUsage(EstimateTokens(request.Messages), EstimateTokens(boundedMessage), null),
                ProviderRequestId: "fake-provider",
                FinishReason: "stop");

            return Task.FromResult(AiChatProviderResult.Success(response));
        }

        private IReadOnlyList<AiProposedActionCandidate> BuildProposedActions(AiChatPayload request)
        {
            if (!request.Options.ToolProposalsEnabled
                || request.ActionSchema is null
                || !request.ActionSchema.AllowedKinds.Contains(AiProposedActionKind.CreateEventDraft))
            {
                return [];
            }

            var payload = OrganizationId is { } organizationId
                ? JsonSerializer.Serialize(new
                {
                    title = "Fake AI event draft",
                    description = "Generated by the deterministic fake AI provider.",
                    organizationId,
                    participationConfiguration = new
                    {
                        participationHandlingModeId = 1,
                        advanceRegistrationObligationId = 1
                    }
                })
                : "{\"title\":\"Fake AI event draft\",\"description\":\"Generated by the deterministic fake AI provider.\",\"participationConfiguration\":{\"participationHandlingModeId\":1,\"advanceRegistrationObligationId\":1}}";

            return
            [
                new AiProposedActionCandidate(
                    AiProposedActionKind.CreateEventDraft,
                    payload,
                    "Create a deterministic fake event draft")
            ];
        }

        private static int EstimateTokens(IEnumerable<AiChatMessage> messages) =>
            Math.Max(1, messages.Sum(message => message.Content.Length) / 4);

        private static int EstimateTokens(string content) => Math.Max(1, content.Length / 4);
    }
}
