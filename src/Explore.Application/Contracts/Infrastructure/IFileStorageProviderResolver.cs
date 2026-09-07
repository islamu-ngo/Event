namespace Explore.Application.Contracts.Infrastructure;

public interface IFileStorageProviderResolver
{
    IFileStorageProvider GetRequired(string provider);
}
