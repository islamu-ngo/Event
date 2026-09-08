using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IRegistrationFormTemplateRepository
{
    Task<IReadOnlyList<RegistrationFormTemplate>> ListAsync(CancellationToken cancellationToken);
    Task<RegistrationFormTemplate?> GetAsync(Guid templateId, CancellationToken cancellationToken);
    Task<RegistrationFormTemplate?> GetForUpdateAsync(Guid templateId, CancellationToken cancellationToken);
    Task CreateAsync(RegistrationFormTemplate template, CancellationToken cancellationToken);
}
