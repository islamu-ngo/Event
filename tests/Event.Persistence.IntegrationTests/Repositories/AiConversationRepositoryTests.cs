using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Ai;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class AiConversationRepositoryTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task CreateAndUpdate_WithTrackedAggregate_PersistsMessagesRunsAndActions()
    {
        await fixture.ResetAsync();
        var scope = await SeedTenantAndUserAsync("ai-repository-update");

        await using (var createContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(scope.TenantId)))
        {
            var repository = new AiConversationRepository(createContext);
            await repository.Create(NewConversation(scope, "Planning"));
        }

        await using (var updateContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(scope.TenantId)))
        {
            var repository = new AiConversationRepository(updateContext);
            var conversation = await repository.GetByIdForUpdateAsync(scope.ConversationId, CancellationToken.None);
            await Assert.That(conversation).IsNotNull();

            var userMessage = conversation!.AddMessage(AiMessageRole.User, "Plan the event", scope.UserId, DateTime.UtcNow);
            var assistantMessage = conversation.AddMessage(AiMessageRole.Assistant, "Here is a plan", null, DateTime.UtcNow.AddSeconds(1));
            var run = conversation.QueueRun("fake", "fake-ai-assistant-v1", DateTime.UtcNow.AddSeconds(2));
            run.Start(DateTime.UtcNow.AddSeconds(3));
            conversation.ProposeAction(
                AiProposedActionKind.CreateEventDraft,
                "{\"title\":\"AI event\"}",
                assistantMessage.Id,
                scope.UserId,
                DateTime.UtcNow.AddSeconds(4));
            conversation.CompleteRun(run, DateTime.UtcNow.AddSeconds(5));

            await repository.Update(conversation);
        }

        await using var verifyContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(scope.TenantId));
        var verifyRepository = new AiConversationRepository(verifyContext);
        var saved = await verifyRepository.GetByIdWithDetailsAsync(scope.ConversationId, CancellationToken.None);

        await Assert.That(saved).IsNotNull();
        await Assert.That(saved!.Status).IsEqualTo(AiConversationStatus.Active);
        await Assert.That(saved.Messages.Select(message => message.Sequence)).IsEquivalentTo([1L, 2L]);
        await Assert.That(saved.Messages.Select(message => message.Role)).IsEquivalentTo([AiMessageRole.User, AiMessageRole.Assistant]);
        await Assert.That(saved.Runs.Single().Status).IsEqualTo(AiRunStatus.Succeeded);
        await Assert.That(saved.ProposedActions.Single().Status).IsEqualTo(AiProposedActionStatus.Proposed);
        await Assert.That(saved.ProposedActions.Single().PayloadJson).Contains("AI event");
    }

    [Test]
    public async Task ListRecentForUserAsync_ReturnsOnlyCurrentTenantRows()
    {
        await fixture.ResetAsync();
        var tenantA = await SeedTenantAndUserAsync("ai-list-a");
        var tenantB = await SeedTenantAndUserAsync("ai-list-b", tenantA.UserId);

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.AiConversations.Add(NewConversation(tenantA, "Tenant A"));
            seedContext.AiConversations.Add(NewConversation(tenantB, "Tenant B"));
            await seedContext.SaveChangesAsync();
        }

        await using var context = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantA.TenantId));
        var repository = new AiConversationRepository(context);

        var conversations = await repository.ListRecentForUserAsync(tenantA.UserId, limit: 10, CancellationToken.None);

        await Assert.That(conversations.Select(conversation => conversation.Id)).IsEquivalentTo([tenantA.ConversationId]);
    }

    [Test]
    public async Task GetByIdWithDetailsAsync_WhenTenantDoesNotMatch_ReturnsNull()
    {
        await fixture.ResetAsync();
        var tenantA = await SeedTenantAndUserAsync("ai-detail-a");
        var tenantB = await SeedTenantAndUserAsync("ai-detail-b", tenantA.UserId);

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.AiConversations.Add(NewConversation(tenantA, "Tenant A"));
            await seedContext.SaveChangesAsync();
        }

        await using var wrongTenantContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantB.TenantId));
        var repository = new AiConversationRepository(wrongTenantContext);

        var conversation = await repository.GetByIdWithDetailsAsync(tenantA.ConversationId, CancellationToken.None);

        await Assert.That(conversation).IsNull();
    }

    [Test]
    public async Task CountUserMessagesSinceAsync_CountsOnlyCurrentTenantUserMessages()
    {
        await fixture.ResetAsync();
        var tenantA = await SeedTenantAndUserAsync("ai-quota-a");
        var tenantB = await SeedTenantAndUserAsync("ai-quota-b", tenantA.UserId);
        var since = DateTime.UtcNow.AddHours(-1);

        await using (var seedContext = fixture.CreateDbContext())
        {
            var conversationA = NewConversation(tenantA, "Tenant A");
            conversationA.AddMessage(AiMessageRole.User, "Count me", tenantA.UserId, DateTime.UtcNow);
            conversationA.AddMessage(AiMessageRole.Assistant, "Do not count assistant", null, DateTime.UtcNow.AddSeconds(1));

            var conversationB = NewConversation(tenantB, "Tenant B");
            conversationB.AddMessage(AiMessageRole.User, "Wrong tenant", tenantB.UserId, DateTime.UtcNow);

            seedContext.AiConversations.AddRange(conversationA, conversationB);
            await seedContext.SaveChangesAsync();
        }

        await using var context = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantA.TenantId));
        var repository = new AiConversationRepository(context);

        var count = await repository.CountUserMessagesSinceAsync(tenantA.UserId, since, CancellationToken.None);

        await Assert.That(count).IsEqualTo(1);
    }

    [Test]
    public async Task ReleaseStaleRunningConversationsForUserAsync_ReleasesOnlyStaleCurrentTenantRuns()
    {
        await fixture.ResetAsync();
        var tenantA = await SeedTenantAndUserAsync("ai-stale-run-a");
        var tenantB = await SeedTenantAndUserAsync("ai-stale-run-b", tenantA.UserId);
        var now = new DateTime(2026, 06, 08, 14, 0, 0, DateTimeKind.Utc);
        var staleBefore = now.AddSeconds(-35);

        var staleTenantAConversation = NewRunningConversation(
            tenantA,
            "Stale Tenant A",
            queuedAt: now.AddMinutes(-2),
            startedAt: now.AddMinutes(-2).AddSeconds(1));
        var freshTenantAConversation = NewRunningConversation(
            tenantA with { ConversationId = Guid.CreateVersion7() },
            "Fresh Tenant A",
            queuedAt: now.AddSeconds(-10),
            startedAt: now.AddSeconds(-9));
        var staleTenantBConversation = NewRunningConversation(
            tenantB,
            "Stale Tenant B",
            queuedAt: now.AddMinutes(-2),
            startedAt: now.AddMinutes(-2).AddSeconds(1));

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.AiConversations.AddRange(staleTenantAConversation, freshTenantAConversation, staleTenantBConversation);
            await seedContext.SaveChangesAsync();
        }

        await using (var releaseContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantA.TenantId)))
        {
            var repository = new AiConversationRepository(releaseContext);

            var released = await repository.ReleaseStaleRunningConversationsForUserAsync(
                tenantA.UserId,
                staleBefore,
                "stale_ai_run_released",
                "Stale run released.",
                now,
                CancellationToken.None);

            await Assert.That(released).IsEqualTo(1);
        }

        await using var verifyContext = fixture.CreateDbContext();
        var staleTenantA = await verifyContext.AiConversations
            .IgnoreQueryFilters()
            .Include(conversation => conversation.Runs)
            .SingleAsync(conversation => conversation.Id == staleTenantAConversation.Id);
        var freshTenantA = await verifyContext.AiConversations
            .IgnoreQueryFilters()
            .Include(conversation => conversation.Runs)
            .SingleAsync(conversation => conversation.Id == freshTenantAConversation.Id);
        var staleTenantB = await verifyContext.AiConversations
            .IgnoreQueryFilters()
            .Include(conversation => conversation.Runs)
            .SingleAsync(conversation => conversation.Id == staleTenantBConversation.Id);

        await Assert.That(staleTenantA.Status).IsEqualTo(AiConversationStatus.Active);
        await Assert.That(staleTenantA.Runs.Single().Status).IsEqualTo(AiRunStatus.Failed);
        await Assert.That(staleTenantA.Runs.Single().FailureCode).IsEqualTo("stale_ai_run_released");
        await Assert.That(staleTenantA.Runs.Single().FailureMessage).IsEqualTo("Stale run released.");
        await Assert.That(freshTenantA.Status).IsEqualTo(AiConversationStatus.Running);
        await Assert.That(freshTenantA.Runs.Single().Status).IsEqualTo(AiRunStatus.InProgress);
        await Assert.That(staleTenantB.Status).IsEqualTo(AiConversationStatus.Running);
        await Assert.That(staleTenantB.Runs.Single().Status).IsEqualTo(AiRunStatus.InProgress);
    }

    [Test]
    public async Task GetProposedActionForUpdateAsync_RespectsTenantFilter()
    {
        await fixture.ResetAsync();
        var tenantA = await SeedTenantAndUserAsync("ai-action-a");
        var tenantB = await SeedTenantAndUserAsync("ai-action-b", tenantA.UserId);
        Guid actionId;

        await using (var seedContext = fixture.CreateDbContext())
        {
            var conversation = NewConversation(tenantA, "Tenant A");
            actionId = conversation.ProposeAction(
                AiProposedActionKind.CreateEventDraft,
                "{\"title\":\"Tenant A event\"}",
                messageId: null,
                tenantA.UserId,
                DateTime.UtcNow).Id;
            seedContext.AiConversations.Add(conversation);
            await seedContext.SaveChangesAsync();
        }

        await using var wrongTenantContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantB.TenantId));
        var wrongTenantRepository = new AiConversationRepository(wrongTenantContext);
        var hiddenAction = await wrongTenantRepository.GetProposedActionForUpdateAsync(actionId, CancellationToken.None);

        await using var tenantContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantA.TenantId));
        var repository = new AiConversationRepository(tenantContext);
        var action = await repository.GetProposedActionForUpdateAsync(actionId, CancellationToken.None);

        await Assert.That(hiddenAction).IsNull();
        await Assert.That(action).IsNotNull();
        await Assert.That(action!.Conversation).IsNotNull();
        await Assert.That(action.PayloadJson).Contains("Tenant A event");
    }

    [Test]
    public async Task ToolExecutionMetadata_CanBePersistedAndQueriedWithoutPayloadLeakage()
    {
        await fixture.ResetAsync();
        var tenantA = await SeedTenantAndUserAsync("ai-tool-execution-a");
        var tenantB = await SeedTenantAndUserAsync("ai-tool-execution-b", tenantA.UserId);
        Guid actionId;

        await using (var seedContext = fixture.CreateDbContext())
        {
            var conversation = NewConversation(tenantA, "Tenant A");
            actionId = conversation.ProposeAction(
                AiProposedActionKind.CreateEventDraft,
                "{\"title\":\"Tenant A event\",\"secret\":\"do not store\"}",
                messageId: null,
                tenantA.UserId,
                DateTime.UtcNow).Id;
            seedContext.AiConversations.Add(conversation);
            await seedContext.SaveChangesAsync();
        }

        await using (var executionContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantA.TenantId)))
        {
            var repository = new AiConversationRepository(executionContext);
            var execution = new AiToolExecution
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantA.TenantId,
                ProposedActionId = actionId,
                ToolName = "CreateEventDraft",
                StartedAt = DateTime.UtcNow
            };
            execution.MarkSucceeded(DateTime.UtcNow.AddMilliseconds(10));

            await repository.CreateToolExecutionAsync(execution, CancellationToken.None);

            var failedExecution = new AiToolExecution
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantA.TenantId,
                ProposedActionId = actionId,
                ToolName = "CreateEventDraft",
                StartedAt = DateTime.UtcNow.AddMilliseconds(20)
            };
            failedExecution.MarkFailed("invalid_tool_arguments", "Title is required.", DateTime.UtcNow.AddMilliseconds(30));

            await repository.CreateToolExecutionAsync(failedExecution, CancellationToken.None);
        }

        await using var wrongTenantContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantB.TenantId));
        var wrongTenantRepository = new AiConversationRepository(wrongTenantContext);
        IReadOnlyList<AiToolExecution> hiddenExecutions = await wrongTenantRepository.ListToolExecutionsForProposedActionAsync(actionId, CancellationToken.None);

        await using var tenantContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantA.TenantId));
        var tenantRepository = new AiConversationRepository(tenantContext);
        IReadOnlyList<AiToolExecution> executions = await tenantRepository.ListToolExecutionsForProposedActionAsync(actionId, CancellationToken.None);

        await Assert.That(hiddenExecutions).IsEmpty();
        await Assert.That(executions.Count).IsEqualTo(2);
        AiToolExecution failed = executions[0];
        AiToolExecution succeeded = executions[1];
        await Assert.That(failed.ToolName).IsEqualTo("CreateEventDraft");
        await Assert.That(failed.Succeeded).IsFalse();
        await Assert.That(failed.CompletedAt).IsNotNull();
        await Assert.That(failed.FailureCode).IsEqualTo("invalid_tool_arguments");
        await Assert.That(failed.FailureMessage).IsEqualTo("Title is required.");
        await Assert.That(succeeded.ToolName).IsEqualTo("CreateEventDraft");
        await Assert.That(succeeded.Succeeded).IsTrue();
        await Assert.That(succeeded.CompletedAt).IsNotNull();
        await Assert.That(succeeded.FailureCode).IsNull();
        await Assert.That(succeeded.FailureMessage).IsNull();
        foreach (AiToolExecution execution in executions)
        {
            await Assert.That(execution.ToolName).DoesNotContain("Tenant A event");
            await Assert.That(execution.ToolName).DoesNotContain("secret");
            await Assert.That(execution.FailureMessage ?? string.Empty).DoesNotContain("Tenant A event");
            await Assert.That(execution.FailureMessage ?? string.Empty).DoesNotContain("secret");
        }
    }

    [Test]
    public async Task RedactExpiredConversationsAsync_RedactsCurrentTenantOnlyAndSoftDeletesConversation()
    {
        await fixture.ResetAsync();
        var now = new DateTime(2026, 06, 03, 12, 0, 0, DateTimeKind.Utc);
        var expiredAt = now.AddDays(-45);
        var tenantA = await SeedTenantAndUserAsync("ai-retention-a");
        var tenantB = await SeedTenantAndUserAsync("ai-retention-b", tenantA.UserId);
        Guid expiredConversationId;
        Guid otherTenantConversationId;

        await using (var seedContext = fixture.CreateDbContext())
        {
            var expiredConversation = NewConversation(tenantA, "Secret AI plan");
            expiredConversation.CreatedAt = expiredAt;
            expiredConversation.AddMessage(AiMessageRole.User, "Secret prompt content", tenantA.UserId, expiredAt.AddMinutes(1));
            expiredConversation.AddReference(AiReferenceKind.Event, Guid.CreateVersion7(), "Secret event", "Secret reference summary", tenantA.UserId, expiredAt.AddMinutes(2));
            var action = expiredConversation.ProposeAction(
                AiProposedActionKind.CreateEventDraft,
                "{\"title\":\"Secret proposed event\"}",
                messageId: null,
                tenantA.UserId,
                expiredAt.AddMinutes(3));
            var run = expiredConversation.QueueRun("fake", "fake-ai-assistant-v1", expiredAt.AddMinutes(4));
            run.Fail("provider_error", "Secret provider failure", expiredAt.AddMinutes(5));
            expiredConversation.UpdatedAt = expiredAt;
            expiredConversationId = expiredConversation.Id;

            var otherTenantConversation = NewConversation(tenantB, "Other tenant secret");
            otherTenantConversation.CreatedAt = expiredAt;
            otherTenantConversation.AddMessage(AiMessageRole.User, "Other tenant prompt", tenantB.UserId, expiredAt.AddMinutes(1));
            otherTenantConversation.UpdatedAt = expiredAt;
            otherTenantConversationId = otherTenantConversation.Id;

            var recentConversation = NewConversation(tenantA, "Recent AI plan");
            recentConversation.Id = Guid.CreateVersion7();
            recentConversation.CreatedAt = now.AddDays(-1);
            recentConversation.AddMessage(AiMessageRole.User, "Recent prompt content", tenantA.UserId, now.AddHours(-1));

            seedContext.AiConversations.AddRange(expiredConversation, otherTenantConversation, recentConversation);
            seedContext.AiToolExecutions.Add(new AiToolExecution
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantA.TenantId,
                ProposedActionId = action.Id,
                ToolName = "CreateEventDraft",
                StartedAt = expiredAt.AddMinutes(6),
                CompletedAt = expiredAt.AddMinutes(7),
                Succeeded = false,
                FailureCode = "tool_failure",
                FailureMessage = "Secret tool failure"
            });
            await seedContext.SaveChangesAsync();
        }

        await using (var cleanupContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantA.TenantId)))
        {
            var repository = new AiConversationRepository(cleanupContext);

            var result = await repository.RedactExpiredConversationsAsync(
                now.AddDays(-30),
                retentionDays: 30,
                now,
                dryRun: false,
                CancellationToken.None);

            await Assert.That(result.EligibleConversations).IsEqualTo(1);
            await Assert.That(result.RedactedConversations).IsEqualTo(1);
            await Assert.That(result.RedactedMessages).IsEqualTo(1);
            await Assert.That(result.RedactedRuns).IsEqualTo(1);
            await Assert.That(result.RedactedReferences).IsEqualTo(1);
            await Assert.That(result.RedactedProposedActions).IsEqualTo(1);
            await Assert.That(result.RedactedToolExecutions).IsEqualTo(1);
        }

        await using var verifyContext = fixture.CreateDbContext();
        var redacted = await verifyContext.AiConversations
            .IgnoreQueryFilters()
            .Include(conversation => conversation.Messages)
            .Include(conversation => conversation.Runs)
            .Include(conversation => conversation.References)
            .Include(conversation => conversation.ProposedActions)
            .FirstAsync(conversation => conversation.Id == expiredConversationId);
        var toolExecution = await verifyContext.AiToolExecutions
            .IgnoreQueryFilters()
            .SingleAsync(execution => execution.ProposedActionId == redacted.ProposedActions.Single().Id);
        var otherTenant = await verifyContext.AiConversations
            .IgnoreQueryFilters()
            .Include(conversation => conversation.Messages)
            .FirstAsync(conversation => conversation.Id == otherTenantConversationId);

        await Assert.That(redacted.IsDeleted).IsTrue();
        await Assert.That(redacted.DeletedAt).IsEqualTo(now);
        await Assert.That(redacted.Title).IsEqualTo("[redacted AI conversation]");
        await Assert.That(redacted.Messages.Single().Content).IsEqualTo("[redacted by AI retention policy]");
        await Assert.That(redacted.Runs.Single().FailureMessage).IsNull();
        await Assert.That(redacted.References.Single().DisplayName).IsEqualTo("[redacted reference]");
        await Assert.That(redacted.References.Single().Summary).IsNull();
        await Assert.That(redacted.ProposedActions.Single().PayloadJson).IsEqualTo("{}");
        await Assert.That(toolExecution.FailureMessage).IsNull();
        await Assert.That(otherTenant.IsDeleted).IsFalse();
        await Assert.That(otherTenant.Messages.Single().Content).IsEqualTo("Other tenant prompt");
    }

    [Test]
    public async Task RedactExpiredConversationsAsync_WhenDryRun_DoesNotRedactContent()
    {
        await fixture.ResetAsync();
        var now = new DateTime(2026, 06, 03, 12, 0, 0, DateTimeKind.Utc);
        var tenant = await SeedTenantAndUserAsync("ai-retention-dry-run");

        await using (var seedContext = fixture.CreateDbContext())
        {
            var conversation = NewConversation(tenant, "Dry run secret");
            conversation.CreatedAt = now.AddDays(-60);
            conversation.AddMessage(AiMessageRole.User, "Dry run prompt", tenant.UserId, now.AddDays(-59));
            conversation.UpdatedAt = now.AddDays(-59);
            seedContext.AiConversations.Add(conversation);
            await seedContext.SaveChangesAsync();
        }

        await using (var cleanupContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenant.TenantId)))
        {
            var repository = new AiConversationRepository(cleanupContext);
            var result = await repository.RedactExpiredConversationsAsync(
                now.AddDays(-30),
                retentionDays: 30,
                now,
                dryRun: true,
                CancellationToken.None);

            await Assert.That(result.EligibleConversations).IsEqualTo(1);
            await Assert.That(result.RedactedConversations).IsEqualTo(0);
            await Assert.That(result.DryRun).IsTrue();
        }

        await using var verifyContext = fixture.CreateDbContext();
        var saved = await verifyContext.AiConversations
            .IgnoreQueryFilters()
            .Include(conversation => conversation.Messages)
            .SingleAsync(conversation => conversation.Title == "Dry run secret");

        await Assert.That(saved.IsDeleted).IsFalse();
        await Assert.That(saved.Messages.Single().Content).IsEqualTo("Dry run prompt");
    }

    [Test]
    public async Task HardDeleteUserConversationGraphAsync_DeletesTargetGraphsAcrossTenants_AndLeavesUnrelatedGraphByteEquivalent()
    {
        await fixture.ResetAsync();
        var tenantA = await SeedTenantAndUserAsync("ai-delete-a");
        var tenantB = await SeedTenantAndUserAsync("ai-delete-b", tenantA.UserId);
        var tenantC = await SeedTenantAndUserAsync("ai-delete-c");

        var targetA = await SeedConversationGraphAsync(tenantA, "Target A", DateTime.UtcNow.AddHours(-3), softDeleted: true);
        var targetB = await SeedConversationGraphAsync(tenantB, "Target B", DateTime.UtcNow.AddHours(-2), softDeleted: true);
        var unrelated = await SeedConversationGraphAsync(tenantC, "Unrelated", DateTime.UtcNow.AddHours(-1));

        byte[] unrelatedBefore;
        await using (var beforeContext = fixture.CreateDbContext())
        {
            unrelatedBefore = await SnapshotConversationGraphAsync(beforeContext, unrelated.ConversationId);
        }

        await using (var deleteContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantA.TenantId)))
        {
            var repository = new AiConversationRepository(deleteContext);
            var deleted = await repository.HardDeleteUserConversationGraphAsync(tenantA.UserId, CancellationToken.None);

            await Assert.That(deleted).IsEqualTo(2);
        }

        await using (var verifyContext = fixture.CreateDbContext())
        {
            await Assert.That(await verifyContext.AiConversations.IgnoreQueryFilters()
                .CountAsync(conversation => conversation.UserId == tenantA.UserId)).IsEqualTo(0);
            await Assert.That(await verifyContext.AiConversations.IgnoreQueryFilters()
                .CountAsync(conversation => conversation.UserId == tenantB.UserId)).IsEqualTo(0);
            await Assert.That(await verifyContext.AiMessages.IgnoreQueryFilters()
                .CountAsync(message => message.ConversationId == targetA.ConversationId || message.ConversationId == targetB.ConversationId)).IsEqualTo(0);
            await Assert.That(await verifyContext.AiRuns.IgnoreQueryFilters()
                .CountAsync(run => run.ConversationId == targetA.ConversationId || run.ConversationId == targetB.ConversationId)).IsEqualTo(0);
            await Assert.That(await verifyContext.AiConversationReferences.IgnoreQueryFilters()
                .CountAsync(reference => reference.ConversationId == targetA.ConversationId || reference.ConversationId == targetB.ConversationId)).IsEqualTo(0);
            await Assert.That(await verifyContext.AiProposedActions.IgnoreQueryFilters()
                .CountAsync(action => action.ConversationId == targetA.ConversationId || action.ConversationId == targetB.ConversationId)).IsEqualTo(0);
            await Assert.That(await verifyContext.AiToolExecutions.IgnoreQueryFilters()
                .CountAsync(execution => execution.ProposedActionId == targetA.ProposedActionId || execution.ProposedActionId == targetB.ProposedActionId)).IsEqualTo(0);
        }

        byte[] unrelatedAfter;
        await using (var afterContext = fixture.CreateDbContext())
        {
            unrelatedAfter = await SnapshotConversationGraphAsync(afterContext, unrelated.ConversationId);
        }

        await Assert.That(unrelatedAfter).IsEquivalentTo(unrelatedBefore);
    }

    [Test]
    public async Task HardDeleteUserConversationGraphAsync_WhenCancelled_BeforeExecution_LeavesRowsUntouched()
    {
        await fixture.ResetAsync();
        var tenant = await SeedTenantAndUserAsync("ai-delete-cancel");
        var graph = await SeedConversationGraphAsync(tenant, "Cancelled", DateTime.UtcNow.AddHours(-1));
        byte[] before;

        await using (var snapshotContext = fixture.CreateDbContext())
        {
            before = await SnapshotConversationGraphAsync(snapshotContext, graph.ConversationId);
        }

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        bool cancelled = false;

        await using (var deleteContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenant.TenantId)))
        {
            var repository = new AiConversationRepository(deleteContext);
            try
            {
                await repository.HardDeleteUserConversationGraphAsync(tenant.UserId, cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
        }

        await Assert.That(cancelled).IsTrue();

        await using (var afterContext = fixture.CreateDbContext())
        {
            var after = await SnapshotConversationGraphAsync(afterContext, graph.ConversationId);
            await Assert.That(after).IsEquivalentTo(before);
        }
    }

    [Test]
    public async Task HardDeleteUserConversationGraphAsync_IsIdempotentOnRepeatedErase()
    {
        await fixture.ResetAsync();
        var tenant = await SeedTenantAndUserAsync("ai-delete-idempotent");
        var graph = await SeedConversationGraphAsync(tenant, "Idempotent", DateTime.UtcNow.AddHours(-1));

        await using (var deleteContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenant.TenantId)))
        {
            var repository = new AiConversationRepository(deleteContext);
            var first = await repository.HardDeleteUserConversationGraphAsync(tenant.UserId, CancellationToken.None);
            var second = await repository.HardDeleteUserConversationGraphAsync(tenant.UserId, CancellationToken.None);

            await Assert.That(first).IsEqualTo(1);
            await Assert.That(second).IsEqualTo(0);
        }

        await using (var verifyContext = fixture.CreateDbContext())
        {
            await Assert.That(await verifyContext.AiConversations.IgnoreQueryFilters()
                .CountAsync(conversation => conversation.Id == graph.ConversationId)).IsEqualTo(0);
            await Assert.That(await verifyContext.AiMessages.IgnoreQueryFilters()
                .CountAsync(message => message.ConversationId == graph.ConversationId)).IsEqualTo(0);
            await Assert.That(await verifyContext.AiRuns.IgnoreQueryFilters()
                .CountAsync(run => run.ConversationId == graph.ConversationId)).IsEqualTo(0);
            await Assert.That(await verifyContext.AiConversationReferences.IgnoreQueryFilters()
                .CountAsync(reference => reference.ConversationId == graph.ConversationId)).IsEqualTo(0);
            await Assert.That(await verifyContext.AiProposedActions.IgnoreQueryFilters()
                .CountAsync(action => action.ConversationId == graph.ConversationId)).IsEqualTo(0);
            await Assert.That(await verifyContext.AiToolExecutions.IgnoreQueryFilters()
                .CountAsync(execution => execution.ProposedActionId == graph.ProposedActionId)).IsEqualTo(0);
        }
    }

    private async Task<AiConversationGraphSeed> SeedConversationGraphAsync(
        AiTestScope scope,
        string title,
        DateTime utcNow,
        bool softDeleted = false)
    {
        await using var context = fixture.CreateDbContext();
        var conversation = NewConversation(scope, title);
        conversation.CreatedAt = utcNow;
        conversation.UpdatedAt = utcNow;

        conversation.AddMessage(AiMessageRole.User, $"{title} prompt", scope.UserId, utcNow.AddMinutes(1));
        var assistantMessage = conversation.AddMessage(AiMessageRole.Assistant, $"{title} assistant", null, utcNow.AddMinutes(2));
        var run = conversation.QueueRun("fake", "fake-ai-assistant-v1", utcNow.AddMinutes(3));
        run.Start(utcNow.AddMinutes(4));
        conversation.AddReference(
            AiReferenceKind.Event,
            Guid.CreateVersion7(),
            $"{title} reference",
            $"{title} summary",
            scope.UserId,
            utcNow.AddMinutes(5));
        var proposedAction = conversation.ProposeAction(
            AiProposedActionKind.CreateEventDraft,
            $"{{\"title\":\"{title}\"}}",
            assistantMessage.Id,
            scope.UserId,
            utcNow.AddMinutes(6));

        if (softDeleted)
        {
            conversation.IsDeleted = true;
            conversation.DeletedAt = utcNow.AddMinutes(9);
            conversation.DeletedBy = scope.UserId;
        }

        context.AiConversations.Add(conversation);
        context.AiToolExecutions.Add(new AiToolExecution
        {
            Id = Guid.CreateVersion7(),
            TenantId = scope.TenantId,
            ProposedActionId = proposedAction.Id,
            ToolName = "CreateEventDraft",
            StartedAt = utcNow.AddMinutes(7),
            CompletedAt = utcNow.AddMinutes(8),
            Succeeded = true
        });
        await context.SaveChangesAsync();
        return new AiConversationGraphSeed(conversation.Id, proposedAction.Id);
    }

    private static async Task<byte[]> SnapshotConversationGraphAsync(ExploreDbContext context, Guid conversationId)
    {
        var conversation = await context.AiConversations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(item => item.Id == conversationId);
        var messageBytes = await context.AiMessages
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(message => message.ConversationId == conversationId)
            .OrderBy(message => message.Sequence)
            .Select(message => new
            {
                message.Id,
                message.TenantId,
                message.ConversationId,
                message.Sequence,
                message.RoleId,
                message.Content,
                message.ImageAttachmentsJson,
                message.CreatedAt,
                message.CreatedBy
            })
            .ToListAsync();
        var runBytes = await context.AiRuns
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(run => run.ConversationId == conversationId)
            .OrderBy(run => run.QueuedAt)
            .Select(run => new
            {
                run.Id,
                run.TenantId,
                run.ConversationId,
                run.StatusId,
                run.Provider,
                run.ModelId,
                run.QueuedAt,
                run.StartedAt,
                run.CompletedAt,
                run.FailureCode,
                run.FailureMessage
            })
            .ToListAsync();
        var referenceBytes = await context.AiConversationReferences
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(reference => reference.ConversationId == conversationId)
            .OrderBy(reference => reference.CreatedAt)
            .Select(reference => new
            {
                reference.Id,
                reference.TenantId,
                reference.ConversationId,
                reference.KindId,
                reference.ReferenceId,
                reference.DisplayName,
                reference.Summary,
                reference.CreatedAt,
                reference.CreatedBy
            })
            .ToListAsync();
        var proposedActions = await context.AiProposedActions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(action => action.ConversationId == conversationId)
            .OrderBy(action => action.CreatedAt)
            .Select(action => new
            {
                action.Id,
                action.TenantId,
                action.ConversationId,
                action.MessageId,
                action.ActingActorId,
                action.KindId,
                action.StatusId,
                action.PayloadJson,
                action.ConfirmedBy,
                action.ConfirmedAt,
                action.RejectedBy,
                action.RejectedAt,
                action.ResultResourceId,
                action.FailureCode,
                action.FailureMessage,
                action.CreatedAt,
                action.CreatedBy
            })
            .ToListAsync();
        var proposedActionIds = proposedActions.Select(action => action.Id).ToArray();
        var toolExecutions = await context.AiToolExecutions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(execution => proposedActionIds.Contains(execution.ProposedActionId))
            .OrderBy(execution => execution.StartedAt)
            .Select(execution => new
            {
                execution.Id,
                execution.TenantId,
                execution.ProposedActionId,
                execution.ToolName,
                execution.StartedAt,
                execution.CompletedAt,
                execution.Succeeded,
                execution.FailureCode,
                execution.FailureMessage
            })
            .ToListAsync();

        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            conversation.Id,
            conversation.TenantId,
            conversation.UserId,
            conversation.ActorId,
            conversation.StatusId,
            conversation.Title,
            conversation.Provider,
            conversation.ModelId,
            conversation.BlockedReason,
            conversation.LastMessageSequence,
            conversation.CreatedAt,
            conversation.CreatedBy,
            conversation.UpdatedAt,
            conversation.UpdatedBy,
            conversation.IsDeleted,
            conversation.DeletedAt,
            conversation.DeletedBy,
            conversation.ConcurrencyStamp,
            Messages = messageBytes,
            Runs = runBytes,
            References = referenceBytes,
            ProposedActions = proposedActions,
            ToolExecutions = toolExecutions
        });
    }

    private async Task<AiTestScope> SeedTenantAndUserAsync(string slugPrefix, Guid? userId = null)
    {
        await using var context = fixture.CreateDbContext();
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            FullName = $"AI Test {slugPrefix}",
            Slug = $"ai-test-{slugPrefix}-{Guid.NewGuid().ToString("N")[..8]}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
        };
        context.Tenants.Add(tenant);

        User user;
        if (userId is { } existingUserId)
        {
            user = (await context.Users.FindAsync(existingUserId))!;
        }
        else
        {
            user = new User
            {
                Id = Guid.NewGuid(),
                Pii = new UserPii
                {
                    Email = $"ai-{slugPrefix}-{Guid.NewGuid():N}@example.com",
                    FirstName = "AI",
                    LastName = "Tester",
                },
                EmailVerified = true,
                CreatedAt = DateTime.UtcNow,
            };
            context.Users.Add(user);
        }

        await context.SaveChangesAsync();
        return new AiTestScope(tenant.Id, user.Id, Guid.CreateVersion7());
    }

    private static AiConversation NewConversation(AiTestScope scope, string title) =>
        new()
        {
            Id = scope.ConversationId,
            TenantId = scope.TenantId,
            UserId = scope.UserId,
            Status = AiConversationStatus.Active,
            Title = title,
            Provider = "fake",
            ModelId = "fake-ai-assistant-v1",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = scope.UserId,
            ConcurrencyStamp = Guid.CreateVersion7(),
        };

    private static AiConversation NewRunningConversation(
        AiTestScope scope,
        string title,
        DateTime queuedAt,
        DateTime startedAt)
    {
        var conversation = NewConversation(scope, title);
        var run = conversation.QueueRun("fake", "fake-ai-assistant-v1", queuedAt);
        run.Start(startedAt);
        return conversation;
    }

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;

    private sealed record AiTestScope(Guid TenantId, Guid UserId, Guid ConversationId);
    private sealed record AiConversationGraphSeed(Guid ConversationId, Guid ProposedActionId);
}
