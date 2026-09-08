using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.StorageObjects.Requests.Commands;
using Explore.Domain;

namespace Explore.Application.Features.StorageObjects.Authorization;

public sealed class CreateStorageUploadSessionAuthorizationContextEnricher(
    ICurrentUserService currentUserService,
    ITenantContext tenantContext,
    IOrganizationTenantRepository organizationTenantRepository)
    : IAuthorizationContextEnricher<CreateStorageUploadSessionCommand>
{
    public async Task<AuthorizationContext> ResolveAsync(
        CreateStorageUploadSessionCommand request,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId;
        var upload = request.UploadSessionDto;

        // Every early return yields no facts. The requested owner is caller input; only a loaded,
        // tenant-matched participation is evidence, and without it the provider must deny.
        if (currentUserService.UserId is not { } subjectUserId ||
            tenantId == Guid.Empty ||
            upload.OwningResourceId is not { } owningResourceId ||
            !string.Equals(upload.OwningResourceKind, StorageOwningResourceKinds.OrganizationTenant, StringComparison.Ordinal))
        {
            return new AuthorizationContext(nameof(CreateStorageUploadSessionCommand));
        }

        var participation = await organizationTenantRepository.GetById(owningResourceId);
        if (participation is null || participation.TenantId != tenantId)
        {
            return new AuthorizationContext(nameof(CreateStorageUploadSessionCommand));
        }

        return new AuthorizationContext(
            nameof(CreateStorageUploadSessionCommand),
            new StorageUploadIntentFacts(
                subjectUserId,
                participation.TenantId,
                StorageOwningResourceKinds.OrganizationTenant,
                participation.Id,
                participation.OrganizationId));
    }
}
