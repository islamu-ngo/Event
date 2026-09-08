using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Ai;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
[Category("AiConversation")]
public sealed class AiConversationPrivacyErasureTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task HardDeleteUserConversationGraphAsync_DeletesEveryConversationRowForTheSubjectAcrossTenants()
    {
        await fixture.ResetAsync();
        var subjectTenantA = await SeedTenantAndUserAsync("ai-hard-delete-a");
        var subjectTenantB = await SeedTenantAndUserAsync("ai-hard-delete-b", subjectTenantA.UserId);
        var unrelatedTenant = await SeedTenantAndUserAsync("ai-hard-delete-unrelated");

        Guid[] subjectConversationIds;
        Guid[] subjectActionIds;

        await using (var seedContext = fixture.CreateDbContext())
        {
            var subjectConversationA = CreateConversationGraph(
                subjectTenantA,
                "Shared AI title",
                "Shared AI prompt",
                "Shared AI reference",
                "shared-ai-summary",
                "{\"title\":\"Shared AI title\"}");
            var subjectConversationB = CreateConversationGraph(
                subjectTenantB,
                "Shared AI title",
                "Shared AI prompt",
                "Shared AI reference",
                "shared-ai-summary",
                "{\"title\":\"Shared AI title\"}");
            var unrelatedConversation = CreateConversationGraph(
                unrelatedTenant,
                "Shared AI title",
                "Shared AI prompt",
                "Shared AI reference",
                "shared-ai-summary",
                "{\"title\":\"Shared AI title\"}");

            var subjectToolExecutionA = CreateToolExecution(subjectConversationA, subjectTenantA.TenantId);
            var subjectToolExecutionB = CreateToolExecution(subjectConversationB, subjectTenantB.TenantId);
            var unrelatedToolExecution = CreateToolExecution(unrelatedConversation, unrelatedTenant.TenantId);

            seedContext.AiConversations.AddRange(subjectConversationA, subjectConversationB, unrelatedConversation);
            seedContext.AiToolExecutions.AddRange(subjectToolExecutionA, subjectToolExecutionB, unrelatedToolExecution);
            await seedContext.SaveChangesAsync();

            subjectConversationIds = [subjectConversationA.Id, subjectConversationB.Id];
            subjectActionIds = [
                subjectConversationA.ProposedActions.Single().Id,
                subjectConversationB.ProposedActions.Single().Id
            ];
        }

        string unrelatedBefore;
        await using (var beforeContext = fixture.CreateDbContext())
        {
            unrelatedBefore = await CaptureGraphAsync(beforeContext, unrelatedTenant.ConversationId);
        }

        await using (var deleteContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(subjectTenantA.TenantId)))
        {
            await Assert.That(await deleteContext.AiConversations.CountAsync(conversation => conversation.UserId == subjectTenantA.UserId)).IsEqualTo(1);
            await Assert.That(await deleteContext.AiConversations.IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure).CountAsync(conversation => conversation.UserId == subjectTenantA.UserId)).IsEqualTo(2);

            var repository = new AiConversationRepository(deleteContext);
            int deleted = await repository.HardDeleteUserConversationGraphAsync(subjectTenantA.UserId, CancellationToken.None);
            await Assert.That(deleted).IsEqualTo(2);
            await Assert.That(await repository.HardDeleteUserConversationGraphAsync(subjectTenantA.UserId, CancellationToken.None)).IsEqualTo(0);
        }

        await using var verifyContext = fixture.CreateDbContext();
        await Assert.That(await verifyContext.AiConversations.IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure).CountAsync(conversation => conversation.UserId == subjectTenantA.UserId)).IsEqualTo(0);
        await Assert.That(await verifyContext.AiMessages.IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure).CountAsync(message => subjectConversationIds.Contains(message.ConversationId))).IsEqualTo(0);
        await Assert.That(await verifyContext.AiRuns.IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure).CountAsync(run => subjectConversationIds.Contains(run.ConversationId))).IsEqualTo(0);
        await Assert.That(await verifyContext.AiConversationReferences.IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure).CountAsync(reference => subjectConversationIds.Contains(reference.ConversationId))).IsEqualTo(0);
        await Assert.That(await verifyContext.AiProposedActions.IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure).CountAsync(action => subjectConversationIds.Contains(action.ConversationId))).IsEqualTo(0);
        await Assert.That(await verifyContext.AiToolExecutions.IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure).CountAsync(execution => subjectActionIds.Contains(execution.ProposedActionId))).IsEqualTo(0);
        await Assert.That(await CaptureGraphAsync(verifyContext, unrelatedTenant.ConversationId)).IsEqualTo(unrelatedBefore);
    }

    [Test]
    public async Task HardDeleteUserConversationGraphAsync_UsesCascadeDeletesFromConversationToChildren()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();

        await Assert.That(context.Model.FindEntityType(typeof(AiMessage))!
            .GetForeignKeys()
            .Single(foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(AiConversation))
            .DeleteBehavior).IsEqualTo(DeleteBehavior.Cascade);
        await Assert.That(context.Model.FindEntityType(typeof(AiRun))!
            .GetForeignKeys()
            .Single(foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(AiConversation))
            .DeleteBehavior).IsEqualTo(DeleteBehavior.Cascade);
        await Assert.That(context.Model.FindEntityType(typeof(AiConversationReference))!
            .GetForeignKeys()
            .Single(foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(AiConversation))
            .DeleteBehavior).IsEqualTo(DeleteBehavior.Cascade);
        await Assert.That(context.Model.FindEntityType(typeof(AiProposedAction))!
            .GetForeignKeys()
            .Single(foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(AiConversation))
            .DeleteBehavior).IsEqualTo(DeleteBehavior.Cascade);
        await Assert.That(context.Model.FindEntityType(typeof(AiToolExecution))!
            .GetForeignKeys()
            .Single(foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(AiProposedAction))
            .DeleteBehavior).IsEqualTo(DeleteBehavior.Cascade);
    }

    [Test]
    public async Task HardDeleteUserConversationGraphAsync_RollsBackWhenTheSerializableTransactionIsCancelled()
    {
        await fixture.ResetAsync();
        var subject = await SeedTenantAndUserAsync("ai-hard-delete-cancel-a");
        var unrelated = await SeedTenantAndUserAsync("ai-hard-delete-cancel-b");

        Guid subjectConversationId;

        await using (var seedContext = fixture.CreateDbContext())
        {
            var subjectConversation = CreateConversationGraph(
                subject,
                "Subject",
                "Subject prompt",
                "Subject ref",
                "subject-summary",
                "{\"title\":\"Subject\"}");
            var unrelatedConversation = CreateConversationGraph(
                unrelated,
                "Other",
                "Other prompt",
                "Other ref",
                "other-summary",
                "{\"title\":\"Other\"}");
            seedContext.AiConversations.AddRange(subjectConversation, unrelatedConversation);
            await seedContext.SaveChangesAsync();
            subjectConversationId = subjectConversation.Id;
        }

        string subjectBefore;
        string unrelatedBefore;
        await using (var beforeContext = fixture.CreateDbContext())
        {
            subjectBefore = await CaptureGraphAsync(beforeContext, subjectConversationId);
            unrelatedBefore = await CaptureGraphAsync(beforeContext, unrelated.ConversationId);
        }

        await using (var deleteContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(subject.TenantId)))
        {
            var unitOfWork = new Explore.Persistence.EfCoreUnitOfWork(deleteContext);
            await Assert.ThrowsAsync<OperationCanceledException>(() => unitOfWork.ExecuteSerializableAsync<object?>(async ct =>
            {
                var repository = new AiConversationRepository(deleteContext);
                await repository.HardDeleteUserConversationGraphAsync(subject.UserId, ct);
                throw new OperationCanceledException(ct);
            }));
        }

        await using var verifyContext = fixture.CreateDbContext();
        await Assert.That(await CaptureGraphAsync(verifyContext, subjectConversationId)).IsEqualTo(subjectBefore);
        await Assert.That(await CaptureGraphAsync(verifyContext, unrelated.ConversationId)).IsEqualTo(unrelatedBefore);
    }

    private static async Task<string> CaptureGraphAsync(ExploreDbContext context, Guid conversationId)
    {
        AiConversation conversation = await context.AiConversations
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .Include(item => item.Messages)
            .Include(item => item.Runs)
            .Include(item => item.References)
            .Include(item => item.ProposedActions)
            .SingleAsync(item => item.Id == conversationId);

        Guid[] proposedActionIds = conversation.ProposedActions.Select(action => action.Id).ToArray();
        var toolExecutions = await context.AiToolExecutions
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .Where(execution => proposedActionIds.Contains(execution.ProposedActionId))
            .OrderBy(execution => execution.StartedAt)
            .ThenBy(execution => execution.Id)
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
            .ToArrayAsync();

        return JsonSerializer.Serialize(new
        {
            conversation.Id,
            conversation.TenantId,
            conversation.UserId,
            conversation.StatusId,
            conversation.Title,
            conversation.Provider,
            conversation.ModelId,
            conversation.BlockedReason,
            conversation.LastMessageSequence,
            Messages = conversation.Messages
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
                .ToArray(),
            Runs = conversation.Runs
                .OrderBy(run => run.QueuedAt)
                .ThenBy(run => run.Id)
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
                .ToArray(),
            References = conversation.References
                .OrderBy(reference => reference.CreatedAt)
                .ThenBy(reference => reference.Id)
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
                .ToArray(),
            ProposedActions = conversation.ProposedActions
                .OrderBy(action => action.CreatedAt)
                .ThenBy(action => action.Id)
                .Select(action => new
                {
                    action.Id,
                    action.TenantId,
                    action.ConversationId,
                    action.MessageId,
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
                .ToArray(),
            ToolExecutions = toolExecutions
        });
    }

    private static AiConversation CreateConversationGraph(
        AiTestScope scope,
        string title,
        string userPrompt,
        string referenceName,
        string referenceSummary,
        string payloadJson)
    {
        var conversation = NewConversation(scope, title);
        conversation.AddMessage(AiMessageRole.User, userPrompt, scope.UserId, DateTime.UtcNow);
        var assistantMessage = conversation.AddMessage(AiMessageRole.Assistant, $"Assistant reply for {title}", null, DateTime.UtcNow.AddSeconds(1));
        conversation.AddReference(AiReferenceKind.Event, Guid.CreateVersion7(), referenceName, referenceSummary, scope.UserId, DateTime.UtcNow.AddSeconds(2));
        var run = conversation.QueueRun("fake", "fake-ai-assistant-v1", DateTime.UtcNow.AddSeconds(3));
        run.Start(DateTime.UtcNow.AddSeconds(4));
        run.Fail("provider_error", $"Provider failure for {title}", DateTime.UtcNow.AddSeconds(5));
        var proposedAction = conversation.ProposeAction(
            AiProposedActionKind.CreateEventDraft,
            payloadJson,
            assistantMessage.Id,
            scope.UserId,
            DateTime.UtcNow.AddSeconds(6));
        proposedAction.MarkFailed("provider_failure", $"Proposal failure for {title}");
        return conversation;
    }

    private static AiToolExecution CreateToolExecution(AiConversation conversation, Guid tenantId)
    {
        var proposedAction = conversation.ProposedActions.Single();
        var execution = new AiToolExecution
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            ProposedActionId = proposedAction.Id,
            ToolName = "CreateEventDraft",
            StartedAt = DateTime.UtcNow.AddSeconds(7)
        };
        execution.MarkSucceeded(DateTime.UtcNow.AddSeconds(8));
        return execution;
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

    private sealed record AiTestScope(Guid TenantId, Guid UserId, Guid ConversationId);

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;
}
