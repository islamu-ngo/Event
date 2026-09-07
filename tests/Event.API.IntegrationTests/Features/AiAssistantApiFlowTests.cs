using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API;
using Explore.Application.Contracts.Persistence;
using AiProviderDefaults = Explore.Application.Contracts.Infrastructure.Ai.AiProviderDefaults;
using AiChatPayload = Explore.Application.Contracts.Infrastructure.Ai.AiChatPayload;
using AiChatProviderResult = Explore.Application.Contracts.Infrastructure.Ai.AiChatProviderResult;
using AiModelDescriptor = Explore.Application.Contracts.Infrastructure.Ai.AiModelDescriptor;
using IAiChatProvider = Explore.Application.Contracts.Infrastructure.Ai.IAiChatProvider;
using IAiModelCatalog = Explore.Application.Contracts.Infrastructure.Ai.IAiModelCatalog;
using IHierarchicalSettingsResolver = Explore.Application.Contracts.Infrastructure.IHierarchicalSettingsResolver;
using ISettingGroup = Explore.Application.Contracts.Infrastructure.ISettingGroup;
using ITenantContext = Explore.Application.Contracts.Infrastructure.ITenantContext;
using ResolvedSetting = Explore.Application.Contracts.Infrastructure.ResolvedSetting;
using SettingSource = Explore.Application.Contracts.Infrastructure.SettingSource;
using Explore.Application.DTOs.Ai;
using Explore.Application.Models;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using Explore.Domain;
using Explore.Domain.Ai;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Explore.Infrastructure.Ai;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Features;

public sealed class AiAssistantApiFlowTests
{
    private static readonly Guid DefaultTenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000001");
    private static readonly Guid AlternateTenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000002");
    private const string TenantHeaderName = "X-Test-Tenant";

    [Test]
    public async Task Conversations_WithoutAuthentication_ReturnsUnauthorized()
    {
        await using var factory = new AiAssistantApiFlowFactory(CreateFakeSettings());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/ai/assistant/conversations");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task CreateConversation_WhenAssistantDisabled_ReturnsForbiddenProblemDetails()
    {
        await using var factory = new AiAssistantApiFlowFactory(CreateDisabledSettings());
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(HttpMethod.Post, "/api/ai/assistant/conversations");
        request.Content = JsonContent.Create(new CreateAiConversationRequestDto { Title = "Planning" });

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        await Assert.That(json.RootElement.GetProperty("code").GetString()).IsEqualTo("disabled");
    }

    [Test]
    public async Task FakeProviderFlow_CreatesConversationSendsMessageAndReturnsHalDetail()
    {
        await using var factory = new AiAssistantApiFlowFactory(CreateFakeSettings());
        using var client = factory.CreateClient();
        var userId = Guid.NewGuid();
        var conversationId = await CreateConversationAsync(client, userId, "Planning");
        var runId = await SendMessageAsync(client, userId, conversationId, "idem-flow", "Plan the opening night.");

        using var detailRequest = CreateAuthenticatedRequest(
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
        await using var factory = new AiAssistantApiFlowFactory(CreateFakeSettings());
        using var client = factory.CreateClient();
        var userId = Guid.NewGuid();
        var conversationId = await CreateConversationAsync(client, userId, "Planning");
        var firstRunId = await SendMessageAsync(client, userId, conversationId, "idem-replay", "Plan the event.");

        var replayRunId = await SendMessageAsync(client, userId, conversationId, "idem-replay", "Plan the event.");

        await Assert.That(replayRunId).IsEqualTo(firstRunId);
    }

    [Test]
    public async Task SendMessage_WithSameIdempotencyKeyAndDifferentPayload_ReturnsConflictProblemDetails()
    {
        await using var factory = new AiAssistantApiFlowFactory(CreateFakeSettings());
        using var client = factory.CreateClient();
        var userId = Guid.NewGuid();
        var conversationId = await CreateConversationAsync(client, userId, "Planning");
        await SendMessageAsync(client, userId, conversationId, "idem-conflict", "Plan the event.");

        using var request = CreateAuthenticatedRequest(
            HttpMethod.Post,
            $"/api/ai/assistant/conversations/{conversationId}/messages",
            userId);
        request.Headers.Add("Idempotency-Key", "idem-conflict");
        request.Content = JsonContent.Create(new SendAiMessageRequestDto { Content = "Plan a different event." });

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        await Assert.That(json.RootElement.GetProperty("code").GetString()).IsEqualTo("idempotency_key_conflict");
    }

    [Test]
    public async Task Detail_WhenRequestedByDifferentUser_ReturnsNotFound()
    {
        await using var factory = new AiAssistantApiFlowFactory(CreateFakeSettings());
        using var client = factory.CreateClient();
        var ownerId = Guid.NewGuid();
        var conversationId = await CreateConversationAsync(client, ownerId, "Planning");

        using var request = CreateAuthenticatedRequest(
            HttpMethod.Get,
            $"/api/ai/assistant/conversations/{conversationId}",
            Guid.NewGuid());

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Detail_WhenRequestedFromDifferentTenant_ReturnsNotFound()
    {
        await using var factory = new AiAssistantApiFlowFactory(CreateFakeSettings());
        using var client = factory.CreateClient();
        var userId = Guid.NewGuid();
        var conversationId = await CreateConversationAsync(client, userId, "Planning", DefaultTenantId);

        using var request = CreateAuthenticatedRequest(
            HttpMethod.Get,
            $"/api/ai/assistant/conversations/{conversationId}",
            userId,
            AlternateTenantId);

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task SendMessage_WhenProviderFails_ReturnsSafeServiceUnavailableProblemDetails()
    {
        await using var factory = new AiAssistantApiFlowFactory(CreateFakeSettings(), providerFails: true);
        using var client = factory.CreateClient();
        var userId = Guid.NewGuid();
        var conversationId = await CreateConversationAsync(client, userId, "Planning");
        using var request = CreateAuthenticatedRequest(
            HttpMethod.Post,
            $"/api/ai/assistant/conversations/{conversationId}/messages",
            userId);
        request.Headers.Add("Idempotency-Key", "provider-failure");
        request.Content = JsonContent.Create(new SendAiMessageRequestDto { Content = "Plan the event." });

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>();
        await Assert.That(body).IsNotNull();

        var run = await WaitForRunStatusAsync(client, userId, conversationId, body!.Id, "Failed");
        await Assert.That(run.GetProperty("failureCode").GetString()).IsEqualTo("provider_unreachable");
        await Assert.That(run.GetProperty("failureMessage").GetString()).DoesNotContain("Plan the event");
        await Assert.That(run.GetProperty("failureMessage").GetString()).DoesNotContain("api_key");
    }

    private static async Task<Guid> CreateConversationAsync(
        HttpClient client,
        Guid userId,
        string title,
        Guid? tenantId = null)
    {
        using var request = CreateAuthenticatedRequest(
            HttpMethod.Post,
            "/api/ai/assistant/conversations",
            userId,
            tenantId);
        request.Content = JsonContent.Create(new CreateAiConversationRequestDto { Title = title });

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>();
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.IsSuccess).IsTrue();
        return body.Id;
    }

    private static async Task<Guid> SendMessageAsync(
        HttpClient client,
        Guid userId,
        Guid conversationId,
        string idempotencyKey,
        string content,
        Guid? tenantId = null,
        string? expectedRunStatus = "Succeeded")
    {
        using var request = CreateAuthenticatedRequest(
            HttpMethod.Post,
            $"/api/ai/assistant/conversations/{conversationId}/messages",
            userId,
            tenantId);
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        request.Content = JsonContent.Create(new SendAiMessageRequestDto { Content = content });

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>();
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.IsSuccess).IsTrue();
        if (!string.IsNullOrWhiteSpace(expectedRunStatus))
        {
            await WaitForRunStatusAsync(client, userId, conversationId, body.Id, expectedRunStatus, tenantId);
        }

        return body.Id;
    }

    private static async Task<JsonElement> WaitForRunStatusAsync(
        HttpClient client,
        Guid userId,
        Guid conversationId,
        Guid runId,
        string expectedStatus,
        Guid? tenantId = null)
    {
        string? lastStatus = null;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            using var request = CreateAuthenticatedRequest(
                HttpMethod.Get,
                $"/api/ai/assistant/conversations/{conversationId}/runs/{runId}",
                userId,
                tenantId);
            var response = await client.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
                var root = json.RootElement.Clone();
                lastStatus = root.GetProperty("status").GetString();
                if (string.Equals(lastStatus, expectedStatus, StringComparison.Ordinal))
                {
                    return root;
                }
                if (string.Equals(lastStatus, "Failed", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"AI run {runId} failed with code {root.GetProperty("failureCode").GetString()} while waiting for {expectedStatus}.");
                }
            }

            await Task.Delay(50);
        }

        throw new TimeoutException(
            $"AI run {runId} did not reach status {expectedStatus}; last observed status was {lastStatus ?? "unavailable"}.");
    }

    private static HttpRequestMessage CreateAuthenticatedRequest(
        HttpMethod method,
        string url,
        Guid? userId = null,
        Guid? tenantId = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(
            TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(userId ?? Guid.NewGuid()));
        request.Headers.Add(TenantHeaderName, (tenantId ?? DefaultTenantId).ToString());
        return request;
    }

    private static AiAssistantSettingGroup CreateFakeSettings()
        => CreateSettings(enabled: true, provider: AiProviderDefaults.ProviderFake, toolProposalsEnabled: true);

    private static AiAssistantSettingGroup CreateDisabledSettings()
        => CreateSettings(enabled: false, provider: AiProviderDefaults.ProviderNone);

    private static AiAssistantSettingGroup CreateSettings(
        bool enabled,
        string provider,
        bool toolProposalsEnabled = false)
    {
        var values = new Dictionary<string, object?>
        {
            [GovernanceSettingKeys.AiAssistant.Enabled] = enabled,
            [GovernanceSettingKeys.AiAssistant.Provider] = provider,
            [GovernanceSettingKeys.AiAssistant.DailyMessageLimit] = 50,
            [GovernanceSettingKeys.AiAssistant.ToolProposalsEnabled] = toolProposalsEnabled
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

    private sealed class AiAssistantApiFlowFactory(AiAssistantSettingGroup settings, bool providerFails = false)
        : AuthenticatedWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider();
            base.ConfigureWebHost(builder);

            Microsoft.AspNetCore.TestHost.WebHostBuilderExtensions.ConfigureTestServices(builder, services =>
            {
                services.RemoveAll<IHierarchicalSettingsResolver>();
                services.RemoveAll<ITenantContext>();
                services.AddSingleton<IHierarchicalSettingsResolver>(new FixedAiSettingsResolver(settings));
                services.AddScoped<ITenantContext, HeaderTenantContext>();

                services.RemoveAll<IAiChatProvider>();
                services.RemoveAll<IAiModelCatalog>();
                services.RemoveAll<IAiConversationRepository>();
                services.RemoveAll<IIdempotencyRepository>();

                services.AddSingleton<InMemoryAiConversationStore>();
                services.AddScoped<IAiConversationRepository, InMemoryAiConversationRepository>();
                services.AddSingleton<IIdempotencyRepository, InMemoryIdempotencyRepository>();

                if (providerFails)
                {
                    services.AddSingleton<FailingAiChatProvider>();
                    services.AddSingleton<IAiChatProvider>(sp => sp.GetRequiredService<FailingAiChatProvider>());
                    services.AddSingleton<IAiModelCatalog>(sp => sp.GetRequiredService<FailingAiChatProvider>());
                }
                else
                {
                    services.AddSingleton<FakeAiChatProvider>();
                    services.AddSingleton<IAiChatProvider>(sp => sp.GetRequiredService<FakeAiChatProvider>());
                    services.AddSingleton<IAiModelCatalog>(sp => sp.GetRequiredService<FakeAiChatProvider>());
                }
            });
        }
    }

    private sealed class HeaderTenantContext(IHttpContextAccessor httpContextAccessor) : ITenantContext
    {
        public Guid TenantId
        {
            get
            {
                var header = httpContextAccessor.HttpContext?.Request.Headers[TenantHeaderName].FirstOrDefault();
                return Guid.TryParse(header, out var tenantId) ? tenantId : DefaultTenantId;
            }
        }
    }

    private sealed class InMemoryAiConversationStore
    {
        public Dictionary<Guid, AiConversation> Conversations { get; } = [];

        public List<AiToolExecution> ToolExecutions { get; } = [];
    }

    private sealed class InMemoryAiConversationRepository(
        InMemoryAiConversationStore store,
        ITenantContext tenantContext) : IAiConversationRepository
    {
        private IEnumerable<AiConversation> TenantConversations
            => store.Conversations.Values.Where(conversation => conversation.TenantId == tenantContext.TenantId);

        public Task<AiConversation?> GetById(Guid id)
            => Task.FromResult(TenantConversations.FirstOrDefault(conversation => conversation.Id == id));

        public Task<IReadOnlyList<AiConversation>> GetAll()
            => Task.FromResult<IReadOnlyList<AiConversation>>(TenantConversations.ToList());

        public Task<(IReadOnlyList<AiConversation> Items, int TotalCount)> GetAllPaged(int pageNumber, int pageSize)
        {
            var conversations = TenantConversations.ToList();
            var items = conversations
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            return Task.FromResult<(
                IReadOnlyList<AiConversation> Items,
                int TotalCount)>((items, conversations.Count));
        }

        public Task<bool> Exists(Guid id)
            => Task.FromResult(TenantConversations.Any(conversation => conversation.Id == id));

        public Task<AiConversation> Create(AiConversation entity)
        {
            store.Conversations[entity.Id] = entity;
            return Task.FromResult(entity);
        }

        public Task Update(AiConversation entity)
        {
            store.Conversations[entity.Id] = entity;
            return Task.CompletedTask;
        }

        public Task Delete(AiConversation entity)
        {
            store.Conversations.Remove(entity.Id);
            return Task.CompletedTask;
        }

        public Task<int> HardDeleteUserConversationGraphAsync(Guid subjectId, CancellationToken cancellationToken)
        {
            var deletedConversations = TenantConversations
                .Where(conversation => conversation.UserId == subjectId)
                .ToArray();
            var deletedActionIds = deletedConversations
                .SelectMany(conversation => conversation.ProposedActions)
                .Select(action => action.Id)
                .ToHashSet();

            foreach (AiConversation conversation in deletedConversations)
            {
                store.Conversations.Remove(conversation.Id);
            }

            if (deletedActionIds.Count > 0)
            {
                store.ToolExecutions.RemoveAll(execution => deletedActionIds.Contains(execution.ProposedActionId));
            }

            return Task.FromResult(deletedConversations.Length);
        }

        public Task<AiConversation?> GetByIdWithDetailsAsync(Guid conversationId, CancellationToken cancellationToken)
            => GetById(conversationId);

        public Task<AiConversation?> GetByIdForUpdateAsync(Guid conversationId, CancellationToken cancellationToken)
            => GetById(conversationId);

        public Task<IReadOnlyList<AiConversation>> ListRecentForUserAsync(
            Guid userId,
            int limit,
            CancellationToken cancellationToken)
        {
            var conversations = TenantConversations
                .Where(conversation => conversation.UserId == userId)
                .OrderByDescending(conversation => conversation.UpdatedAt ?? conversation.CreatedAt)
                .ThenByDescending(conversation => conversation.Id)
                .Take(Math.Max(0, limit))
                .ToList();

            return Task.FromResult<IReadOnlyList<AiConversation>>(conversations);
        }

        public Task<int> CountUserMessagesSinceAsync(Guid userId, DateTime sinceUtc, CancellationToken cancellationToken)
        {
            var count = TenantConversations
                .SelectMany(conversation => conversation.Messages)
                .Count(message => message.Role == AiMessageRole.User
                    && message.CreatedBy == userId
                    && message.CreatedAt >= sinceUtc);

            return Task.FromResult(count);
        }

        public Task<int> CountTenantMessagesSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken)
        {
            var count = TenantConversations
                .SelectMany(conversation => conversation.Messages)
                .Count(message => message.Role == AiMessageRole.User && message.CreatedAt >= sinceUtc);

            return Task.FromResult(count);
        }

        public Task<int> ReleaseStaleRunningConversationsForUserAsync(
            Guid userId,
            DateTime staleBeforeUtc,
            string failureCode,
            string failureMessage,
            DateTime utcNow,
            CancellationToken cancellationToken)
        {
            var staleConversations = TenantConversations
                .Where(conversation => conversation.UserId == userId)
                .Where(conversation => conversation.Status == AiConversationStatus.Running)
                .Where(conversation => conversation.Runs.Any(run =>
                    run.Status is AiRunStatus.Queued or AiRunStatus.InProgress
                    && (run.StartedAt ?? run.QueuedAt) <= staleBeforeUtc))
                .Where(conversation => !conversation.Runs.Any(run =>
                    run.Status is AiRunStatus.Queued or AiRunStatus.InProgress
                    && (run.StartedAt ?? run.QueuedAt) > staleBeforeUtc))
                .ToList();

            foreach (var conversation in staleConversations)
            {
                foreach (var run in conversation.Runs.Where(run =>
                    run.Status is AiRunStatus.Queued or AiRunStatus.InProgress
                    && (run.StartedAt ?? run.QueuedAt) <= staleBeforeUtc))
                {
                    run.Fail(failureCode, failureMessage, utcNow);
                }

                conversation.Activate(utcNow);
            }

            return Task.FromResult(staleConversations.Count);
        }

        public Task<int> CountRunningConversationsForUserAsync(Guid userId, CancellationToken cancellationToken)
        {
            var count = TenantConversations
                .Count(conversation => conversation.UserId == userId && conversation.Status == AiConversationStatus.Running);

            return Task.FromResult(count);
        }


        public Task<AiProposedAction?> GetProposedActionForUpdateAsync(Guid proposedActionId, CancellationToken cancellationToken)
        {
            var action = TenantConversations
                .SelectMany(conversation => conversation.ProposedActions)
                .FirstOrDefault(candidate => candidate.Id == proposedActionId);

            return Task.FromResult(action);
        }

        public Task UpdateProposedActionAsync(AiProposedAction proposedAction, CancellationToken cancellationToken)
        {
            var existingAction = TenantConversations
                .SelectMany(conversation => conversation.ProposedActions)
                .FirstOrDefault(candidate => candidate.Id == proposedAction.Id);

            if (existingAction is null)
            {
                return Task.CompletedTask;
            }

            existingAction.Status = proposedAction.Status;
            existingAction.ConfirmedBy = proposedAction.ConfirmedBy;
            existingAction.ConfirmedAt = proposedAction.ConfirmedAt;
            existingAction.RejectedBy = proposedAction.RejectedBy;
            existingAction.RejectedAt = proposedAction.RejectedAt;
            existingAction.ResultResourceId = proposedAction.ResultResourceId;
            existingAction.FailureCode = proposedAction.FailureCode;
            existingAction.FailureMessage = proposedAction.FailureMessage;

            return Task.CompletedTask;
        }

        public Task CreateToolExecutionAsync(AiToolExecution toolExecution, CancellationToken cancellationToken)
        {
            store.ToolExecutions.Add(toolExecution);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AiToolExecution>> ListToolExecutionsForProposedActionAsync(Guid proposedActionId, CancellationToken cancellationToken)
        {
            var executions = store.ToolExecutions
                .Where(execution => execution.TenantId == tenantContext.TenantId && execution.ProposedActionId == proposedActionId)
                .OrderByDescending(execution => execution.StartedAt)
                .ThenByDescending(execution => execution.Id)
                .ToList();

            return Task.FromResult<IReadOnlyList<AiToolExecution>>(executions);
        }

        public Task<AiRetentionCleanupResult> RedactExpiredConversationsAsync(
            DateTime cutoffUtc,
            int retentionDays,
            DateTime utcNow,
            bool dryRun,
            CancellationToken cancellationToken)
        {
            int eligibleConversations = TenantConversations
                .Count(conversation => (conversation.UpdatedAt ?? conversation.CreatedAt) <= cutoffUtc);

            return Task.FromResult(new AiRetentionCleanupResult(
                cutoffUtc,
                retentionDays,
                eligibleConversations,
                RedactedConversations: 0,
                RedactedMessages: 0,
                RedactedRuns: 0,
                RedactedReferences: 0,
                RedactedProposedActions: 0,
                RedactedToolExecutions: 0,
                DryRun: dryRun));
        }
    }

    private sealed class FailingAiChatProvider : IAiChatProvider, IAiModelCatalog
    {
        public Task<AiChatProviderResult> SendAsync(AiChatPayload request, CancellationToken cancellationToken = default)
            => Task.FromResult(AiChatProviderResult.Failure(
                "provider_unreachable",
                "AI provider is unavailable.",
                isTransient: true));

        public Task<IReadOnlyList<AiModelDescriptor>> ListAvailableModelsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AiModelDescriptor>>(
            [
                new AiModelDescriptor(
                    AiProviderDefaults.FakeModelId,
                    AiProviderDefaults.FakeModelDisplayName,
                    AiProviderDefaults.DefaultMaxInputTokens,
                    AiProviderDefaults.DefaultMaxOutputTokens,
                    SupportsToolProposals: true)
            ]);
    }

    private sealed class InMemoryIdempotencyRepository : IIdempotencyRepository
    {
        private readonly Dictionary<(Guid TenantId, string Key), IdempotencyRecord> _records = [];

        public Task<IdempotencyRecord?> FindAsync(string key, Guid tenantId, CancellationToken cancellationToken = default)
            => Task.FromResult(_records.GetValueOrDefault((tenantId, key)));

        public Task SaveAsync(IdempotencyRecord record, CancellationToken cancellationToken = default)
        {
            _records[(record.TenantId, record.Key)] = record;
            return Task.CompletedTask;
        }

        public Task<IdempotencyClaim> TryClaimAsync(
            IdempotencyRecord record,
            CancellationToken cancellationToken = default)
        {
            if (_records.TryGetValue((record.TenantId, record.Key), out var existing))
            {
                return Task.FromResult(new IdempotencyClaim(existing, IsOwner: false));
            }

            _records[(record.TenantId, record.Key)] = record;
            return Task.FromResult(new IdempotencyClaim(record, IsOwner: true));
        }

        public Task<bool> CompleteAsync(
            Guid recordId,
            int statusCode,
            string? responseBody,
            string? contentType,
            CancellationToken cancellationToken = default)
        {
            var record = _records.Values.FirstOrDefault(candidate => candidate.Id == recordId);
            if (record is null || record.StatusCode != IdempotencyRecord.InProgressStatusCode)
            {
                return Task.FromResult(false);
            }

            record.StatusCode = statusCode;
            record.ResponseBody = responseBody;
            record.ContentType = contentType;
            return Task.FromResult(true);
        }

        public Task<bool> ReleaseAsync(Guid recordId, CancellationToken cancellationToken = default)
        {
            var pair = _records.FirstOrDefault(candidate => candidate.Value.Id == recordId);
            if (pair.Value is null || pair.Value.StatusCode != IdempotencyRecord.InProgressStatusCode)
            {
                return Task.FromResult(false);
            }

            _records.Remove(pair.Key);
            return Task.FromResult(true);
        }

        public Task<int> CountExpiredAsync(DateTime expiresBeforeUtc, int batchSize, CancellationToken cancellationToken = default)
        {
            var count = _records.Values.Count(record => record.ExpiresAt < expiresBeforeUtc);
            return Task.FromResult(Math.Min(count, batchSize));
        }

        public Task<int> DeleteExpiredAsync(DateTime expiresBeforeUtc, int batchSize, CancellationToken cancellationToken = default)
        {
            var expired = _records
                .Where(pair => pair.Value.ExpiresAt < expiresBeforeUtc)
                .Take(batchSize)
                .Select(pair => pair.Key)
                .ToList();

            foreach (var key in expired)
            {
                _records.Remove(key);
            }

            return Task.FromResult(expired.Count);
        }
    }

    private sealed class FixedAiSettingsResolver(AiAssistantSettingGroup settings) : IHierarchicalSettingsResolver
    {
        public Task<T?> ResolveAsync<T>(string key, SettingContext context, CancellationToken ct = default)
            => Task.FromResult(default(T));

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
}
