using System.Reflection;

namespace Explore.Blazor.Client.Contracts.Providers;

public interface ILazyAssemblyLoader
{
    Task<List<Assembly>> LoadAssembliesAsync(params string[] assemblyNames);
}
