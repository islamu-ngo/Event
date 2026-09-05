// ABOUTME: Resolves safe outbound email capability for an explicit instance or tenant scope.
// ABOUTME: Instance-owned authentication callers pass null regardless of their tenant route.

using Explore.Application.Models;

namespace Explore.Application.Contracts.Services;

public interface IEmailDeliveryCapabilityResolver
{
    Task<EmailDeliveryCapability> ResolveAsync(Guid? tenantId, CancellationToken cancellationToken = default);
}
