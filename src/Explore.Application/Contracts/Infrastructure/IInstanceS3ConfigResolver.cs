namespace Explore.Application.Contracts.Infrastructure;

/// <summary>Checks instance S3 governance and credentials independently of the requesting tenant.</summary>
public interface IInstanceS3ConfigResolver
{
    Task<bool> IsInstanceConfiguredAsync(CancellationToken cancellationToken = default);
}
