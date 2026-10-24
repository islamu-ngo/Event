using Explore.Application.Services.Moderation;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Authenticates an assertion against independently acquired public trust; supplies no current permission or passing verdict.</summary>
public interface IModerationMeasurementReceiptVerifier
{
    ModerationMeasurementVerificationResult Verify(string? token, ModerationMeasurementTrustKey? trust,
        ModerationActivationBinding? expectedBinding, DateTimeOffset observedAt);
}
