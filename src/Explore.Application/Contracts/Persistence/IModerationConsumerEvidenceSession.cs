using Explore.Application.Services.Moderation;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Borrows the caller's original native acquisition and retained transaction fences.
/// Disposing ends the borrow, not the transaction. Observations supply no conformance verdict.
/// </summary>
public interface IModerationConsumerEvidenceSession : IDisposable
{
    Guid TransactionId { get; }

    Task<ModerationMeasurementDatabaseObservation?> AcquirePrimaryDatabaseAsync(
        DateTimeOffset observedAt, CancellationToken cancellationToken);

    Task<ModerationCapabilitySourceObservation?> AcquireInstallationAsync(
        ModerationActivationBinding binding, string consumerId, DateTimeOffset observedAt,
        CancellationToken cancellationToken);

    Task<ModerationMeasurementReceiptObservation?> AcquireMeasurementAsync(
        ModerationActivationBinding binding, string consumerId, DateTimeOffset observedAt,
        CancellationToken cancellationToken);
}

/// <summary>Facts from the actual composed primary context and its open native connection, not a passing verdict.</summary>
public sealed record ModerationMeasurementDatabaseObservation(
    Guid TransactionId, string ConfigurationDigest, string ProviderCode, string ProviderVersion,
    DateTimeOffset ObservedAt);
