using System.Linq.Expressions;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class RegistrationProviderRepository(ExploreDbContext dbContext) : IRegistrationProviderRepository
{
    private const string ProviderSubmissionEffectKind = "registration.provider_submission";
    private const string ManualImportEffectKind = "registration.provider_manual_import";
    private const string ResolvedIssuePrefix = "RESOLVED_";

    public async Task<RegistrationProviderConnection?> GetConnectionAsync(Guid tenantId, Guid connectionId, CancellationToken cancellationToken)
    {
        RegistrationProviderConnection? connection = await dbContext.RegistrationProviderConnections
            .FirstOrDefaultAsync(connection => connection.TenantId == tenantId && connection.Id == connectionId, cancellationToken);
        if (connection is not null)
        {
            await dbContext.Entry(connection).Collection(row => row.ApprovedOrigins).Query()
                .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
                .LoadAsync(cancellationToken);
        }

        return connection;
    }

    public async Task<IReadOnlyList<RegistrationProviderConnection>> GetConnectionsAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await dbContext.RegistrationProviderConnections
            .AsNoTracking()
            .Include(connection => connection.ApprovedOrigins)
            .Where(connection => connection.TenantId == tenantId)
            .OrderBy(connection => connection.Name)
            .ThenBy(connection => connection.Id)
            .ToListAsync(cancellationToken);

    public async Task<RegistrationProviderBinding?> GetBindingAsync(Guid tenantId, Guid bindingId, CancellationToken cancellationToken)
    {
        RegistrationProviderBinding? binding = await dbContext.RegistrationProviderBindings
            .Include(binding => binding.Connection!)
            .Include(binding => binding.FieldMappings)
            .Include(binding => binding.OptionMappings)
            .Include(binding => binding.Capabilities)
            .FirstOrDefaultAsync(binding => binding.TenantId == tenantId && binding.Id == bindingId, cancellationToken);
        if (binding?.Connection is not null)
        {
            await dbContext.Entry(binding.Connection).Collection(row => row.ApprovedOrigins).Query()
                .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
                .LoadAsync(cancellationToken);
        }

        return binding;
    }

    public Task<bool> FormVersionBelongsToEventAsync(Guid tenantId, Guid eventId, Guid formId, Guid formVersionId, CancellationToken cancellationToken) =>
        dbContext.RegistrationFormVersions.AsNoTracking()
            .AnyAsync(version => version.TenantId == tenantId && version.EventId == eventId &&
                version.RegistrationFormId == formId && version.Id == formVersionId, cancellationToken);

    public async Task<IReadOnlyList<RegistrationProviderBinding>> GetBindingsAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await dbContext.RegistrationProviderBindings
            .AsNoTracking()
            .Include(binding => binding.Connection!)
                .ThenInclude(connection => connection.ApprovedOrigins)
            .Include(binding => binding.Capabilities)
            .Where(binding => binding.TenantId == tenantId)
            .OrderBy(binding => binding.CreatedAt)
            .ThenBy(binding => binding.Id)
            .ToListAsync(cancellationToken);

    public Task<RegistrationRequirement?> GetRequirementAsync(Guid tenantId, Guid eventId, Guid workflowId, Guid requirementId, CancellationToken cancellationToken) =>
        dbContext.RegistrationRequirements
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .Include(requirement => requirement.Channels)
            .FirstOrDefaultAsync(requirement => requirement.TenantId == tenantId && requirement.EventId == eventId &&
                requirement.RegistrationWorkflowId == workflowId && requirement.Id == requirementId && !requirement.IsDeleted, cancellationToken);

    public Task<RegistrationChannel?> GetChannelAsync(Guid tenantId, Guid eventId, Guid workflowId, Guid requirementId, Guid channelId, CancellationToken cancellationToken) =>
        dbContext.RegistrationChannels.FirstOrDefaultAsync(channel => channel.TenantId == tenantId && channel.EventId == eventId &&
            channel.RegistrationWorkflowId == workflowId && channel.RegistrationRequirementId == requirementId && channel.Id == channelId, cancellationToken);

    public Task<RegistrationProviderBinding?> GetBindingForCallbackAsync(Guid bindingId, CancellationToken cancellationToken) =>
        dbContext.RegistrationProviderBindings
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .Include(binding => binding.Connection)
            .Include(binding => binding.FieldMappings)
            .Include(binding => binding.OptionMappings)
            .Include(binding => binding.Capabilities)
            .FirstOrDefaultAsync(binding => !binding.IsDeleted && binding.Id == bindingId, cancellationToken);

    public Task<bool> HasSubmissionForBindingAsync(Guid tenantId, Guid bindingId, CancellationToken cancellationToken) =>
        dbContext.RegistrationSubmissions.AsNoTracking()
            .AnyAsync(submission => submission.TenantId == tenantId && submission.RegistrationProviderBindingId == bindingId, cancellationToken);

    public async Task<IReadOnlyList<RegistrationProviderBinding>> GetBindingsForEventAsync(Guid tenantId, Guid eventId, CancellationToken cancellationToken) =>
        await dbContext.RegistrationProviderBindings
            .AsNoTracking()
            .Include(binding => binding.Connection)
            .Include(binding => binding.FieldMappings)
            .Include(binding => binding.OptionMappings)
            .Include(binding => binding.Capabilities)
            .Where(binding => binding.TenantId == tenantId &&
                dbContext.RegistrationForms.Any(form => form.TenantId == tenantId && form.EventId == eventId && form.Id == binding.RegistrationFormId))
            .OrderBy(binding => binding.CreatedAt)
            .ThenBy(binding => binding.Id)
            .ToListAsync(cancellationToken);

    public Task<DateTime?> GetLastCallbackAtAsync(Guid tenantId, Guid bindingId, CancellationToken cancellationToken) =>
        dbContext.IncomingWebhookEffectOutboxes
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .AsNoTracking()
            .Where(effect => effect.TenantId == tenantId &&
                effect.EffectKind == ProviderSubmissionEffectKind &&
                effect.Status == OutboxMessageStatus.Completed &&
                dbContext.IncomingWebhookMessages
                    .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
                    .Any(message => message.Id == effect.IncomingWebhookMessageId &&
                        message.EventType == ProviderSubmissionEffectKind) &&
                EF.Functions.Like(effect.ProviderDecisionId, bindingId.ToString("N") + ":%"))
            .MaxAsync(effect => (DateTime?)effect.CreatedAt, cancellationToken);

    public async Task<int> CountParkedItemsAsync(Guid tenantId, Guid bindingId, CancellationToken cancellationToken) =>
        await dbContext.RegistrationSubmissions.AsNoTracking()
            .CountAsync(submission => submission.TenantId == tenantId &&
                submission.RegistrationProviderBindingId == bindingId &&
                dbContext.RegistrationSubmissionIssues.Any(issue => issue.TenantId == tenantId && issue.RegistrationSubmissionId == submission.Id) &&
                !dbContext.RegistrationSubmissionIssues.Any(issue => issue.TenantId == tenantId && issue.RegistrationSubmissionId == submission.Id && issue.Code.StartsWith(ResolvedIssuePrefix)), cancellationToken)
        + await dbContext.IncomingWebhookEffectOutboxes
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .AsNoTracking()
            .CountAsync(effect => effect.TenantId == tenantId &&
                (effect.EffectKind == ProviderSubmissionEffectKind || effect.EffectKind == ManualImportEffectKind) &&
                EF.Functions.Like(effect.ProviderDecisionId, bindingId.ToString("N") + ":%") &&
                effect.Status != OutboxMessageStatus.Completed, cancellationToken);

    public async Task<DateTime?> GetOldestPendingItemAtAsync(Guid tenantId, Guid bindingId, CancellationToken cancellationToken)
    {
        DateTime? submission = await dbContext.RegistrationSubmissions.AsNoTracking()
            .Where(row => row.TenantId == tenantId && row.RegistrationProviderBindingId == bindingId &&
                dbContext.RegistrationSubmissionIssues.Any(issue => issue.TenantId == tenantId && issue.RegistrationSubmissionId == row.Id) &&
                !dbContext.RegistrationSubmissionIssues.Any(issue => issue.TenantId == tenantId && issue.RegistrationSubmissionId == row.Id && issue.Code.StartsWith(ResolvedIssuePrefix)))
            .MinAsync(row => (DateTime?)row.CreatedAt, cancellationToken);
        DateTime? effect = await dbContext.IncomingWebhookEffectOutboxes
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .AsNoTracking()
            .Where(row => row.TenantId == tenantId && (row.EffectKind == ProviderSubmissionEffectKind || row.EffectKind == ManualImportEffectKind) &&
                EF.Functions.Like(row.ProviderDecisionId, bindingId.ToString("N") + ":%") && row.Status != OutboxMessageStatus.Completed)
            .MinAsync(row => (DateTime?)(row.NextAttemptAt ?? row.CreatedAt), cancellationToken);
        return submission is null ? effect : effect is null ? submission : submission < effect ? submission : effect;
    }

    public Task<RegistrationForm?> GetFormForExternalImportAsync(Guid tenantId, Guid eventId, Guid formId, CancellationToken cancellationToken) =>
        dbContext.RegistrationForms
            .Include(form => form.Versions)
            .ThenInclude(version => version.Sections)
            .ThenInclude(section => section.Fields)
            .ThenInclude(field => field.Options)
            .Include(form => form.Versions)
            .ThenInclude(version => version.Rules)
            .FirstOrDefaultAsync(form => form.TenantId == tenantId && form.EventId == eventId && form.Id == formId, cancellationToken);

    public Task<RegistrationForm?> GetExternalImportFormAsync(
        Guid tenantId,
        Guid eventId,
        Guid connectionId,
        string providerSurveyId,
        CancellationToken cancellationToken) =>
        dbContext.RegistrationForms
            .Include(form => form.Versions)
            .ThenInclude(version => version.Sections)
            .ThenInclude(section => section.Fields)
            .ThenInclude(field => field.Options)
            .Include(form => form.Versions)
            .ThenInclude(version => version.Rules)
            .FirstOrDefaultAsync(form => form.TenantId == tenantId && form.EventId == eventId &&
                form.Versions.Any(version => version.SourceKindId == (int)Explore.Domain.Enums.RegistrationFormVersionSourceKindEnum.ExternalImported &&
                    version.ExternalRegistrationProviderConnectionId == connectionId &&
                    version.ExternalProviderSurveyId == providerSurveyId), cancellationToken);

    public Task<RegistrationProviderSchemaRevision?> GetLatestExternalImportSchemaRevisionAsync(
        Guid tenantId,
        Guid eventId,
        Guid formId,
        Guid connectionId,
        string providerSurveyId,
        CancellationToken cancellationToken) =>
        dbContext.RegistrationFormVersions
            .AsNoTracking()
            .Where(version => version.TenantId == tenantId && version.EventId == eventId &&
                version.RegistrationFormId == formId &&
                version.SourceKindId == (int)Explore.Domain.Enums.RegistrationFormVersionSourceKindEnum.ExternalImported &&
                version.ExternalRegistrationProviderConnectionId == connectionId &&
                version.ExternalProviderSurveyId == providerSurveyId &&
                version.ExternalRegistrationProviderSchemaRevisionId != null)
            .OrderByDescending(version => version.Version)
            .Join(dbContext.RegistrationProviderSchemaRevisions.AsNoTracking(),
                version => new { version.TenantId, RevisionId = version.ExternalRegistrationProviderSchemaRevisionId!.Value },
                revision => new { revision.TenantId, RevisionId = revision.Id },
                (_, revision) => revision)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<RegistrationProviderSchemaRevision?> GetSchemaRevisionByHashAsync(
        Guid tenantId,
        Guid connectionId,
        string providerSurveyId,
        RegistrationEvidenceHash revisionHash,
        CancellationToken cancellationToken) =>
        dbContext.RegistrationProviderSchemaRevisions
            .AsNoTracking()
            .FirstOrDefaultAsync(revision => revision.TenantId == tenantId &&
                revision.RegistrationProviderConnectionId == connectionId &&
                revision.ProviderSurveyId == providerSurveyId &&
                revision.RevisionHash == revisionHash, cancellationToken);

    public async Task<IReadOnlyList<RegistrationProviderParkedItem>> GetParkedItemsForEventAsync(
        Guid tenantId,
        Guid eventId,
        int limit,
        CancellationToken cancellationToken)
    {
        List<RegistrationSubmission> submissions = await dbContext.RegistrationSubmissions
            .AsNoTracking()
            .Where(submission =>
                submission.TenantId == tenantId &&
                submission.EventId == eventId &&
                submission.RegistrationProviderBindingId != null &&
                dbContext.RegistrationSubmissionIssues.Any(issue => issue.TenantId == tenantId && issue.RegistrationSubmissionId == submission.Id) &&
                !dbContext.RegistrationSubmissionIssues.Any(issue => issue.TenantId == tenantId && issue.RegistrationSubmissionId == submission.Id && issue.Code.StartsWith(ResolvedIssuePrefix)))
            .OrderByDescending(submission => submission.CreatedAt)
            .ThenByDescending(submission => submission.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
        Guid[] submissionIds = [.. submissions.Select(submission => submission.Id)];
        List<RegistrationSubmissionIssue> issues = await dbContext.RegistrationSubmissionIssues
            .AsNoTracking()
            .Where(issue => issue.TenantId == tenantId && submissionIds.Contains(issue.RegistrationSubmissionId))
            .ToListAsync(cancellationToken);
        var parkedSubmissions = submissions
            .Select(submission => new RegistrationProviderParkedSubmission(
                submission,
                issues.Where(issue => issue.RegistrationSubmissionId == submission.Id).ToArray()))
            .ToArray();
        var bindingIds = await GetBindingsForEventAsync(tenantId, eventId, cancellationToken);
        string[] prefixes = [.. bindingIds.Select(binding => binding.Id.ToString("N") + ":")];
        List<IncomingWebhookEffectOutbox> effects = prefixes.Length == 0
            ? []
            : await dbContext.IncomingWebhookEffectOutboxes
                .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
                .Include(effect => effect.IncomingWebhookMessage)
                .AsNoTracking()
                .Where(effect => effect.TenantId == tenantId &&
                    (effect.EffectKind == ProviderSubmissionEffectKind || effect.EffectKind == ManualImportEffectKind) &&
                    effect.Status != OutboxMessageStatus.Completed)
                .Where(ProviderDecisionStartsWithAny(prefixes))
                .OrderBy(effect => effect.NextAttemptAt ?? effect.CreatedAt)
                .ThenBy(effect => effect.Id)
                .Take(limit)
                .ToListAsync(cancellationToken);
        return parkedSubmissions.Select(submission => new RegistrationProviderParkedItem(submission, null))
            .Concat(effects.Select(effect => new RegistrationProviderParkedItem(
                null,
                new RegistrationProviderParkedEffect(effect, ParseBindingId(effect.ProviderDecisionId), eventId))))
            .OrderByDescending(item => item.Submission?.Submission.CreatedAt ?? item.Effect!.Effect.CreatedAt)
            .Take(limit)
            .ToArray();
    }

    public Task<RegistrationSubmission?> GetParkedSubmissionAsync(Guid tenantId, Guid eventId, Guid submissionId, CancellationToken cancellationToken) =>
        dbContext.RegistrationSubmissions.AsNoTracking()
            .FirstOrDefaultAsync(submission => submission.TenantId == tenantId && submission.EventId == eventId && submission.Id == submissionId &&
                submission.RegistrationProviderBindingId != null &&
                dbContext.RegistrationSubmissionIssues.Any(issue => issue.TenantId == tenantId && issue.RegistrationSubmissionId == submission.Id) &&
                !dbContext.RegistrationSubmissionIssues.Any(issue => issue.TenantId == tenantId && issue.RegistrationSubmissionId == submission.Id && issue.Code.StartsWith(ResolvedIssuePrefix)), cancellationToken);

    public async Task AddSubmissionIssueAsync(RegistrationSubmissionIssue issue, CancellationToken cancellationToken) =>
        await dbContext.RegistrationSubmissionIssues.AddAsync(issue, cancellationToken);

    private static Guid ParseBindingId(string providerDecisionId) =>
        Guid.TryParseExact(providerDecisionId.Split(':', 2)[0], "N", out Guid bindingId) ? bindingId : Guid.Empty;

    private static Expression<Func<IncomingWebhookEffectOutbox, bool>> ProviderDecisionStartsWithAny(string[] prefixes)
    {
        ParameterExpression parameter = Expression.Parameter(typeof(IncomingWebhookEffectOutbox), "effect");
        Expression property = Expression.Property(parameter, nameof(IncomingWebhookEffectOutbox.ProviderDecisionId));
        Expression? body = null;
        foreach (string prefix in prefixes)
        {
            Expression startsWith = Expression.Call(property, nameof(string.StartsWith), Type.EmptyTypes, Expression.Constant(prefix));
            body = body is null ? startsWith : Expression.OrElse(body, startsWith);
        }

        return Expression.Lambda<Func<IncomingWebhookEffectOutbox, bool>>(body!, parameter);
    }

    public async Task AddConnectionAsync(RegistrationProviderConnection connection, CancellationToken cancellationToken) =>
        await dbContext.RegistrationProviderConnections.AddAsync(connection, cancellationToken);

    public async Task AddBindingAsync(RegistrationProviderBinding binding, CancellationToken cancellationToken) =>
        await dbContext.RegistrationProviderBindings.AddAsync(binding, cancellationToken);

    public async Task AddFormAsync(RegistrationForm form, CancellationToken cancellationToken) =>
        await dbContext.RegistrationForms.AddAsync(form, cancellationToken);

    public async Task AddChannelAsync(RegistrationChannel channel, CancellationToken cancellationToken) =>
        await dbContext.RegistrationChannels.AddAsync(channel, cancellationToken);

    public async Task AddSchemaRevisionAsync(RegistrationProviderSchemaRevision revision, CancellationToken cancellationToken) =>
        await dbContext.RegistrationProviderSchemaRevisions.AddAsync(revision, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
