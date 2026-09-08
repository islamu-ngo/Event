namespace Explore.Application.Contracts.Infrastructure;

public interface IStaticTranslationBundleReader
{
    Task<IReadOnlyDictionary<string, string>> ReadBundleAsync(
        string languageCode,
        CancellationToken cancellationToken = default);
}
