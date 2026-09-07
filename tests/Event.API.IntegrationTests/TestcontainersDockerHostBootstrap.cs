using System.Runtime.CompilerServices;

namespace Event.Api.IntegrationTests;

internal static class TestcontainersDockerHostBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOCKER_HOST")))
        {
            return;
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile))
        {
            return;
        }

        var dockerDesktopSocket = Path.Combine(userProfile, ".docker", "desktop", "docker.sock");
        if (File.Exists(dockerDesktopSocket))
        {
            Environment.SetEnvironmentVariable("DOCKER_HOST", $"unix://{dockerDesktopSocket}");
            return;
        }

        var runtimeDirectory = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrWhiteSpace(runtimeDirectory))
        {
            var podmanSocket = Path.Combine(runtimeDirectory, "podman", "podman.sock");
            if (File.Exists(podmanSocket))
            {
                Environment.SetEnvironmentVariable("DOCKER_HOST", $"unix://{podmanSocket}");
            }
        }
    }
}
