using Explore.Domain;
using Explore.Domain.Settings.Documents;

namespace Explore.Application.Contracts.Services;

public sealed record TenantBrandingDocumentSeed(
    Guid DocumentId,
    int SchemaVersion,
    string DefaultsVersion,
    string PayloadJson);

public sealed record TenantDirectoryOperatorIdentityDocumentSeed(
    Guid DocumentId,
    int SchemaVersion,
    string DefaultsVersion,
    string PayloadJson);

public sealed record TenantCreationRequest(
    Guid TenantId,
    string FullName,
    string Slug,
    int TenantStatusId,
    Guid? ActorUserId,
    DateTime OccurredAt,
    TenantBrandingDocumentSeed Branding,
    TenantDirectoryOperatorIdentityDocumentSeed DirectoryOperatorIdentity);

public sealed record TenantCreationOutcome(
    Tenant Tenant,
    TenantSettingsDocument BrandingDocument,
    TenantSettingsDocument DirectoryOperatorIdentityDocument);

public interface ITenantCreationService
{
    Task<TenantCreationOutcome> CreateInCurrentTransactionAsync(
        TenantCreationRequest request,
        CancellationToken cancellationToken);
}
