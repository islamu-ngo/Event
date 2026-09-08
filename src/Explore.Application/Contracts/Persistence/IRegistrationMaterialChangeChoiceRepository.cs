using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IRegistrationMaterialChangeChoiceRepository
{
    Task<IReadOnlyList<RegistrationMaterialChangeChoice>> GetByPaymentAsync(
        Guid tenantId,
        Guid paymentAttemptId,
        CancellationToken cancellationToken);

    Task<RegistrationMaterialChangeChoice?> GetAsync(
        Guid tenantId,
        Guid campaignId,
        Guid registrationOrderId,
        CancellationToken cancellationToken);

    Task<bool> AcceptAsync(
        Guid tenantId,
        Guid choiceId,
        Guid actorId,
        DateTime decidedAt,
        CancellationToken cancellationToken);
}
