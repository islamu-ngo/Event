using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace Explore.Infrastructure.Tests.Storage;

[Category("Runtime")]
[NotInParallel("DockerCompose")]
public sealed class MinioSampleBucketPostureTests
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(2);

    [Test]
    public async Task SampleBucketInitializerRestoresPrivatePostureForExistingBucket()
    {
        string accessKey = RequiredEnvironmentVariable("STORAGE_S3_ACCESS_KEY_ID");
        string secretKey = RequiredEnvironmentVariable("STORAGE_S3_SECRET_ACCESS_KEY");
        string repositoryRoot = FindRepositoryRoot();
        string composePath = Path.Combine(repositoryRoot, "docker-compose.yml");
        string projectName = $"event-storage-posture-{Guid.NewGuid():N}";
        string temporaryDirectory = Path.Combine(Path.GetTempPath(), projectName);
        Directory.CreateDirectory(temporaryDirectory);
        string overridePath = Path.Combine(temporaryDirectory, "docker-compose.override.yml");
        await File.WriteAllTextAsync(overridePath, """
            services:
              minio:
                image: minio/minio:latest
                pull_policy: never
              minio-init:
                pull_policy: never
            """);

        var environment = new Dictionary<string, string?>
        {
            ["STORAGE_S3_ACCESS_KEY_ID"] = accessKey,
            ["STORAGE_S3_SECRET_ACCESS_KEY"] = secretKey,
            ["STORAGE_S3_BUCKET_NAME"] = null,
            ["MINIO_API_PORT"] = "0",
            ["MINIO_CONSOLE_PORT"] = "0",
            ["SECRET_PROVIDER"] = "Environment",
        };

        string[] compose =
        [
            "compose",
            "--project-name", projectName,
            "--profile", "storage",
            "-f", composePath,
            "-f", overridePath,
        ];

        try
        {
            CommandResult rendered = await RunAsync(
                "docker",
                [.. compose, "config", "--no-interpolate", "--format", "json"],
                repositoryRoot,
                environment);
            using JsonDocument machineConfig = JsonDocument.Parse(rendered.StandardOutput);
            JsonElement services = machineConfig.RootElement.GetProperty("services");
            await Assert.That(services.TryGetProperty("minio", out _)).IsTrue();
            await Assert.That(services.TryGetProperty("minio-init", out _)).IsTrue();

            await RunAsync(
                "docker",
                [.. compose, "up", "--detach", "--wait", "minio"],
                repositoryRoot,
                environment);

            var published = await RunAsync("docker", [.. compose, "port", "minio", "9000"],
                repositoryRoot, environment);
            string address = published.StandardOutput.Trim();
            int apiPort = int.Parse(address[(address.LastIndexOf(':') + 1)..],
                System.Globalization.CultureInfo.InvariantCulture);

            await RunAsync(
                "docker",
                [.. compose, "run", "--rm", "-e", "STORAGE_S3_ACCESS_KEY_ID", "-e", "STORAGE_S3_SECRET_ACCESS_KEY",
                    "--entrypoint", "/bin/sh", "minio-init", "-c",
                    "mc alias set local http://minio:9000 \"$STORAGE_S3_ACCESS_KEY_ID\" \"$STORAGE_S3_SECRET_ACCESS_KEY\" >/dev/null && " +
                    "mc mb --ignore-existing local/explore >/dev/null && " +
                    "printf public-check | mc pipe local/explore/public-check.txt >/dev/null && " +
                    "mc anonymous set public local/explore >/dev/null"],
                repositoryRoot,
                environment);

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            using HttpResponseMessage publicResponse = await http.GetAsync(
                $"http://127.0.0.1:{apiPort}/explore/public-check.txt");
            await Assert.That(publicResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

            await RunAsync(
                "docker",
                [.. compose, "run", "--rm", "minio-init"],
                repositoryRoot,
                environment);

            using HttpResponseMessage anonymousResponse = await http.GetAsync(
                $"http://127.0.0.1:{apiPort}/explore/public-check.txt");
            await Assert.That(anonymousResponse.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);

            await RunAsync(
                "docker",
                [.. compose, "run", "--rm", "-e", "STORAGE_S3_ACCESS_KEY_ID", "-e", "STORAGE_S3_SECRET_ACCESS_KEY",
                    "--entrypoint", "/bin/sh", "minio-init", "-c",
                    "mc alias set local http://minio:9000 \"$STORAGE_S3_ACCESS_KEY_ID\" \"$STORAGE_S3_SECRET_ACCESS_KEY\" >/dev/null && " +
                    "mc stat local/explore/public-check.txt >/dev/null"],
                repositoryRoot,
                environment);
        }
        finally
        {
            try
            {
                await RunAsync(
                    "docker",
                    [.. compose, "down", "--volumes", "--remove-orphans"],
                    repositoryRoot,
                    environment);
            }
            finally
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }
    }

    private static async Task<CommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string?> environment)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);
        foreach ((string key, string? value) in environment)
        {
            if (value is null)
                startInfo.Environment.Remove(key);
            else
                startInfo.Environment[key] = value;
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start {fileName}.");
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(CommandTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            throw;
        }
        string output = await standardOutput;
        string error = await standardError;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{fileName} exited {process.ExitCode}: {error}");
        return new CommandResult(output, error);
    }

    private static string RequiredEnvironmentVariable(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"{name} must be injected into the test environment.");

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "docker-compose.yml")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private sealed record CommandResult(string StandardOutput, string StandardError);
}
