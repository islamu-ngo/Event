using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using Event.Testing.DataProtection;
using Explore.ServiceDefaults.DataProtection;
using Explore.ServiceDefaults.Secrets;
using Explore.Blazor.Configuration;
using Explore.Blazor.Extensions;
using Explore.Blazor.HealthChecks;
using Explore.Blazor.Hosting;
using Explore.Blazor.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Yarp.ReverseProxy.Configuration;

namespace Explore.Blazor.IntegrationTests.Hosting;

public sealed class BlazorHostCompositionTests
{
    [Test]
    public async Task ProductionCompositionRequiresCompletedSecretPreparation()
    {
        var builder = CreateBuilder();
        builder.Environment.EnvironmentName = "Production";
        builder.Configuration["SecretProvider:Provider"] = "Environment";
        builder.Services.AddSingleton(new AuthenticationCredentialAcquisitionReceipts());
        Action compose = () => builder.AddBlazorHostServices(
            BlazorHostProfile.Split, new GracefulShutdownState());
        var failure = await Assert.That(compose).Throws<InvalidOperationException>();
        await Assert.That(failure!.Message).IsEqualTo("bff_secret_preparation_required");
    }

    [Test]
    public async Task FrontendPreparationRejectsAnApiOwnerBeforeReading()
    {
        var builder = CreateBuilder();
        using var authority = (ConfigurationRoot)new ConfigurationBuilder().Build();
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.Api), authority);
        Func<Task> prepare = () => builder.PrepareBlazorHostSecretsAsync(
            BlazorHostProfile.Split, owner).AsTask();
        await Assert.That(prepare).Throws<InvalidOperationException>();
        await Assert.That(builder.Services).DoesNotContain(descriptor =>
            descriptor.ServiceType == typeof(AuthenticationCredentialAcquisitionReceipts));
    }

    [Test]
    public async Task SplitHost_ServesExistingCssStaticAsset()
    {
        var assembly = typeof(BlazorHostApplicationExtensions).Assembly;
        var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToArray();
        string[] requiredKeys =
        [
            "ISLAMU.Assurance.ProjectPath", "ISLAMU.Assurance.TargetPath", "ISLAMU.Assurance.TargetName",
            "ISLAMU.Assurance.RuntimeManifestFileName", "ISLAMU.Assurance.EndpointManifestFileName"
        ];
        await Assert.That(requiredKeys.All(key => metadata.Count(item => item.Key == key) == 1)).IsTrue()
            .Because("The owning executable must declare one compiled project, target and selected manifest pair.");
        string? project = metadata.Single(item => item.Key == requiredKeys[0]).Value;
        string? target = metadata.Single(item => item.Key == requiredKeys[1]).Value;
        string? name = metadata.Single(item => item.Key == requiredKeys[2]).Value;
        await Assert.That(project == "src/Explore.Blazor/Explore.Blazor.csproj" && CanonicalRelative(target)
            && target!.EndsWith("/" + Path.GetFileName(assembly.Location), StringComparison.Ordinal)
            && name == Path.GetFileNameWithoutExtension(assembly.Location)).IsTrue()
            .Because("Compiled provenance must identify the actual owning artifact without build-machine paths.");
        string[] owningManifestNames =
            [name + ".staticwebassets.runtime.json", name + ".staticwebassets.endpoints.json"];
        for (int index = 0; index < owningManifestNames.Length; index++)
        {
            string? fileName = metadata.Single(item => item.Key == requiredKeys[index + 3]).Value;
            await Assert.That(fileName == owningManifestNames[index] && !string.IsNullOrWhiteSpace(fileName)
                && fileName == Path.GetFileName(fileName)
                && !fileName.Any(char.IsControl)
                && File.Exists(Path.Combine(Path.GetDirectoryName(assembly.Location)!, fileName))).IsTrue()
                .Because("The selected pair must name real outputs of the owning build.");
        }

        await using var factory = new BlazorBffWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });

        using var response = await client.GetAsync("/css/layers.css");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("text/css");

        if (!OperatingSystem.IsLinux()) return;
        string temporary = Path.Combine(Path.GetTempPath(), "islamu-selected-assets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var failures = new List<Exception>();
        try
        {
            string output = Path.GetDirectoryName(assembly.Location)!;
            var runtimeTemplate = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(output, owningManifestNames[0])))!;
            var endpointTemplate = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(output, owningManifestNames[1])))!;
            string logicalAsset = "selected-proof.css";
            string[] sourceAssets = ["layers.css", "base.css"];
            string[] routes = ["assurance-selection-one.css", "assurance-selection-two.css"];
            byte[]? previousPayload = null;
            for (int index = 0; index < sourceAssets.Length; index++)
            {
                string owner = Guid.CreateVersion7().ToString("N");
                string root = Path.Combine(temporary, owner);
                Directory.CreateDirectory(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                await using (var marker = new FileStream(Path.Combine(root, ".islamu-test-owner"), new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.Read,
                    UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
                }))
                {
                    await marker.WriteAsync(System.Text.Encoding.UTF8.GetBytes(owner));
                }
                string hostRoot = Directory.CreateDirectory(Path.Combine(root, "host")).FullName;
                string webRoot = Directory.CreateDirectory(Path.Combine(hostRoot, "wwwroot")).FullName;
                string payloadRoot = Directory.CreateDirectory(Path.Combine(root, "payload")).FullName;
                var asset = runtimeTemplate["Root"]!["Children"]!["css"]!["Children"]![sourceAssets[index]]!["Asset"]!;
                string sourceRoot = runtimeTemplate["ContentRoots"]![asset["ContentRootIndex"]!.GetValue<int>()]!.GetValue<string>();
                byte[] payload = await File.ReadAllBytesAsync(Path.Combine(sourceRoot, asset["SubPath"]!.GetValue<string>()));
                await Assert.That(previousPayload is null || !previousPayload.SequenceEqual(payload)).IsTrue()
                    .Because("The two selected runtime providers must resolve distinct owned payloads.");
                previousPayload = payload;
                await File.WriteAllBytesAsync(Path.Combine(payloadRoot, logicalAsset), payload);
                var runtime = runtimeTemplate.DeepClone();
                runtime["ContentRoots"] = new JsonArray(JsonValue.Create(payloadRoot + Path.DirectorySeparatorChar));
                runtime["Root"] = new JsonObject
                {
                    ["Children"] = new JsonObject
                    {
                        [logicalAsset] = new JsonObject
                        {
                            ["Children"] = null,
                            ["Asset"] = new JsonObject { ["ContentRootIndex"] = 0, ["SubPath"] = logicalAsset },
                            ["Patterns"] = null
                        }
                    },
                    ["Asset"] = null,
                    ["Patterns"] = null
                };
                var endpoint = endpointTemplate["Endpoints"]!.AsArray().Single(item =>
                    item!["Route"]!.GetValue<string>() == "css/" + sourceAssets[index]
                    && item["AssetFile"]!.GetValue<string>() == "css/" + sourceAssets[index])!.DeepClone();
                endpoint!["Route"] = routes[index];
                endpoint["AssetFile"] = logicalAsset;
                var endpoints = endpointTemplate.DeepClone();
                endpoints["Endpoints"] = new JsonArray(endpoint);
                string runtimePath = Path.Combine(root, "selected.runtime.json");
                string endpointPath = Path.Combine(root, "selected.endpoints.json");
                await File.WriteAllTextAsync(runtimePath, runtime.ToJsonString());
                await File.WriteAllTextAsync(endpointPath, endpoints.ToJsonString());

                WebApplicationBuilder CreateSelectionBuilder()
                {
                    var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
                    {
                        ApplicationName = assembly.GetName().Name,
                        EnvironmentName = "Assurance",
                        ContentRootPath = hostRoot,
                        WebRootPath = webRoot
                    });
                    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ISLAMU_TEST_ROOT"] = root,
                        ["ISLAMU_TEST_RUN_ID"] = owner
                    });
                    builder.WebHost.UseTestServer();
                    return builder;
                }
                foreach (bool completeContext in new[] { false, true })
                {
                    var missingBuilder = CreateSelectionBuilder();
                    if (!completeContext) missingBuilder.Configuration["ISLAMU_TEST_RUN_ID"] = null;
                    await using var missingHost = missingBuilder.Build();
                    InvalidOperationException? missing = null;
                    try { missingHost.MapSelectedStaticAssets(); }
                    catch (InvalidOperationException failure) { missing = failure; }
                    await Assert.That(missing?.Message == "managed_served_assets_selection_rejected").IsTrue()
                        .Because("A private Assurance context must never silently map an omitted-path default.");
                }
                var buildFailureBuilder = CreateSelectionBuilder();
                var buildPrimary = new InvalidOperationException("selected_assets_build_control");
                buildFailureBuilder.Host.ConfigureContainer<IServiceCollection>((_, _) => throw buildPrimary);
                var borrowedBuildProvider = buildFailureBuilder.Environment.WebRootFileProvider;
                var buildSelection = await buildFailureBuilder.SelectAssuranceStaticAssetsAsync(runtimePath, endpointPath);
                var selectedBuildProvider = buildFailureBuilder.Environment.WebRootFileProvider;
                WebApplication? unexpectedHost = null;
                var buildFailures = new List<Exception>();
                try { unexpectedHost = buildFailureBuilder.Build(); }
                catch (Exception failure) { buildFailures.Add(failure); }
                bool leaseSurvivedBuild = false;
                try
                {
                    leaseSurvivedBuild = ReferenceEquals(selectedBuildProvider, buildFailureBuilder.Environment.WebRootFileProvider)
                        && selectedBuildProvider.GetFileInfo(logicalAsset).Exists;
                }
                catch (Exception failure) { buildFailures.Add(failure); }
                try { if (unexpectedHost is not null) await unexpectedHost.DisposeAsync(); }
                catch (Exception failure) { buildFailures.Add(failure); }
                try { buildSelection.Dispose(); }
                catch (Exception failure) { buildFailures.Add(failure); }
                await Assert.That(buildFailures.Count == 1 && ReferenceEquals(buildFailures[0], buildPrimary)
                    && leaseSurvivedBuild && ReferenceEquals(borrowedBuildProvider, buildFailureBuilder.Environment.WebRootFileProvider)).IsTrue()
                    .Because("A real Build failure must retain its primary while the Program-owned lease survives and then releases.");

                var selectedBuilder = CreateSelectionBuilder();
                var originalProvider = selectedBuilder.Environment.WebRootFileProvider;
                string malformedRuntime = Path.Combine(root, "malformed.runtime.json");
                await File.WriteAllTextAsync(malformedRuntime, "{");
                InvalidOperationException? registrationFailure = null;
                try
                {
                    using var unexpected = await selectedBuilder.SelectAssuranceStaticAssetsAsync(malformedRuntime, endpointPath);
                }
                catch (InvalidOperationException failure) { registrationFailure = failure; }
                await Assert.That(registrationFailure?.Message == "managed_served_assets_selection_rejected"
                    && ReferenceEquals(originalProvider, selectedBuilder.Environment.WebRootFileProvider)).IsTrue()
                    .Because("Failed pre-Build selection must release its partial state and preserve the borrowed provider.");
                selectedBuilder.AddServiceDefaults();
                var primaryControl = new InvalidOperationException("selected_assets_body_control");
                var cleanupControl = new InvalidOperationException("selected_assets_cleanup_control");
                bool leaseAliveDuringCleanup = false;
                if (index == 1)
                {
                    selectedBuilder.Services.AddSingleton<IDisposable>(_ =>
                    {
                        var resource = Substitute.For<IDisposable>();
                        resource.When(item => item.Dispose()).Do(_ =>
                        {
                            leaseAliveDuringCleanup = !ReferenceEquals(originalProvider, selectedBuilder.Environment.WebRootFileProvider)
                                && selectedBuilder.Environment.WebRootFileProvider.GetFileInfo(logicalAsset).Exists;
                            throw cleanupControl;
                        });
                        return resource;
                    });
                }
                Microsoft.Extensions.Hosting.Extensions.AssuranceStaticAssetSelection? selection = null;
                WebApplication? selectedHost = null;
                var selectedFailures = new List<Exception>();
                try
                {
                    selection = await selectedBuilder.SelectAssuranceStaticAssetsAsync(runtimePath, endpointPath);
                    selectedHost = selectedBuilder.Build();
                    if (index == 1) _ = selectedHost.Services.GetRequiredService<IDisposable>();
                    selectedHost.MapSelectedStaticAssets();
                    await selectedHost.StartAsync();
                    string recordPath = Directory.EnumerateFiles(root, "managed-host-root-*.json").Single();
                    await using (var record = new FileStream(recordPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        byte[] recordBytes = new byte[16385];
                        int count = await record.ReadAtLeastAsync(recordBytes, recordBytes.Length, throwOnEndOfStream: false);
                        await Assert.That(count <= 16384).IsTrue();
                        using var document = System.Text.Json.JsonDocument.Parse(recordBytes.AsMemory(0, count));
                        var served = document.RootElement.GetProperty("ServedAssets");
                        byte[] runtimeBytes = await File.ReadAllBytesAsync(runtimePath);
                        byte[] endpointBytes = await File.ReadAllBytesAsync(endpointPath);
                        await Assert.That(served.GetProperty("RuntimeManifestPath").GetString() == runtimePath
                            && served.GetProperty("EndpointManifestPath").GetString() == endpointPath
                            && served.GetProperty("RuntimeManifestSha256").GetString()
                                == Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(runtimeBytes))
                            && served.GetProperty("EndpointManifestSha256").GetString()
                                == Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(endpointBytes))).IsTrue()
                            .Because("The private host observation must describe the same selected inputs; HTTP separately proves their use.");
                    }
                    using var selectedClient = selectedHost.GetTestClient();
                    using var selectedResponse = await selectedClient.GetAsync("/" + routes[index]);
                    byte[] actual = await selectedResponse.Content.ReadAsByteArrayAsync();
                    await Assert.That(selectedResponse.StatusCode == HttpStatusCode.OK
                        && selectedResponse.Content.Headers.ContentType?.MediaType == "text/css"
                        && actual.SequenceEqual(payload)).IsTrue()
                        .Because("The explicitly selected runtime and endpoint manifests must serve the exact owned payload.");
                    using var otherRoute = await selectedClient.GetAsync("/" + routes[1 - index]);
                    await Assert.That(otherRoute.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
                    await File.AppendAllTextAsync(endpointPath, " ");
                    InvalidOperationException? changed = null;
                    try { selectedHost.MapSelectedStaticAssets(); }
                    catch (InvalidOperationException failure) { changed = failure; }
                    await Assert.That(changed?.Message == "managed_served_assets_selection_rejected").IsTrue()
                        .Because("Changed selected inputs cannot retain a successful selection observation.");
                    if (index == 1) throw primaryControl;
                }
                catch (Exception failure) { selectedFailures.Add(failure); }
                try
                {
                    if (selectedHost is not null)
                    {
                        selectedHost.Lifetime.StopApplication();
                        await selectedHost.WaitForShutdownAsync();
                    }
                }
                catch (Exception failure) { selectedFailures.Add(failure); }
                try { if (selectedHost is not null) await selectedHost.DisposeAsync(); }
                catch (Exception failure) { selectedFailures.Add(failure); }
                try { selection?.Dispose(); }
                catch (Exception failure) { selectedFailures.Add(failure); }
                if (index == 1)
                {
                    await Assert.That(selectedFailures.Count == 2 && ReferenceEquals(selectedFailures[0], primaryControl)
                        && ReferenceEquals(selectedFailures[1], cleanupControl)
                        && leaseAliveDuringCleanup
                        && ReferenceEquals(originalProvider, selectedBuilder.Environment.WebRootFileProvider)).IsTrue()
                        .Because("An actual application cleanup fault must retain the body primary and still release the Program-owned selection.");
                    selectedFailures.Clear();
                }
                if (selectedFailures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(selectedFailures[0]).Throw();
                if (selectedFailures.Count > 1) throw new AggregateException(selectedFailures);
                if (index == 1)
                {
                    var runBuilder = CreateSelectionBuilder();
                    var runBorrowedProvider = runBuilder.Environment.WebRootFileProvider;
                    var startupPrimary = new InvalidOperationException("selected_assets_run_startup_control");
                    var runCleanup = new InvalidOperationException("selected_assets_run_cleanup_control");
                    bool startupAttempted = false;
                    bool runLeaseAliveDuringCleanup = false;
                    var startup = Substitute.For<IHostedService>();
                    startup.StartAsync(Arg.Any<CancellationToken>()).Returns(_ =>
                    {
                        startupAttempted = true;
                        return Task.FromException(startupPrimary);
                    });
                    startup.StopAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
                    runBuilder.Services.AddSingleton(startup);
                    runBuilder.Services.AddSingleton<IDisposable>(_ =>
                    {
                        var resource = Substitute.For<IDisposable>();
                        resource.When(item => item.Dispose()).Do(_ =>
                        {
                            runLeaseAliveDuringCleanup = !ReferenceEquals(runBorrowedProvider, runBuilder.Environment.WebRootFileProvider)
                                && runBuilder.Environment.WebRootFileProvider.GetFileInfo(logicalAsset).Exists;
                            throw runCleanup;
                        });
                        return resource;
                    });
                    Microsoft.Extensions.Hosting.Extensions.AssuranceStaticAssetSelection? runSelection = null;
                    WebApplication? runHost = null;
                    var runFailures = new List<Exception>();
                    try
                    {
                        runSelection = await runBuilder.SelectAssuranceStaticAssetsAsync(runtimePath, endpointPath);
                        runHost = runBuilder.Build();
                        runHost.MapSelectedStaticAssets();
                        _ = runHost.Services.GetRequiredService<IDisposable>();
                        await runHost.StartAsync();
                        await runHost.WaitForShutdownAsync();
                    }
                    catch (Exception failure) { runFailures.Add(failure); }
                    try { if (runHost is not null) await runHost.StopAsync(); }
                    catch (Exception failure) { runFailures.Add(failure); }
                    try { if (runHost is not null) await runHost.DisposeAsync(); }
                    catch (Exception failure) { runFailures.Add(failure); }
                    try { runSelection?.Dispose(); }
                    catch (Exception failure) { runFailures.Add(failure); }
                    var pending = new Stack<Exception>(runFailures.Take(64));
                    var observed = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
                    int inspected = pending.Count;
                    bool traversalComplete = runFailures.Count <= 64;
                    while (pending.Count != 0 && traversalComplete)
                    {
                        Exception failure = pending.Pop();
                        if (!observed.Add(failure)) continue;
                        if (failure is AggregateException aggregate)
                        {
                            for (int innerIndex = 0; innerIndex < aggregate.InnerExceptions.Count; innerIndex++)
                            {
                                if (inspected == 64) { traversalComplete = false; break; }
                                inspected++;
                                pending.Push(aggregate.InnerExceptions[innerIndex]);
                            }
                        }
                        else if (failure.InnerException is { } nested)
                        {
                            if (inspected == 64) traversalComplete = false;
                            else { inspected++; pending.Push(nested); }
                        }
                    }
                    bool startupRetained = observed.Contains(startupPrimary);
                    bool cleanupRetained = observed.Contains(runCleanup);
                    bool restored = ReferenceEquals(runBorrowedProvider, runBuilder.Environment.WebRootFileProvider);
                    await Assert.That(startupAttempted && traversalComplete && pending.Count == 0 && startupRetained && cleanupRetained
                        && runLeaseAliveDuringCleanup && restored).IsTrue()
                        .Because($"Selected-host startup and cleanup must both survive: startupAttempted={startupAttempted};startupRetained={startupRetained};cleanupRetained={cleanupRetained};leaseAliveDuringCleanup={runLeaseAliveDuringCleanup};borrowedProviderRestored={restored}.");
                }
            }
        }
        catch (Exception failure) { failures.Add(failure); }
        try { Directory.Delete(temporary, recursive: true); }
        catch (Exception failure) { failures.Add(failure); }
        if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException(failures);

        static bool CanonicalRelative(string? path) => !string.IsNullOrWhiteSpace(path) && path.Length <= 4096
            && !Path.IsPathRooted(path) && !path.Contains('\\') && !path.Contains(':') && !path.Any(char.IsControl)
            && path.Split('/').All(segment => segment.Length > 0 && segment is not ("." or ".."));
    }

    [Test]
    public async Task HostCompositionTypes_ArePublicAndProfilesAreExhaustive()
    {
        await Assert.That(typeof(BlazorHostProfile).IsPublic).IsTrue();
        await Assert.That(typeof(BlazorHostServiceCollectionExtensions).IsPublic).IsTrue();
        await Assert.That(typeof(BlazorHostApplicationExtensions).IsPublic).IsTrue();
        await Assert.That(Enum.GetValues<BlazorHostProfile>()
            .SequenceEqual([BlazorHostProfile.Split, BlazorHostProfile.Combined])).IsTrue();
    }

    [Test]
    public async Task AddBlazorHostServices_Split_RegistersProxyAndRemoteApiReadiness()
    {
        var builder = CreateBuilder();

        builder.AddBlazorHostServices(BlazorHostProfile.Split, new GracefulShutdownState());

        await Assert.That(builder.Services).Contains(descriptor =>
            descriptor.ServiceType == typeof(IProxyConfigProvider));
        await using var provider = builder.Services.BuildServiceProvider();
        var healthChecks = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations;
        await Assert.That(healthChecks).Contains(registration => registration.Name == "self");
        await Assert.That(healthChecks).Contains(registration => registration.Name == "shutdown");
        await Assert.That(healthChecks).Contains(registration => registration.Name == "distributed-cache");
        await Assert.That(healthChecks).Contains(registration => registration.Name == "oidc-discovery");
        await Assert.That(healthChecks).Contains(registration => registration.Name == "explore-api");
        await Assert.That(healthChecks).Contains(registration => registration.Name == "data-protection-keys");
        await Assert.That(healthChecks).Contains(registration => registration.Name == "atproto-authentication");
    }

    [Test]
    public async Task AddBlazorHostServices_Combined_ExcludesProxyAndRemoteApiReadiness()
    {
        var builder = CreateBuilder();

        builder.AddBlazorHostServices(BlazorHostProfile.Combined, new GracefulShutdownState());

        await Assert.That(builder.Services).DoesNotContain(descriptor =>
            descriptor.ServiceType == typeof(IProxyConfigProvider));
        await using var provider = builder.Services.BuildServiceProvider();
        var healthChecks = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations;
        await Assert.That(healthChecks).DoesNotContain(registration => registration.Name == "self");
        await Assert.That(healthChecks).DoesNotContain(registration => registration.Name == "shutdown");
        await Assert.That(healthChecks).DoesNotContain(registration => registration.Name == "distributed-cache");
        await Assert.That(healthChecks).DoesNotContain(registration => registration.Name == "oidc-discovery");
        await Assert.That(healthChecks).DoesNotContain(registration => registration.Name == "explore-api");
        await Assert.That(healthChecks).Contains(registration => registration.Name == "data-protection-keys");
        await Assert.That(healthChecks).Contains(registration => registration.Name == "atproto-authentication");
    }

    [Test]
    public async Task AddBlazorHostServices_ConflictingProfiles_Throws()
    {
        var builder = CreateBuilder();
        builder.AddBlazorHostServices(BlazorHostProfile.Split, new GracefulShutdownState());

        var act = () => builder.AddBlazorHostServices(
            BlazorHostProfile.Combined,
            new GracefulShutdownState());

        await Assert.That(act).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task InitializeBlazorHostAsync_Split_FailsBeforeServingWhenApiStartupProbeFails()
    {
        var builder = CreateBuilder();
        builder.Configuration["Blazor:RequireApiReadyOnStart"] = "true";
        builder.AddBlazorHostServices(BlazorHostProfile.Split, new GracefulShutdownState());
        var probe = Substitute.For<IExploreApiReadinessProbe>();
        probe.EnsureReadyAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new HttpRequestException("HOSTILE downstream detail")));
        builder.Services.Replace(ServiceDescriptor.Singleton(probe));
        await using var app = builder.Build();

        InvalidOperationException? failure = null;
        try
        {
            await app.InitializeBlazorHostAsync(BlazorHostProfile.Split);
        }
        catch (InvalidOperationException exception)
        {
            failure = exception;
        }

        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.Message)
            .IsEqualTo("The split Blazor host cannot start because the Explore API is not ready.");
        await Assert.That(failure.InnerException).IsTypeOf<HttpRequestException>();
    }

    [Test]
    public async Task InitializeBlazorHostAsync_Combined_DoesNotProbeRemoteApi()
    {
        var builder = CreateBuilder();
        builder.Configuration["Blazor:RequireApiReadyOnStart"] = "true";
        builder.AddBlazorHostServices(BlazorHostProfile.Combined, new GracefulShutdownState());
        var probe = Substitute.For<IExploreApiReadinessProbe>();
        builder.Services.Replace(ServiceDescriptor.Singleton(probe));
        await using var app = builder.Build();

        await app.InitializeBlazorHostAsync(BlazorHostProfile.Combined);

        await probe.DidNotReceive().EnsureReadyAsync(Arg.Any<CancellationToken>());
    }

    private static WebApplicationBuilder CreateBuilder()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(Program).Assembly.GetName().Name,
            EnvironmentName = "Testing"
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:cache"] = "localhost:6379,abortConnect=false,connectTimeout=100",
            ["Keycloak:Authority"] = "https://auth.example.com",
            ["Keycloak:Realm"] = "explore",
            ["Deployment:Mode"] = "SingleTenant",
            ["Deployment:DefaultTenantId"] = "018e4e5c-7f00-7000-8000-000000000001"
        });
        builder.Services.AddSingleton<IExternalDataProtectionSource>(new ExternalDataProtectionTestAuthority("bff"));
        return builder;
    }
}
