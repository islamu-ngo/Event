using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.Interfaces;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Explore.Persistence.Repositories;

public sealed class RegistrationFormAuthoringRepository(ExploreDbContext dbContext)
    : IRegistrationFormAuthoringRepository
{
    public Task<RegistrationWorkflow?> GetWorkflowAsync(
        Guid eventId,
        string purpose,
        CancellationToken cancellationToken) =>
        WorkflowGraph().AsNoTracking().FirstOrDefaultAsync(
            workflow => workflow.EventId == eventId && workflow.Purpose == purpose,
            cancellationToken);

    public Task<RegistrationWorkflow?> GetWorkflowForUpdateAsync(
        Guid eventId,
        Guid workflowId,
        CancellationToken cancellationToken) =>
        WorkflowGraph().FirstOrDefaultAsync(
            workflow => workflow.EventId == eventId && workflow.Id == workflowId,
            cancellationToken);

    public async Task<IReadOnlyList<RegistrationForm>> GetFormsAsync(
        Guid eventId,
        CancellationToken cancellationToken) =>
        await dbContext.RegistrationForms
            .AsNoTracking()
            .Where(form => form.EventId == eventId)
            .Include(form => form.Versions)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RegistrationFormVersion>> GetPublishedVersionsAsync(
        Guid eventId,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        return await VersionGraph()
            .AsNoTracking()
            .Where(version => version.EventId == eventId && !version.IsDeleted &&
                version.StatusId == (int)Explore.Domain.Enums.RegistrationFormStatusEnum.Published)
            .OrderBy(version => version.RegistrationFormId)
            .ThenBy(version => version.Version)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>> GetAttachedRequirementIdsAsync(
        Guid eventId,
        CancellationToken cancellationToken) =>
        await dbContext.ParticipationRequirementAttachments
            .AsNoTracking()
            .Where(attachment => attachment.EventId == eventId && !attachment.IsDeleted)
            .Select(attachment => attachment.RegistrationRequirementId)
            .ToHashSetAsync(cancellationToken);

    public Task<RegistrationForm?> GetFormAsync(
        Guid eventId,
        Guid formId,
        CancellationToken cancellationToken) =>
        FormGraph().AsNoTracking().FirstOrDefaultAsync(
            form => form.EventId == eventId && form.Id == formId,
            cancellationToken);

    public Task<RegistrationForm?> GetFormForUpdateAsync(
        Guid eventId,
        Guid formId,
        CancellationToken cancellationToken) =>
        FormGraph().FirstOrDefaultAsync(
            form => form.EventId == eventId && form.Id == formId,
            cancellationToken);

    public Task<RegistrationFormVersion?> GetVersionAsync(
        Guid eventId,
        Guid formId,
        Guid versionId,
        CancellationToken cancellationToken) =>
        VersionGraph().AsNoTracking().FirstOrDefaultAsync(
            version => version.EventId == eventId &&
                version.RegistrationFormId == formId && version.Id == versionId,
            cancellationToken);

    public Task<RegistrationFormVersion?> GetTemplateSourceVersionAsync(
        Guid eventId,
        Guid formId,
        Guid versionId,
        CancellationToken cancellationToken) =>
        VersionGraph()
            .IgnoreTenantFilter(TenantFilterBypassReasons.RegistrationFormTemplateSourceVersion)
            .AsNoTracking()
            .FirstOrDefaultAsync(
                version => version.EventId == eventId &&
                    version.RegistrationFormId == formId && version.Id == versionId,
                cancellationToken);

    public Task<RegistrationFormVersion?> GetVersionForUpdateAsync(
        Guid eventId,
        Guid formId,
        Guid versionId,
        CancellationToken cancellationToken) =>
        VersionGraph().FirstOrDefaultAsync(
            version => version.EventId == eventId &&
                version.RegistrationFormId == formId && version.Id == versionId,
            cancellationToken);

    public async Task CreateWorkflowAsync(
        RegistrationWorkflow workflow,
        CancellationToken cancellationToken)
    {
        await dbContext.RegistrationWorkflows.AddAsync(workflow, cancellationToken);
        await SaveChangesAsync(cancellationToken);
    }

    public Task UpdateWorkflowAsync(
        RegistrationWorkflow workflow,
        CancellationToken cancellationToken) => SaveChangesAsync(cancellationToken);

    public async Task CreateFormAsync(RegistrationForm form, CancellationToken cancellationToken)
    {
        await dbContext.RegistrationForms.AddAsync(form, cancellationToken);
        await SaveChangesAsync(cancellationToken);
    }

    public Task UpdateFormAsync(
        RegistrationForm form,
        CancellationToken cancellationToken) => SaveChangesAsync(cancellationToken);

    public Task UpdateVersionAsync(
        RegistrationFormVersion version,
        CancellationToken cancellationToken) => SaveChangesAsync(cancellationToken);

    public async Task ReorderSectionsAsync(
        RegistrationFormVersion version,
        IReadOnlyList<Guid> orderedSectionIds,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await FenceVersionHoldsAsync(cancellationToken);
        await dbContext.RegistrationFormSections
            .Where(section => section.RegistrationFormVersionId == version.Id &&
                orderedSectionIds.Contains(section.Id))
            .ExecuteUpdateAsync(setters => setters.SetProperty(section => section.Ordinal, section => -section.Ordinal),
                cancellationToken);
        await SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ReorderFieldsAsync(
        RegistrationFormVersion version,
        IReadOnlyList<Guid> orderedFieldIds,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await FenceVersionHoldsAsync(cancellationToken);
        await dbContext.RegistrationFormFields
            .Where(field => orderedFieldIds.Contains(field.Id))
            .ExecuteUpdateAsync(setters => setters.SetProperty(field => field.Ordinal, field => -field.Ordinal),
                cancellationToken);
        await SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!dbContext.Database.IsRelational() || dbContext.Database.CurrentTransaction is not null)
            {
                if (dbContext.Database.IsRelational())
                    await FenceVersionHoldsAsync(cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            dbContext.ChangeTracker.DetectChanges();
            var stamps = dbContext.ChangeTracker.Entries()
                .Where(entry => entry.Entity is IConcurrencyAware)
                .Select(entry => entry.Property(nameof(IConcurrencyAware.ConcurrencyStamp)))
                .Select(property => new
                {
                    Property = property,
                    Original = property.OriginalValue,
                    Current = property.CurrentValue,
                    Modified = property.IsModified
                }).ToArray();
            (EntityEntry Entry, Guid Stamp)[] commitProofs = [];
            void RestoreStamps()
            {
                foreach (var stamp in stamps)
                {
                    stamp.Property.OriginalValue = stamp.Original;
                    stamp.Property.CurrentValue = stamp.Current;
                    stamp.Property.IsModified = stamp.Modified;
                }
            }

            try
            {
                await dbContext.Database.CreateExecutionStrategy().ExecuteInTransactionAsync(
                    dbContext,
                    async (_, token) =>
                    {
                        commitProofs = [];
                        RestoreStamps();
                        await FenceVersionHoldsAsync(token);
                        int saved = await dbContext.SaveChangesAsync(acceptAllChangesOnSuccess: false, token);
                        commitProofs = dbContext.ChangeTracker.Entries()
                            .Where(entry => entry.State is EntityState.Added or EntityState.Modified
                                && entry.Entity is IConcurrencyAware)
                            .Select(entry => (entry, ((IConcurrencyAware)entry.Entity).ConcurrencyStamp))
                            .ToArray();
                        return saved;
                    },
                    async (_, token) =>
                    {
                        // A surviving unique graph stamp proves the fence and the writes
                        // committed together; never replay an acknowledged-lost commit.
                        foreach (var proof in commitProofs)
                        {
                            var persisted = await proof.Entry.GetDatabaseValuesAsync(token);
                            if (persisted?[nameof(IConcurrencyAware.ConcurrencyStamp)] is Guid stamp
                                && stamp == proof.Stamp)
                                return true;
                        }
                        return false;
                    }, cancellationToken);
                dbContext.ChangeTracker.AcceptAllChanges();
            }
            catch
            {
                RestoreStamps();
                throw;
            }
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConcurrencyConflictException(
                ConcurrencyConflictException.ConcurrentUpdate,
                "Registration authoring data was modified by another request. Reload and retry.",
                innerException: exception);
        }
    }

    private async Task FenceVersionHoldsAsync(CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.DetectChanges();
        var changed = dbContext.ChangeTracker.Entries()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToArray();
        Guid[] formIds = changed.Where(entry => entry.Entity is RegistrationForm)
            .Select(entry => ((RegistrationForm)entry.Entity).Id).ToArray();
        Guid[] versionIds = changed.Select(entry => entry.Entity switch
        {
            RegistrationFormVersion version => version.Id,
            RegistrationFormSection section => section.RegistrationFormVersionId,
            RegistrationFormField field => field.RegistrationFormVersionId,
            _ => Guid.Empty
        }).Where(id => id != Guid.Empty).Distinct().ToArray();
        if (formIds.Length == 0 && versionIds.Length == 0)
            return;

        Guid[] storageIds = await (from storage in dbContext.StorageObjects.AsNoTracking()
                .IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete])
                                   join submission in dbContext.RegistrationSubmissions.AsNoTracking()
                                       .IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete])
                                       on new { storage.TenantId, Id = storage.OwningResourceId }
                                       equals new { submission.TenantId, Id = (Guid?)submission.Id }
                                   where storage.OwningResourceKind == "registration_submission_sink"
                                       && (formIds.Contains(submission.RegistrationFormId) || versionIds.Contains(submission.RegistrationFormVersionId))
                                   select storage.Id).ToArrayAsync(cancellationToken);
        // Schema publication, field retention and physical removal all change
        // indirect CSV authority even though none carries a StorageObject FK.
        await new StorageObjectReferenceRepository(dbContext).FenceAsync(storageIds, cancellationToken);
    }

    private IQueryable<RegistrationWorkflow> WorkflowGraph() =>
        dbContext.RegistrationWorkflows
            .Include(workflow => workflow.Requirements)
            .ThenInclude(requirement => requirement.Channels);

    private IQueryable<RegistrationForm> FormGraph() =>
        dbContext.RegistrationForms
            .Include(form => form.Versions)
            .ThenInclude(version => version.Sections)
            .ThenInclude(section => section.Fields)
            .ThenInclude(field => field.Options)
            .Include(form => form.Versions)
            .ThenInclude(version => version.Rules);

    private IQueryable<RegistrationFormVersion> VersionGraph() =>
        dbContext.RegistrationFormVersions
            .Include(version => version.Sections)
            .ThenInclude(section => section.Fields)
            .ThenInclude(field => field.Options)
            .Include(version => version.Rules);
}
