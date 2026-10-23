namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Reads fixed cross-tenant operational aggregates without acquiring email content.
/// Tenant backlog samples are restricted to between one and one hundred entries.
/// </summary>
public interface IEmailDispatchHealthSnapshotReader
{
    Task<EmailDispatchHealthSnapshot> ReadAsync(
        DateTime now,
        DateTime processingStartedBefore,
        int tenantSampleLimit,
        CancellationToken cancellationToken);
}
