using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Explore.Secrets.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Explore.Secrets.UnitTests.Database;

public sealed class DeploymentCredentialConfigurationTests
{
    [Test]
    [Arguments(null)]
    [Arguments("")]
    public async Task RuntimeNeverBorrowsSharedOrMigratorCredentials(string? selectedPassword)
    {
        string generated = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "PostgreSql",
            ["Database:Host"] = "database.example.test",
            ["Database:Database"] = "event",
            ["Database:Schema"] = "event",
            ["Database:Runtime:Username"] = "event_runtime",
            ["Database:Runtime:Password"] = selectedPassword,
            ["Database:Username"] = "shared",
            ["Database:Password"] = generated,
            ["Database:Migrator:Username"] = "event_migrator",
            ["Database:Migrator:Password"] = generated
        }).Build();
        await Assert.That(() => PrimaryDatabaseConfiguration.BindRuntime(configuration))
            .Throws<OptionsValidationException>();
    }

    [Test]
    public async Task ExplicitEmptyRoleValueCannotFallBackToItsEnvironmentAlias()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "PostgreSql",
            ["Database:Host"] = "database.example.test",
            ["Database:Database"] = "event",
            ["Database:Schema"] = "event",
            ["Database:Runtime:Username"] = "event_runtime",
            ["Database:Runtime:Password"] = "",
            ["DATABASE_RUNTIME_PASSWORD"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(32))
        }).Build();
        await Assert.That(() => PrimaryDatabaseConfiguration.BindRuntime(configuration))
            .Throws<OptionsValidationException>();
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("explore")]
    public async Task RenderedOmittedEmptyAndDefaultRuntimePasswordCannotBind(string? password)
    {
        using var rendered = await Render(password);
        var environment = rendered.RootElement.GetProperty("services").GetProperty("islamu-event-api").GetProperty("environment");
        var configuration = Configuration(environment);
        await Assert.That(() => PrimaryDatabaseConfiguration.BindRuntime(configuration))
            .Throws<OptionsValidationException>();
    }

    [Test]
    public async Task RenderedRuntimeAndMigratorReceiveOnlyTheirOwnCredentials()
    {
        string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        using var rendered = await Render(password);
        var services = rendered.RootElement.GetProperty("services");
        var runtime = services.GetProperty("islamu-event-api").GetProperty("environment");
        var migrator = services.GetProperty("event-migrationservice").GetProperty("environment");
        var runtimeOptions = PrimaryDatabaseConfiguration.BindRuntime(Configuration(runtime));
        var migratorOptions = PrimaryDatabaseConfiguration.BindMigrator(Configuration(migrator));
        await Assert.That(services.GetProperty("islamu-event-api").GetProperty("ports")
            .EnumerateArray().All(port => port.GetProperty("host_ip").GetString() == "127.0.0.1")).IsTrue();
        await Assert.That(runtime.GetProperty("EffectiveHostAuthority__InstanceHosts__0").GetString())
            .IsEqualTo("islamu-event-api");
        await Assert.That(runtimeOptions.Password == password).IsTrue();
        await Assert.That(runtimeOptions.Username == migratorOptions.Username).IsFalse();
        await Assert.That(runtimeOptions.Password == migratorOptions.Password).IsFalse();
        await Assert.That(runtime.TryGetProperty("Database__Migrator__Password", out _)).IsFalse();
        await Assert.That(migrator.TryGetProperty("Database__Runtime__Password", out _)).IsFalse();
    }

    private static IConfiguration Configuration(JsonElement environment) =>
        new ConfigurationBuilder().AddInMemoryCollection(environment.EnumerateObject()
            .Select(property => new KeyValuePair<string, string?>(
                property.Name.Replace("__", ":", StringComparison.Ordinal), property.Value.GetString()))).Build();

    private static async Task<JsonDocument> Render(string? runtimePassword)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "docker-compose.yml")))
            directory = directory.Parent;
        if (directory is null) throw new InvalidOperationException("compose_source_not_found");
        var start = new ProcessStartInfo("docker")
        {
            WorkingDirectory = directory.FullName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[] { "compose", "--env-file", "/dev/null", "-f", "docker-compose.yml",
                     "--profile", "*", "config", "--format", "json" })
            start.ArgumentList.Add(argument);
        // No ambient deployment credential or .env file may enter the test renderer.
        string? path = System.Environment.GetEnvironmentVariable("PATH");
        start.Environment.Clear();
        if (path is not null) start.Environment["PATH"] = path;
        start.Environment["SECRET_PROVIDER"] = "Environment";
        start.Environment["DATABASE_PROVIDER"] = "PostgreSql";
        start.Environment["DATABASE_RUNTIME_USERNAME"] = "event_runtime";
        start.Environment["DATABASE_MIGRATOR_USERNAME"] = "event_migrator";
        start.Environment["DATABASE_MIGRATOR_PASSWORD"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        start.Environment["DATABASE_GLOBAL_USERNAME"] = "event_global";
        start.Environment["DATABASE_GLOBAL_PASSWORD"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        if (runtimePassword is not null) start.Environment["DATABASE_RUNTIME_PASSWORD"] = runtimePassword;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("compose_renderer_not_started");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var stdout = process.StandardOutput.ReadToEndAsync(deadline.Token);
        var stderr = process.StandardError.ReadToEndAsync(deadline.Token);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            _ = await stderr;
            if (process.ExitCode != 0) throw new InvalidOperationException("compose_render_failed");
            return JsonDocument.Parse(await stdout);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }
}
