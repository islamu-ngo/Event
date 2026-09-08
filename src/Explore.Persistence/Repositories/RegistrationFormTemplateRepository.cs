using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class RegistrationFormTemplateRepository(ExploreDbContext dbContext) : IRegistrationFormTemplateRepository
{
    public async Task<IReadOnlyList<RegistrationFormTemplate>> ListAsync(CancellationToken cancellationToken) =>
        await VisibleTemplates()
            .AsNoTracking()
            .OrderBy(template => template.Category)
            .ThenBy(template => template.Name)
            .ToListAsync(cancellationToken);

    public Task<RegistrationFormTemplate?> GetAsync(Guid templateId, CancellationToken cancellationToken) =>
        VisibleTemplates().AsNoTracking().FirstOrDefaultAsync(template => template.Id == templateId, cancellationToken);

    public Task<RegistrationFormTemplate?> GetForUpdateAsync(Guid templateId, CancellationToken cancellationToken) =>
        VisibleTemplates().FirstOrDefaultAsync(template => template.Id == templateId, cancellationToken);

    public async Task CreateAsync(RegistrationFormTemplate template, CancellationToken cancellationToken)
    {
        await dbContext.RegistrationFormTemplates.AddAsync(template, cancellationToken);
        await SaveChangesAsync(cancellationToken);
    }

    private IQueryable<RegistrationFormTemplate> VisibleTemplates() =>
        dbContext.RegistrationFormTemplates.Where(template =>
            !template.IsDeleted &&
            (template.TenantId == null || template.TenantId == dbContext.TenantFilterTenantId));

    private async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConcurrencyConflictException(
                ConcurrencyConflictException.ConcurrentUpdate,
                "Registration form template data was modified by another request. Reload and retry.",
                innerException: exception);
        }
    }
}
