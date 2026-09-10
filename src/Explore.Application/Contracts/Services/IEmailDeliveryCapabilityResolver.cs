
using Explore.Application.Models;

namespace Explore.Application.Contracts.Services;

public interface IEmailDeliveryCapabilityResolver
{
    Task<EmailDeliveryCapability> ResolveAsync(Guid? tenantId, CancellationToken cancellationToken = default);
}
