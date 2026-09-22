using System.Text;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Models.Storage;
using Explore.Infrastructure;
using Explore.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Event.Standalone.IntegrationTests;

public sealed class StandaloneResourceDurabilityTests
{
    [Test]
    public async Task DefaultStorageRootSharesTheDurableStandaloneDataVolume()
    {
        using var services = ComposeStorage();
        var options = services.GetRequiredService<IOptions<LocalFileStorageOptions>>().Value;

        await Assert.That(options.RootPath).IsEqualTo("/app/data/storage");
    }

    [Test]
    public async Task ExplicitDurableRootPreservesBytesAcrossProviderReconstruction()
    {
        var root = Path.Combine(Path.GetTempPath(), $"standalone-resources-{Guid.CreateVersion7():N}");
        Directory.CreateDirectory(root);

        try
        {
            FileStorageWriteResult stored;
            using (var services = ComposeStorage(root))
            {
                var provider = services.GetRequiredService<IFileStorageProvider>();
                await using var content = new MemoryStream(Encoding.UTF8.GetBytes("durable material"));
                stored = await provider.WriteAsync(
                    new FileStorageWriteInput(
                        Guid.CreateVersion7(),
                        content,
                        "text/plain",
                        "material.txt",
                        ".txt",
                        ExpectedSizeBytes: 16,
                        MaxSizeBytes: 16),
                    CancellationToken.None);
            }

            using var recreatedServices = ComposeStorage(root);
            var recreatedProvider = recreatedServices.GetRequiredService<IFileStorageProvider>();
            var restored = await recreatedProvider.OpenReadAsync(
                new FileStorageReadInput(stored.ObjectKey, stored.ContentType),
                CancellationToken.None);
            await using var restoredContent = restored.Content;
            using var reader = new StreamReader(restoredContent);

            await Assert.That(await reader.ReadToEndAsync()).IsEqualTo("durable material");
            await Assert.That(recreatedServices.GetRequiredService<IOptions<LocalFileStorageOptions>>()
                .Value.RootPath).IsEqualTo(root);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static ServiceProvider ComposeStorage(string? rootOverride = null)
    {
        var configurationBuilder = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(FindRepositoryRoot(), "src", "Event.Standalone", "appsettings.json"));
        if (rootOverride is not null)
        {
            configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:Local:RootPath"] = rootOverride,
            });
        }

        IServiceCollection services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureInfrastructureServices(configurationBuilder.Build());

        // Select the production local registration without constructing optional S3 dependencies.
        var localRegistration = services.Single(descriptor =>
            descriptor.ServiceType == typeof(IFileStorageProvider)
            && descriptor.ImplementationType == typeof(LocalFileStorageProvider));
        services.Add(localRegistration);
        return services.BuildServiceProvider();
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "Event.Standalone", "Dockerfile")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
