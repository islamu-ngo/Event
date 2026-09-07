namespace Explore.Application.Settings;

public interface IPublicationPolicyMutationBoundary
{
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
