using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Observes detached stored instance settings under independent read authority, without hierarchy defaults or mutation custody.</summary>
public interface IInstanceSystemSettingsReader
{
    Task<IReadOnlyList<SystemSetting>> ReadAsync(
        IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default);
}
