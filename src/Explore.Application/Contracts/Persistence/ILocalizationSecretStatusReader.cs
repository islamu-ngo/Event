namespace Explore.Application.Contracts.Persistence;

/// <summary>Reports binding existence for the fixed TMS key in the selected tenant or instance.</summary>
public interface ILocalizationSecretStatusReader
{
    Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default);
}
