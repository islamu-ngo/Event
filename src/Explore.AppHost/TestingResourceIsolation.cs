using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;

namespace Explore.AppHost;

public sealed record TestingResourceIsolation(string RunId, string Root)
{
    public const string MarkerName = ".islamu-test-owner";

    public static void ConfigureOwnedInputs(ConfigurationManager configuration, string[] arguments)
    {
        using var explicitInputs = new ConfigurationManager();
        explicitInputs.AddCommandLine(arguments);
        string? runId = explicitInputs["ISLAMU_TEST_RUN_ID"];
        var authority = configuration.Sources.OfType<MemoryConfigurationSource>()
            .SingleOrDefault(source => source.InitialData?.Any(pair =>
                pair.Key == "TestingEnvironment:ISLAMU_TEST_RUN_ID" && pair.Value == runId) == true)
            ?? throw new InvalidOperationException("test_resource_authority_missing");
        var ownedInputs = authority.InitialData!.ToArray();
        string[] technicalKeys = ["AppHost:Directory", "AppHost:Path", "AppHost:Sha256",
            "DcpPublisher:CliPath", "DcpPublisher:RandomizePorts"];
        var technicalInputs = technicalKeys.Select(key => new KeyValuePair<string, string?>(key, configuration[key])).ToArray();
        configuration.Sources.Clear();
        configuration.AddInMemoryCollection(technicalInputs)
            .AddInMemoryCollection(ownedInputs).AddCommandLine(arguments);
    }

    public static TestingResourceIsolation? Admit(IConfiguration configuration)
    {
        string? runId = configuration["ISLAMU_TEST_RUN_ID"];
        string? root = configuration["ISLAMU_TEST_ROOT"];
        if (runId is null && root is null)
            return null;
        if (!Guid.TryParseExact(runId, "N", out _) || root is null
            || !Path.IsPathFullyQualified(root) || Path.GetFullPath(root) != root
            || Path.GetFileName(root) != runId)
            throw new InvalidOperationException("test_resource_owner_invalid");
        ValidateProfile(configuration);
        string marker = Path.Combine(root, MarkerName);
        var owner = new TestingResourceIsolation(runId, root);
        owner.VerifyOwnership();
        if (Directory.EnumerateFileSystemEntries(root).Any(path => path != marker))
            throw new InvalidOperationException("test_resource_root_not_empty");
        return owner;
    }

    public void VerifyOwnership()
    {
        EnsureNoLinks(Root);
        string marker = Path.Combine(Root, MarkerName);
        if (!Directory.Exists(Root) || !File.Exists(marker)
            || new FileInfo(marker).LinkTarget is not null || File.ReadAllText(marker) != RunId)
            throw new InvalidOperationException("test_resource_marker_invalid");
    }

    public static void ValidateProfile(IConfiguration configuration)
    {
        if (configuration["DOTNET_ENVIRONMENT"] != "Assurance"
            || configuration["ASPNETCORE_ENVIRONMENT"] is not (null or "Assurance")
            || configuration["ISLAMU_ASPIRE_MODE"] != "local-default"
            || configuration["Hosting:Topology"] is not ("Split" or "Standalone")
            || configuration["SECRET_PROVIDER"] != "Environment"
            || configuration["SecretProvider:Provider"] is not (null or "Environment")
            || configuration["AGENT_BROWSER_SEED_ENABLED"] is not (null or "false")
            || configuration["CONFIGURATION_MANIFEST_MODE"] is not (null or "Off")
            || configuration["PrivacyErasure:Authority:Topology"] is not (null or "EmbeddedSqlite")
            || configuration["ERASURE_DATABASE_TOPOLOGY"] is not (null or "EmbeddedSqlite")
            || configuration["IDENTITY_DATABASE_TOPOLOGY"] is not (null or "CoLocated")
            || configuration["WEBHOOKS_PROVIDER"] is not (null or "Local"))
            throw new InvalidOperationException("test_resource_profile_invalid");
    }

    public string VolumePrefix => $"islamu-test-{RunId}-";

    public FileStream Claim() => new(Path.Combine(Root, ".apphost-claim"), FileMode.CreateNew,
        FileAccess.Write, FileShare.None);

    public void Apply(IDistributedApplicationBuilder builder)
    {
        foreach (var resource in builder.Resources)
        {
            foreach (var endpoint in resource.Annotations.OfType<EndpointAnnotation>())
                endpoint.Port = null;
            if (resource is ContainerResource container)
            {
                builder.CreateResourceBuilder(container).WithContainerRuntimeArgs("--pids-limit", "512",
                    "--label", $"islamu.assurance.owner={RunId}",
                    "--label", $"islamu.assurance.resource={container.Name}");
                foreach (var mount in container.Annotations.OfType<ContainerMountAnnotation>().ToArray())
                {
                    if (mount.Type == ContainerMountType.Volume)
                    {
                        container.Annotations.Remove(mount);
                        builder.CreateResourceBuilder(container).WithVolume(
                            VolumePrefix + mount.Source, mount.Target, mount.IsReadOnly);
                    }
                    else if (!mount.IsReadOnly)
                        throw new InvalidOperationException("test_resource_foreign_writable_mount");
                }
            }
            if (resource is ProjectResource project)
            {
                var projected = builder.CreateResourceBuilder(project)
                    .WithEnvironment("DOTNET_ENVIRONMENT", "Assurance")
                    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Assurance");
                if (project.Name != "event-migrationservice")
                {
                    projected.WithHttpEndpoint(name: "operations")
                        .WithEnvironment("OperationalEndpoints__Address", "127.0.0.1")
                        .WithEnvironment("OperationalEndpoints__Port",
                            projected.GetEndpoint("operations").Property(EndpointProperty.TargetPort))
                        .WithHttpHealthCheck("/health", endpointName: "operations");
                }
            }
        }
    }

    private static void EnsureNoLinks(string path)
    {
        for (DirectoryInfo? directory = new(path); directory is not null; directory = directory.Parent)
            if (directory.LinkTarget is not null)
                throw new InvalidOperationException("test_resource_symbolic_path");
    }
}
