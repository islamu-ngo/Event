using Explore.Application.Contracts.Persistence;
using Explore.Application.Models;
using Explore.Domain.Ai;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class AiConversationRepository : GenericRepository<AiConversation, Guid>, IAiConversationRepository
{
    private readonly ExploreDbContext _dbContext;

    public AiConversationRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<AiConversation?> GetByIdWithDetailsAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        return await IncludeConversationDetails(_dbContext.AiConversations.AsNoTracking())
            .FirstOrDefaultAsync(conversation => conversation.Id == conversationId, cancellationToken);
    }

    public async Task<AiConversation?> GetByIdForUpdateAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        return await IncludeConversationDetails(_dbContext.AiConversations)
            .FirstOrDefaultAsync(conversation => conversation.Id == conversationId, cancellationToken);
    }

    public async Task<int> HardDeleteUserConversationGraphAsync(Guid subjectId, CancellationToken cancellationToken)
    {
        RequireId(subjectId, nameof(subjectId));

        string reason = TenantFilterBypassReasons.UserPrivacyErasure;

        // ponytail: root delete relies on verified FK cascade shape for the entire AI graph.
        return await _dbContext.AiConversations
            .IgnoreAllFilters(reason)
            .Where(conversation => conversation.UserId == subjectId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AiConversation>> ListRecentForUserAsync(
        Guid userId,
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            return [];
        }

        var boundedLimit = Math.Min(limit, 100);

        return await _dbContext.AiConversations
            .AsNoTracking()
            .Where(conversation => conversation.UserId == userId)
            .OrderByDescending(conversation => conversation.UpdatedAt ?? conversation.CreatedAt)
            .ThenByDescending(conversation => conversation.Id)
            .Take(boundedLimit)
            .ToListAsync(cancellationToken);
    }

    public override async Task Update(AiConversation entity)
    {
        var entry = _dbContext.Entry(entity);

        if (entry.State != EntityState.Detached)
        {
            _dbContext.ChangeTracker.DetectChanges();

            if (entry.State == EntityState.Modified)
            {
                await PersistAggregateGraphAsync(entity);
                return;
            }

            await _dbContext.SaveChangesAsync();
            return;
        }

        await PersistAggregateGraphAsync(entity);
    }

    private async Task PersistAggregateGraphAsync(AiConversation entity)
    {
        var statusId = entity.StatusId;
        var title = entity.Title;
        var provider = entity.Provider;
        var modelId = entity.ModelId;
        var blockedReason = entity.BlockedReason;
        var lastMessageSequence = entity.LastMessageSequence;
        var actorId = entity.ActorId;
        var updatedAt = entity.UpdatedAt;
        var updatedBy = entity.UpdatedBy;
        var concurrencyStamp = Guid.CreateVersion7();

        var affectedRows = await _dbContext.AiConversations
            .Where(conversation => conversation.Id == entity.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(conversation => conversation.StatusId, statusId)
                .SetProperty(conversation => conversation.Title, title)
                .SetProperty(conversation => conversation.Provider, provider)
                .SetProperty(conversation => conversation.ModelId, modelId)
                .SetProperty(conversation => conversation.BlockedReason, blockedReason)
                .SetProperty(conversation => conversation.LastMessageSequence, lastMessageSequence)
                .SetProperty(conversation => conversation.ActorId, actorId)
                .SetProperty(conversation => conversation.UpdatedAt, updatedAt)
                .SetProperty(conversation => conversation.UpdatedBy, updatedBy)
                .SetProperty(conversation => conversation.ConcurrencyStamp, concurrencyStamp));

        if (affectedRows == 0)
        {
            throw new DbUpdateConcurrencyException("The AI conversation aggregate could not be updated.");
        }

        var messages = entity.Messages.ToList();
        var runs = entity.Runs.ToList();
        var references = entity.References.ToList();
        var proposedActions = entity.ProposedActions.ToList();

        foreach (var message in messages)
        {
            message.Conversation = null;
        }

        foreach (var run in runs)
        {
            run.Conversation = null;
        }

        foreach (var reference in references)
        {
            reference.Conversation = null;
        }

        foreach (var action in proposedActions)
        {
            action.Conversation = null;
            action.Message = null;
        }

        _dbContext.ChangeTracker.Clear();

        var messageIds = messages.Select(message => message.Id).ToArray();
        var existingMessageIds = await _dbContext.AiMessages
            .Where(message => messageIds.Contains(message.Id))
            .Select(message => message.Id)
            .ToListAsync();
        _dbContext.AiMessages.AddRange(messages.Where(message => !existingMessageIds.Contains(message.Id)));

        var referenceIds = references.Select(reference => reference.Id).ToArray();
        var existingReferenceIds = await _dbContext.AiConversationReferences
            .Where(reference => referenceIds.Contains(reference.Id))
            .Select(reference => reference.Id)
            .ToListAsync();
        _dbContext.AiConversationReferences.AddRange(references.Where(reference => !existingReferenceIds.Contains(reference.Id)));

        var runIds = runs.Select(run => run.Id).ToArray();
        var existingRunIds = await _dbContext.AiRuns
            .Where(run => runIds.Contains(run.Id))
            .Select(run => run.Id)
            .ToListAsync();
        _dbContext.AiRuns.AddRange(runs.Where(run => !existingRunIds.Contains(run.Id)));

        var proposedActionIds = proposedActions.Select(action => action.Id).ToArray();
        var existingProposedActionIds = await _dbContext.AiProposedActions
            .Where(action => proposedActionIds.Contains(action.Id))
            .Select(action => action.Id)
            .ToListAsync();
        _dbContext.AiProposedActions.AddRange(proposedActions.Where(action => !existingProposedActionIds.Contains(action.Id)));

        await _dbContext.SaveChangesAsync();

        foreach (var run in runs.Where(run => existingRunIds.Contains(run.Id)))
        {
            await _dbContext.AiRuns
                .Where(existingRun => existingRun.Id == run.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(existingRun => existingRun.StatusId, run.StatusId)
                    .SetProperty(existingRun => existingRun.StartedAt, run.StartedAt)
                    .SetProperty(existingRun => existingRun.CompletedAt, run.CompletedAt)
                    .SetProperty(existingRun => existingRun.FailureCode, run.FailureCode)
                    .SetProperty(existingRun => existingRun.FailureMessage, run.FailureMessage));
        }

        foreach (var action in proposedActions.Where(action => existingProposedActionIds.Contains(action.Id)))
        {
            await _dbContext.AiProposedActions
                .Where(existingAction => existingAction.Id == action.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(existingAction => existingAction.StatusId, action.StatusId)
                    .SetProperty(existingAction => existingAction.ConfirmedBy, action.ConfirmedBy)
                    .SetProperty(existingAction => existingAction.ConfirmedAt, action.ConfirmedAt)
                    .SetProperty(existingAction => existingAction.RejectedBy, action.RejectedBy)
                    .SetProperty(existingAction => existingAction.RejectedAt, action.RejectedAt)
                    .SetProperty(existingAction => existingAction.ResultResourceId, action.ResultResourceId)
                    .SetProperty(existingAction => existingAction.FailureCode, action.FailureCode)
                    .SetProperty(existingAction => existingAction.FailureMessage, action.FailureMessage));
        }

        entity.StatusId = statusId;
        entity.Title = title;
        entity.Provider = provider;
        entity.ModelId = modelId;
        entity.BlockedReason = blockedReason;
        entity.LastMessageSequence = lastMessageSequence;
        entity.ActorId = actorId;
        entity.UpdatedAt = updatedAt;
        entity.UpdatedBy = updatedBy;
    }

    public async Task<int> CountUserMessagesSinceAsync(
        Guid userId,
        DateTime sinceUtc,
        CancellationToken cancellationToken)
    {
        return await _dbContext.AiMessages
            .AsNoTracking()
            .Where(message => message.RoleId == (int)AiMessageRole.User)
            .Where(message => message.CreatedBy == userId)
            .Where(message => message.CreatedAt >= sinceUtc)
            .CountAsync(cancellationToken);
    }

    public async Task<int> CountTenantMessagesSinceAsync(
        DateTime sinceUtc,
        CancellationToken cancellationToken)
    {
        return await _dbContext.AiMessages
            .AsNoTracking()
            .Where(message => message.RoleId == (int)AiMessageRole.User)
            .Where(message => message.CreatedAt >= sinceUtc)
            .CountAsync(cancellationToken);
    }

    public async Task<int> ReleaseStaleRunningConversationsForUserAsync(
        Guid userId,
        DateTime staleBeforeUtc,
        string failureCode,
        string failureMessage,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var activeRunStatuses = new[]
        {
            (int)AiRunStatus.Queued,
            (int)AiRunStatus.InProgress
        };

        var staleConversationIds = await _dbContext.AiConversations
            .AsNoTracking()
            .Where(conversation => conversation.UserId == userId)
            .Where(conversation => conversation.StatusId == (int)AiConversationStatus.Running)
            .Where(conversation => conversation.Runs.Any(run =>
                activeRunStatuses.Contains(run.StatusId)
                && (run.StartedAt ?? run.QueuedAt) <= staleBeforeUtc))
            .Where(conversation => !conversation.Runs.Any(run =>
                activeRunStatuses.Contains(run.StatusId)
                && (run.StartedAt ?? run.QueuedAt) > staleBeforeUtc))
            .Select(conversation => conversation.Id)
            .ToListAsync(cancellationToken);

        if (staleConversationIds.Count == 0)
        {
            return 0;
        }

        var normalizedFailureCode = string.IsNullOrWhiteSpace(failureCode)
            ? "stale_ai_run_released"
            : failureCode.Trim();
        var normalizedFailureMessage = string.IsNullOrWhiteSpace(failureMessage)
            ? "AI run was released after it stopped reporting progress."
            : failureMessage.Trim();

        await _dbContext.AiRuns
            .Where(run => staleConversationIds.Contains(run.ConversationId))
            .Where(run => activeRunStatuses.Contains(run.StatusId))
            .Where(run => (run.StartedAt ?? run.QueuedAt) <= staleBeforeUtc)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(run => run.StatusId, (int)AiRunStatus.Failed)
                .SetProperty(run => run.CompletedAt, utcNow)
                .SetProperty(run => run.FailureCode, normalizedFailureCode)
                .SetProperty(run => run.FailureMessage, normalizedFailureMessage),
                cancellationToken);

        return await _dbContext.AiConversations
            .Where(conversation => staleConversationIds.Contains(conversation.Id))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(conversation => conversation.StatusId, (int)AiConversationStatus.Active)
                .SetProperty(conversation => conversation.BlockedReason, (string?)null)
                .SetProperty(conversation => conversation.UpdatedAt, utcNow)
                .SetProperty(conversation => conversation.UpdatedBy, userId),
                cancellationToken);
    }

    public async Task<int> CountRunningConversationsForUserAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.AiConversations
            .AsNoTracking()
            .Where(conversation => conversation.UserId == userId)
            .Where(conversation => conversation.StatusId == (int)AiConversationStatus.Running)
            .CountAsync(cancellationToken);
    }

    public async Task<AiProposedAction?> GetProposedActionForUpdateAsync(
        Guid proposedActionId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.AiProposedActions
            .Include(action => action.Conversation)
                .ThenInclude(conversation => conversation!.Messages)
            .Include(action => action.Message)
            .FirstOrDefaultAsync(action => action.Id == proposedActionId, cancellationToken);
    }

    public async Task UpdateProposedActionAsync(AiProposedAction proposedAction, CancellationToken cancellationToken)
    {
        await _dbContext.AiProposedActions
            .Where(existingAction => existingAction.Id == proposedAction.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(existingAction => existingAction.StatusId, proposedAction.StatusId)
                .SetProperty(existingAction => existingAction.ConfirmedBy, proposedAction.ConfirmedBy)
                .SetProperty(existingAction => existingAction.ConfirmedAt, proposedAction.ConfirmedAt)
                .SetProperty(existingAction => existingAction.RejectedBy, proposedAction.RejectedBy)
                .SetProperty(existingAction => existingAction.RejectedAt, proposedAction.RejectedAt)
                .SetProperty(existingAction => existingAction.ResultResourceId, proposedAction.ResultResourceId)
                .SetProperty(existingAction => existingAction.FailureCode, proposedAction.FailureCode)
                .SetProperty(existingAction => existingAction.FailureMessage, proposedAction.FailureMessage),
                cancellationToken);
    }

    public async Task CreateToolExecutionAsync(AiToolExecution toolExecution, CancellationToken cancellationToken)
    {
        await _dbContext.AiToolExecutions.AddAsync(toolExecution, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AiToolExecution>> ListToolExecutionsForProposedActionAsync(
        Guid proposedActionId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.AiToolExecutions
            .AsNoTracking()
            .Where(execution => execution.ProposedActionId == proposedActionId)
            .OrderByDescending(execution => execution.StartedAt)
            .ThenByDescending(execution => execution.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<AiRetentionCleanupResult> RedactExpiredConversationsAsync(
        DateTime cutoffUtc,
        int retentionDays,
        DateTime utcNow,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        var conversationIds = await _dbContext.AiConversations
            .AsNoTracking()
            .Where(conversation => (conversation.UpdatedAt ?? conversation.CreatedAt) <= cutoffUtc)
            .OrderBy(conversation => conversation.UpdatedAt ?? conversation.CreatedAt)
            .Select(conversation => conversation.Id)
            .ToListAsync(cancellationToken);

        if (conversationIds.Count == 0)
        {
            return new AiRetentionCleanupResult(cutoffUtc, retentionDays, 0, 0, 0, 0, 0, 0, 0, dryRun);
        }

        var proposedActionIds = await _dbContext.AiProposedActions
            .AsNoTracking()
            .Where(action => conversationIds.Contains(action.ConversationId))
            .Select(action => action.Id)
            .ToListAsync(cancellationToken);

        var messageCount = await _dbContext.AiMessages
            .AsNoTracking()
            .Where(message => conversationIds.Contains(message.ConversationId))
            .CountAsync(cancellationToken);
        var runCount = await _dbContext.AiRuns
            .AsNoTracking()
            .Where(run => conversationIds.Contains(run.ConversationId))
            .CountAsync(cancellationToken);
        var referenceCount = await _dbContext.AiConversationReferences
            .AsNoTracking()
            .Where(reference => conversationIds.Contains(reference.ConversationId))
            .CountAsync(cancellationToken);
        var proposedActionCount = proposedActionIds.Count;
        var toolExecutionCount = proposedActionIds.Count == 0
            ? 0
            : await _dbContext.AiToolExecutions
                .AsNoTracking()
                .Where(execution => proposedActionIds.Contains(execution.ProposedActionId))
                .CountAsync(cancellationToken);

        if (dryRun)
        {
            return new AiRetentionCleanupResult(
                cutoffUtc,
                retentionDays,
                conversationIds.Count,
                0,
                0,
                0,
                0,
                0,
                0,
                dryRun);
        }

        await _dbContext.AiMessages
            .Where(message => conversationIds.Contains(message.ConversationId))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(message => message.Content, RetentionRedactedMessage),
                cancellationToken);

        await _dbContext.AiRuns
            .Where(run => conversationIds.Contains(run.ConversationId))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(run => run.FailureMessage, (string?)null),
                cancellationToken);

        await _dbContext.AiConversationReferences
            .Where(reference => conversationIds.Contains(reference.ConversationId))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(reference => reference.DisplayName, RetentionRedactedReference)
                .SetProperty(reference => reference.Summary, (string?)null),
                cancellationToken);

        await _dbContext.AiProposedActions
            .Where(action => conversationIds.Contains(action.ConversationId))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(action => action.PayloadJson, RetentionRedactedPayload)
                .SetProperty(action => action.FailureMessage, (string?)null),
                cancellationToken);

        if (proposedActionIds.Count > 0)
        {
            await _dbContext.AiToolExecutions
                .Where(execution => proposedActionIds.Contains(execution.ProposedActionId))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(execution => execution.FailureMessage, (string?)null),
                    cancellationToken);
        }

        var redactedConversationCount = await _dbContext.AiConversations
            .Where(conversation => conversationIds.Contains(conversation.Id))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(conversation => conversation.Title, RetentionRedactedTitle)
                .SetProperty(conversation => conversation.BlockedReason, (string?)null)
                .SetProperty(conversation => conversation.IsDeleted, true)
                .SetProperty(conversation => conversation.DeletedAt, utcNow)
                .SetProperty(conversation => conversation.DeletedBy, (Guid?)null)
                .SetProperty(conversation => conversation.UpdatedAt, utcNow)
                .SetProperty(conversation => conversation.UpdatedBy, (Guid?)null),
                cancellationToken);

        return new AiRetentionCleanupResult(
            cutoffUtc,
            retentionDays,
            conversationIds.Count,
            redactedConversationCount,
            messageCount,
            runCount,
            referenceCount,
            proposedActionCount,
            toolExecutionCount,
            dryRun);
    }

    private static IQueryable<AiConversation> IncludeConversationDetails(IQueryable<AiConversation> query)
    {
        return query
            .Include(conversation => conversation.Messages.OrderBy(message => message.Sequence))
            .Include(conversation => conversation.Runs.OrderBy(run => run.QueuedAt))
            .Include(conversation => conversation.References.OrderBy(reference => reference.CreatedAt))
            .Include(conversation => conversation.ProposedActions.OrderBy(action => action.CreatedAt));
    }

    private static void RequireId(Guid id, string parameterName)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A non-empty id is required.", parameterName);
        }
    }

    private const string RetentionRedactedMessage = "[redacted by AI retention policy]";
    private const string RetentionRedactedReference = "[redacted reference]";
    private const string RetentionRedactedPayload = "{}";
    private const string RetentionRedactedTitle = "[redacted AI conversation]";
}
