using Explore.Blazor.Client.Components.Registration.FormRenderer;

namespace Explore.Blazor.Client.Contracts.Services;

public interface INativeRegistrationFormService
{
    Task<NativeRegistrationRequirementCollectionView?> GetRequirementsAsync(
        Guid eventId, Guid orderId, GuestRegistrationOrderCapability? guestCapability,
        CancellationToken cancellationToken = default);

    Task<NativeRegistrationAttemptView?> LaunchAsync(
        Guid eventId, Guid orderId, NativeRegistrationLaunchDescriptorView descriptor,
        GuestRegistrationOrderCapability? guestCapability, string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<NativeRegistrationActionResult> SubmitAsync(
        Guid eventId, Guid orderId, NativeRegistrationAttemptView attempt,
        RegistrationSubjectView subject, RegistrationFormSubmission submission,
        GuestRegistrationOrderCapability? guestCapability, string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<bool> SkipAsync(
        Guid eventId, Guid orderId, NativeRegistrationAttemptView attempt,
        GuestRegistrationOrderCapability? guestCapability, string idempotencyKey,
        CancellationToken cancellationToken = default);
}
