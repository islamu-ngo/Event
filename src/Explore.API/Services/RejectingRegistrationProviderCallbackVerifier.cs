using Explore.Application.Contracts.Services.Registration;

namespace Explore.API.Services;

public sealed class RejectingRegistrationProviderCallbackVerifier : IRegistrationProviderCallbackVerifier
{
    public Task<RegistrationProviderCallbackVerificationResult> VerifyCallbackAsync(
        RegistrationProviderCallbackVerificationRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new RegistrationProviderCallbackVerificationResult(false, "registration_callback_verifier_not_configured"));
}
