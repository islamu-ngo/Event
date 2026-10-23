namespace Explore.Application.Settings;

using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.Settings;

public interface IPublicationPolicyMutationBoundary
{
    Task<PublicationPolicyMutationResult> ApplyTypedInCurrentTransactionAsync(
        TypedSettingsPublication publication, CancellationToken cancellationToken = default);

    Task<PublicationPolicyMutationResult> ApplyImportInCurrentTransactionAsync(
        HumanConfigurationImportPublication publication,
        CancellationToken cancellationToken = default);

    Task<PublicationPolicyMutationResult> ApplyTenantAsync(
        PublicationPolicyTenantMutationRequest request,
        CancellationToken cancellationToken = default);

    Task<PublicationPolicyMutationResult> ApplyTenantInCurrentTransactionAsync(
        PublicationPolicyTenantMutationRequest request,
        CancellationToken cancellationToken = default);

    Task<PublicationPolicyMutationResult> ApplyInstanceAsync(
        PublicationPolicyInstanceMutationRequest request,
        CancellationToken cancellationToken = default);

    Task<PublicationPolicyMutationResult> ApplyInstanceInCurrentTransactionAsync(
        PublicationPolicyInstanceMutationRequest request,
        CancellationToken cancellationToken = default);
}
