using System.Reflection;
using Explore.Blazor.Client.Contracts.Providers;
using Microsoft.Extensions.Logging;

namespace Explore.Blazor.Client.Services;

/// <summary>
/// Default implementation of lazy assembly loading for Blazor WASM.
/// Uses the built-in Blazor lazy loading mechanism via the Router component's AdditionalAssemblies parameter.
/// This service provides a convenient interface for loading assemblies on-demand.
/// </summary>
public class LazyAssemblyLoaderService(ILogger<LazyAssemblyLoaderService> logger) : ILazyAssemblyLoader
{
    private readonly ILogger<LazyAssemblyLoaderService> _logger = logger;

    /// <summary>
    /// Loads assemblies dynamically at runtime.
    /// In Blazor WASM, assemblies marked with BlazorWebAssemblyLazyLoad in the .csproj
    /// are automatically loaded when referenced in the Router's AdditionalAssemblies parameter.
    /// </summary>
    /// <param name="assemblyNames">Names of assemblies to load (e.g., "Explore.Blazor.Client.Pages.Admin.dll")</param>
    /// <returns>List of loaded assemblies</returns>
    public async Task<List<Assembly>> LoadAssembliesAsync(params string[] assemblyNames)
    {
        var loadedAssemblies = new List<Assembly>();

        foreach (var assemblyName in assemblyNames)
        {
            try
            {
                // Blazor WASM loads lazy assemblies via the Router's AdditionalAssemblies parameter;
                // this method resolves already-loaded ones from the AppDomain and logs the rest.
                var assembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == assemblyName.Replace(".dll", ""));

                if (assembly != null)
                {
                    loadedAssemblies.Add(assembly);
                }
                else
                {
                    _logger.LogDebug("Assembly {AssemblyName} will be loaded by the Router when needed.", assemblyName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load assembly {AssemblyName}.", assemblyName);
            }
        }

        return await Task.FromResult(loadedAssemblies);
    }
}
