using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Models;
using Explore.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

public class CerbosPolicyPackageServiceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"cerbos-package-{Guid.NewGuid():N}");

    [Test]
    public async Task BuildManifestAsync_WithNamespacedArtifacts_ReturnsStableManifest()
    {
        var policiesRoot = CreatePackageRoot();
        await File.WriteAllTextAsync(Path.Combine(policiesRoot, "islamuevent_event.yaml"), "resourcePolicy:\n  resource: islamuevent_event\n");
        await File.WriteAllTextAsync(Path.Combine(policiesRoot, "derived_roles.yaml"), "derivedRoles:\n  name: islamuevent_explore_admin_roles\n");
        await File.WriteAllTextAsync(Path.Combine(policiesRoot, "_schemas", "islamuevent_event.json"), "{\"type\":\"object\"}");
        await File.WriteAllTextAsync(Path.Combine(policiesRoot, "_schemas", "islamuevent_principal.json"), "{\"type\":\"object\"}");

        var service = CreateService(policiesRoot);

        var manifest = await service.BuildManifestAsync();

        await Assert.That(manifest.PackageId).IsEqualTo("test-policy-package");
        await Assert.That(manifest.Version).IsEqualTo(manifest.ContentHash);
        await Assert.That(manifest.ContentHash.Length).IsEqualTo(64);
        await Assert.That(manifest.Artifacts.Count).IsEqualTo(4);
        await Assert.That(manifest.Artifacts.Select(a => a.LogicalId)).IsEquivalentTo([
            "_schemas/islamuevent_event.json",
            "_schemas/islamuevent_principal.json",
            "derived_roles.yaml",
            "islamuevent_event.yaml"
        ]);
        await Assert.That(manifest.Artifacts.Single(a => a.LogicalId == "islamuevent_event.yaml").Kind)
            .IsEqualTo(PolicyArtifactKind.Policy);
        await Assert.That(manifest.Artifacts.Single(a => a.LogicalId == "_schemas/islamuevent_event.json").Kind)
            .IsEqualTo(PolicyArtifactKind.Schema);
    }

    [Test]
    public async Task BuildManifestAsync_WithLegacyPolicyFile_Throws()
    {
        var policiesRoot = CreatePackageRoot();
        await File.WriteAllTextAsync(Path.Combine(policiesRoot, "event.yaml"), "resourcePolicy:\n  resource: event\n");
        await File.WriteAllTextAsync(Path.Combine(policiesRoot, "_schemas", "islamuevent_event.json"), "{\"type\":\"object\"}");

        var service = CreateService(policiesRoot);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.BuildManifestAsync());

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception?.Message ?? string.Empty).Contains("must use the 'islamuevent_' namespace prefix");
    }

    [Test]
    public async Task BuildManifestAsync_WithLegacySchemaFile_Throws()
    {
        var policiesRoot = CreatePackageRoot();
        await File.WriteAllTextAsync(Path.Combine(policiesRoot, "islamuevent_event.yaml"), "resourcePolicy:\n  resource: islamuevent_event\n");
        await File.WriteAllTextAsync(Path.Combine(policiesRoot, "_schemas", "event.json"), "{\"type\":\"object\"}");

        var service = CreateService(policiesRoot);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.BuildManifestAsync());

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception?.Message ?? string.Empty).Contains("must use the 'islamuevent_' namespace prefix");
    }

    [Test]
    public async Task ExportArchiveAsync_WithNamespacedArtifacts_IncludesPoliciesSchemasManifestAndInstructions()
    {
        var policiesRoot = CreatePackageRoot();
        const string policy = "resourcePolicy:\n  resource: islamuevent_event\n";
        await File.WriteAllTextAsync(Path.Combine(policiesRoot, "islamuevent_event.yaml"), policy);
        await File.WriteAllTextAsync(Path.Combine(policiesRoot, "derived_roles.yaml"), "derivedRoles:\n  name: islamuevent_explore_admin_roles\n");
        await File.WriteAllTextAsync(Path.Combine(policiesRoot, "_schemas", "islamuevent_event.json"), "{\"type\":\"object\"}");
        await File.WriteAllTextAsync(Path.Combine(policiesRoot, "_schemas", "islamuevent_principal.json"), "{\"type\":\"object\"}");

        var handler = new RecordingMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var service = CreateService(policiesRoot, handler: handler);

        var archiveModel = await service.ExportArchiveAsync();

        await Assert.That(archiveModel.ContentType).IsEqualTo("application/zip");
        await Assert.That(archiveModel.FileName).StartsWith("test-policy-package-");
        await Assert.That(archiveModel.FileName).EndsWith(".zip");
        await Assert.That(archiveModel.Manifest.Artifacts.Count).IsEqualTo(4);
        await Assert.That(handler.Requests).IsEmpty();

        using var memoryStream = new MemoryStream(archiveModel.Content.ToArray());
        using var zipArchive = new ZipArchive(memoryStream, ZipArchiveMode.Read);
        var entryNames = zipArchive.Entries.Select(entry => entry.FullName).ToArray();

        await Assert.That(entryNames).Contains("_schemas/islamuevent_event.json");
        await Assert.That(entryNames).Contains("_schemas/islamuevent_principal.json");
        await Assert.That(entryNames).Contains("derived_roles.yaml");
        await Assert.That(entryNames).Contains("islamuevent_event.yaml");
        await Assert.That(entryNames).Contains("manifest.json");
        await Assert.That(entryNames).Contains("INSTRUCTIONS.md");

        var instructions = await ReadEntryAsync(zipArchive, "INSTRUCTIONS.md");
        await Assert.That(await ReadEntryAsync(zipArchive, "islamuevent_event.yaml")).IsEqualTo(policy);
        await Assert.That(instructions).Contains("docker compose --profile authz run --rm cerbos-policy-sync");
        await Assert.That(instructions).Contains("cerbosctl put policy --recursive .");
        await Assert.That(instructions).Contains("cerbosctl put schema --recursive _schemas");
        await Assert.That(await ReadEntryAsync(zipArchive, "manifest.json")).Contains("test-policy-package");
    }

    [Test]
    public async Task ExportArchiveAsync_WhenPolicyRootMissing_ThrowsSafeUnavailableException()
    {
        var missingRoot = Path.Combine(_tempRoot, "does-not-exist", "policies");
        var service = CreateService(missingRoot);

        var exception = await Assert.ThrowsAsync<PolicyPackageUnavailableException>(async () =>
            await service.ExportArchiveAsync());

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception?.Message ?? string.Empty).Contains("Cerbos:PolicyPackagePath");
        await Assert.That(exception?.Message ?? string.Empty).DoesNotContain(_tempRoot);
        await Assert.That(exception?.Message ?? string.Empty).DoesNotContain("does-not-exist");
    }

    [Test]
    public async Task BuildManifestAsync_WhenSchemaDirectoryMissing_ThrowsSafeUnavailableException()
    {
        var policiesRoot = Path.Combine(_tempRoot, "policies-without-schemas");
        Directory.CreateDirectory(policiesRoot);
        await File.WriteAllTextAsync(Path.Combine(policiesRoot, "islamuevent_event.yaml"), CreatePolicyYaml("islamuevent_event"));
        var service = CreateService(policiesRoot);

        var exception = await Assert.ThrowsAsync<PolicyPackageUnavailableException>(async () =>
            await service.BuildManifestAsync());

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception?.Message ?? string.Empty).Contains("Cerbos:PolicyPackagePath");
        await Assert.That(exception?.Message ?? string.Empty).DoesNotContain(policiesRoot);
    }

    [Test]
    public async Task PublishAsync_WithNamespacedArtifacts_UploadsSchemasBeforePoliciesThenReloads()
    {
        var policiesRoot = CreatePackageRoot();
        const string schema = "{\"type\":\"object\"}";
        await File.WriteAllTextAsync(Path.Combine(policiesRoot, "islamuevent_event.yaml"), "apiVersion: api.cerbos.dev/v1\nresourcePolicy:\n  resource: islamuevent_event\n  version: default\n  rules: []\n");
        await File.WriteAllTextAsync(Path.Combine(policiesRoot, "_schemas", "islamuevent_event.json"), schema);

        var handler = new RecordingMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}")
        });
        var service = CreateService(policiesRoot, handler: handler);

        var result = await service.PublishAsync();

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(handler.Requests.Count).IsEqualTo(3);
        await Assert.That(handler.Requests[0].RequestUri?.AbsolutePath).IsEqualTo("/admin/schema");
        await Assert.That(handler.Requests[1].RequestUri?.AbsolutePath).IsEqualTo("/admin/policy");
        await Assert.That(handler.Requests[2].RequestUri?.PathAndQuery).IsEqualTo("/admin/store/reload?wait=true");
        await Assert.That(handler.Requests.All(r => r.Authorization?.Scheme == "Basic")).IsTrue();

        using var schemaPayload = JsonDocument.Parse(handler.Requests[0].Body);
        var schemaItem = schemaPayload.RootElement.GetProperty("schemas")[0];
        await Assert.That(schemaItem.GetProperty("id").GetString()).IsEqualTo("islamuevent_event.json");
        await Assert.That(Encoding.UTF8.GetString(Convert.FromBase64String(schemaItem.GetProperty("definition").GetString() ?? string.Empty)))
            .IsEqualTo(schema);

        using var policyPayload = JsonDocument.Parse(handler.Requests[1].Body);
        var policy = policyPayload.RootElement.GetProperty("policies")[0];
        await Assert.That(policy.GetProperty("apiVersion").GetString()).IsEqualTo("api.cerbos.dev/v1");
        await Assert.That(policy.GetProperty("resourcePolicy").GetProperty("resource").GetString()).IsEqualTo("islamuevent_event");
    }

    [Test]
    public async Task PublishAsync_WithByoAdminConfiguration_UsesResolvedEndpointAndCredentialsOnly()
    {
        var policiesRoot = CreatePackageRoot();
        await File.WriteAllTextAsync(Path.Combine(policiesRoot, "islamuevent_event.yaml"), CreatePolicyYaml("islamuevent_event"));
        await File.WriteAllTextAsync(Path.Combine(policiesRoot, "_schemas", "islamuevent_event.json"), "{\"type\":\"object\"}");

        var handler = new RecordingMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var service = CreateService(
            policiesRoot,
            handler: handler,
            resolvedConfiguration: new CerbosConfiguration
            {
                Endpoint = "tenant-grpc.example:3593",
                Mode = CerbosMode.CustomEndpoint,
                AdminEndpoint = "https://tenant-cerbos.example/base",
                AdminUsername = "tenant-user",
                AdminPassword = "tenant-secret",
                IsInstanceDefault = false
            });

        var result = await service.PublishAsync();

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(handler.Requests.All(r => r.RequestUri?.Host == "tenant-cerbos.example")).IsTrue();
        await Assert.That(handler.Requests.All(r => r.RequestUri?.AbsolutePath.StartsWith("/base/admin", StringComparison.Ordinal) == true)).IsTrue();

        var authorization = handler.Requests[0].Authorization;
        await Assert.That(authorization?.Scheme).IsEqualTo("Basic");
        var decodedCredentials = Encoding.UTF8.GetString(Convert.FromBase64String(authorization?.Parameter ?? string.Empty));
        await Assert.That(decodedCredentials).IsEqualTo("tenant-user:tenant-secret");
    }

    [Test]
    public async Task PublishInstanceAsync_WithByoAdminConfiguration_UsesInstanceTargetOnly()
    {
        var policiesRoot = CreatePackageRoot();
        await File.WriteAllTextAsync(Path.Combine(policiesRoot, "islamuevent_event.yaml"), CreatePolicyYaml("islamuevent_event"));
        await File.WriteAllTextAsync(Path.Combine(policiesRoot, "_schemas", "islamuevent_event.json"), "{\"type\":\"object\"}");

        var handler = new RecordingMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var configResolver = Substitute.For<ICerbosConfigResolver>();
        configResolver.ResolveAsync(Arg.Any<CancellationToken>()).Returns(new CerbosConfiguration
        {
            Endpoint = "tenant-grpc.example:3593",
            Mode = CerbosMode.CustomEndpoint,
            AdminEndpoint = "https://tenant-cerbos.example/base",
            AdminUsername = "tenant-user",
            AdminPassword = "tenant-secret",
            IsInstanceDefault = false
        });
        var service = CreateService(policiesRoot, handler: handler, configResolver: configResolver);

        var result = await service.PublishInstanceAsync();

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(handler.Requests.All(request => request.RequestUri?.Host == "cerbos.example")).IsTrue();
        var credentials = Encoding.UTF8.GetString(
            Convert.FromBase64String(handler.Requests[0].Authorization?.Parameter ?? string.Empty));
        await Assert.That(credentials).IsEqualTo("admin:secret");
        await configResolver.DidNotReceive().ResolveAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PublishInstanceAsync_WithOneTimeCredentials_OverridesMissingDeploymentCredentialsForOneRequest()
    {
        var policiesRoot = CreatePackageRoot();
        await File.WriteAllTextAsync(Path.Combine(policiesRoot, "islamuevent_event.yaml"), CreatePolicyYaml("islamuevent_event"));
        await File.WriteAllTextAsync(Path.Combine(policiesRoot, "_schemas", "islamuevent_event.json"), "{\"type\":\"object\"}");

        var handler = new RecordingMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var service = CreateService(
            policiesRoot,
            handler: handler,
            adminUsername: string.Empty,
            adminPassword: string.Empty);

        var result = await service.PublishInstanceAsync(
            CancellationToken.None,
            new PolicyPackageAdminCredentials("one-time-admin", "one-time-password"));

        await Assert.That(result.Succeeded).IsTrue();
        var credentials = Encoding.UTF8.GetString(
            Convert.FromBase64String(handler.Requests[0].Authorization?.Parameter ?? string.Empty));
        await Assert.That(credentials).IsEqualTo("one-time-admin:one-time-password");
    }

    [Test]
    public async Task PublishAsync_WithByoAdminEndpointMissingCredentials_FailsWithoutHttpRequest()
    {
        var policiesRoot = CreatePackageRoot();
        await File.WriteAllTextAsync(Path.Combine(policiesRoot, "islamuevent_event.yaml"), CreatePolicyYaml("islamuevent_event"));
        await File.WriteAllTextAsync(Path.Combine(policiesRoot, "_schemas", "islamuevent_event.json"), "{\"type\":\"object\"}");

        var handler = new RecordingMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var service = CreateService(
            policiesRoot,
            handler: handler,
            resolvedConfiguration: new CerbosConfiguration
            {
                Endpoint = "tenant-grpc.example:3593",
                Mode = CerbosMode.CustomEndpoint,
                AdminEndpoint = "https://tenant-cerbos.example",
                AdminUsername = "tenant-user",
                AdminPassword = null,
                IsInstanceDefault = false
            });

        var result = await service.PublishAsync();

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.IssueCode).IsEqualTo(PolicyPackageIssueCode.AdminApiNotConfigured);
        await Assert.That(handler.Requests).IsEmpty();
        await Assert.That(string.Join(' ', result.Warnings)).Contains("requires both admin username and admin password");
        await Assert.That(string.Join(' ', result.Warnings)).DoesNotContain("tenant-user");
    }

    [Arguments("http://tenant-cerbos.example")]
    [Arguments("https://localhost:3592")]
    [Arguments("https://127.0.0.1:3592")]
    [Arguments("https://user:password@tenant-cerbos.example")]
    [Test]
    public async Task PublishAsync_WithUnsafeByoAdminEndpoint_FailsWithoutHttpRequest(string adminEndpoint)
    {
        var policiesRoot = CreatePackageRoot();
        await File.WriteAllTextAsync(Path.Combine(policiesRoot, "islamuevent_event.yaml"), CreatePolicyYaml("islamuevent_event"));
        await File.WriteAllTextAsync(Path.Combine(policiesRoot, "_schemas", "islamuevent_event.json"), "{\"type\":\"object\"}");

        var handler = new RecordingMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var service = CreateService(
            policiesRoot,
            handler: handler,
            resolvedConfiguration: new CerbosConfiguration
            {
                Endpoint = "tenant-grpc.example:3593",
                Mode = CerbosMode.CustomEndpoint,
                AdminEndpoint = adminEndpoint,
                AdminUsername = "tenant-user",
                AdminPassword = "tenant-secret",
                IsInstanceDefault = false
            });

        var result = await service.PublishAsync();

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(handler.Requests).IsEmpty();
        await Assert.That(string.Join(' ', result.Warnings)).DoesNotContain("tenant-secret");
        await Assert.That(string.Join(' ', result.Warnings)).DoesNotContain("password@tenant-cerbos.example");
    }

    [Test]
    public async Task PublishAsync_WithPolicyLimit_SplitsPolicyRequests()
    {
        var policiesRoot = CreatePackageRoot();
        await File.WriteAllTextAsync(Path.Combine(policiesRoot, "_schemas", "islamuevent_event.json"), "{\"type\":\"object\"}");
        await File.WriteAllTextAsync(Path.Combine(policiesRoot, "islamuevent_event.yaml"), CreatePolicyYaml("islamuevent_event"));
        await File.WriteAllTextAsync(Path.Combine(policiesRoot, "islamuevent_tenant.yaml"), CreatePolicyYaml("islamuevent_tenant"));
        await File.WriteAllTextAsync(Path.Combine(policiesRoot, "islamuevent_organization.yaml"), CreatePolicyYaml("islamuevent_organization"));

        var handler = new RecordingMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var service = CreateService(policiesRoot, maxPoliciesPerRequest: 2, handler: handler);

        var result = await service.PublishAsync();

        await Assert.That(result.Succeeded).IsTrue();
        var policyRequests = handler.Requests.Where(r => r.RequestUri?.AbsolutePath == "/admin/policy").ToArray();
        await Assert.That(policyRequests.Length).IsEqualTo(2);
        using var firstBatch = JsonDocument.Parse(policyRequests[0].Body);
        using var secondBatch = JsonDocument.Parse(policyRequests[1].Body);
        await Assert.That(firstBatch.RootElement.GetProperty("policies").GetArrayLength()).IsEqualTo(2);
        await Assert.That(secondBatch.RootElement.GetProperty("policies").GetArrayLength()).IsEqualTo(1);
    }

    [Test]
    public async Task PublishAsync_WhenSchemaUploadFails_ReturnsFailureWithoutPolicyUpload()
    {
        var policiesRoot = CreatePackageRoot();
        await File.WriteAllTextAsync(Path.Combine(policiesRoot, "islamuevent_event.yaml"), CreatePolicyYaml("islamuevent_event"));
        await File.WriteAllTextAsync(Path.Combine(policiesRoot, "_schemas", "islamuevent_event.json"), "{\"type\":\"object\"}");

        var handler = new RecordingMessageHandler(request => request.RequestUri?.AbsolutePath == "/admin/schema"
            ? new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("bad credentials") }
            : new HttpResponseMessage(HttpStatusCode.OK));
        var service = CreateService(policiesRoot, handler: handler);

        var result = await service.PublishAsync();

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.IssueCode).IsEqualTo(PolicyPackageIssueCode.AdminApiAuthenticationFailed);
        await Assert.That(result.Message).Contains("publish failed");
        await Assert.That(result.Message).Contains("authentication failed");
        await Assert.That(string.Join(' ', result.Warnings)).DoesNotContain("bad credentials");
        await Assert.That(string.Join(' ', result.Warnings)).DoesNotContain("secret");
        await Assert.That(handler.Requests.Select(r => r.RequestUri?.AbsolutePath ?? string.Empty)).IsEquivalentTo(["/admin/schema"]);
    }

    [Test]
    public async Task PublishAsync_WhenSchemaUploadIsUnavailable_ReturnsAdminApiUnavailableIssue()
    {
        var policiesRoot = CreatePackageRoot();
        await File.WriteAllTextAsync(Path.Combine(policiesRoot, "islamuevent_event.yaml"), CreatePolicyYaml("islamuevent_event"));
        await File.WriteAllTextAsync(Path.Combine(policiesRoot, "_schemas", "islamuevent_event.json"), "{\"type\":\"object\"}");

        var handler = new RecordingMessageHandler(request => request.RequestUri?.AbsolutePath == "/admin/schema"
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("upstream unavailable with secret") }
            : new HttpResponseMessage(HttpStatusCode.OK));
        var service = CreateService(policiesRoot, handler: handler);

        var result = await service.PublishAsync();

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.IssueCode).IsEqualTo(PolicyPackageIssueCode.AdminApiUnavailable);
        await Assert.That(result.Message).Contains("unavailable");
        await Assert.That(string.Join(' ', result.Warnings)).DoesNotContain("upstream unavailable");
        await Assert.That(string.Join(' ', result.Warnings)).DoesNotContain("secret");
        await Assert.That(handler.Requests.Select(r => r.RequestUri?.AbsolutePath ?? string.Empty)).IsEquivalentTo(["/admin/schema"]);
    }

    [Test]
    public async Task PublishAsync_WhenSchemaUploadTransportFails_ReturnsAdminApiUnavailableIssue()
    {
        var policiesRoot = CreatePackageRoot();
        await File.WriteAllTextAsync(Path.Combine(policiesRoot, "islamuevent_event.yaml"), CreatePolicyYaml("islamuevent_event"));
        await File.WriteAllTextAsync(Path.Combine(policiesRoot, "_schemas", "islamuevent_event.json"), "{\"type\":\"object\"}");

        var handler = new RecordingMessageHandler(request => request.RequestUri?.AbsolutePath == "/admin/schema"
            ? throw new HttpRequestException("tenant-secret transport failure")
            : new HttpResponseMessage(HttpStatusCode.OK));
        var service = CreateService(policiesRoot, handler: handler);

        var result = await service.PublishAsync();

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.IssueCode).IsEqualTo(PolicyPackageIssueCode.AdminApiUnavailable);
        await Assert.That(result.Message).Contains("unavailable");
        await Assert.That(string.Join(' ', result.Warnings)).DoesNotContain("tenant-secret");
        await Assert.That(string.Join(' ', result.Warnings)).Contains("failed before a response was received");
        await Assert.That(handler.Requests.Select(r => r.RequestUri?.AbsolutePath ?? string.Empty)).IsEquivalentTo(["/admin/schema"]);
    }

    [Test]
    public async Task PublishAsync_WhenReloadFails_ReturnsFailureAfterUpload()
    {
        var policiesRoot = CreatePackageRoot();
        await File.WriteAllTextAsync(Path.Combine(policiesRoot, "islamuevent_event.yaml"), CreatePolicyYaml("islamuevent_event"));
        await File.WriteAllTextAsync(Path.Combine(policiesRoot, "_schemas", "islamuevent_event.json"), "{\"type\":\"object\"}");

        var handler = new RecordingMessageHandler(request => request.RequestUri?.AbsolutePath == "/admin/store/reload"
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : new HttpResponseMessage(HttpStatusCode.OK));
        var service = CreateService(policiesRoot, handler: handler);

        var result = await service.PublishAsync();

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.IssueCode).IsEqualTo(PolicyPackageIssueCode.ReloadFailed);
        await Assert.That(result.Message).Contains("failed to reload");
        await Assert.That(handler.Requests.Select(r => r.RequestUri?.AbsolutePath ?? string.Empty)).IsEquivalentTo([
            "/admin/schema",
            "/admin/policy",
            "/admin/store/reload"
        ]);
    }

    [Test]
    public async Task PublishAsync_WhenReloadTransportFails_ReturnsReloadFailedIssue()
    {
        var policiesRoot = CreatePackageRoot();
        await File.WriteAllTextAsync(Path.Combine(policiesRoot, "islamuevent_event.yaml"), CreatePolicyYaml("islamuevent_event"));
        await File.WriteAllTextAsync(Path.Combine(policiesRoot, "_schemas", "islamuevent_event.json"), "{\"type\":\"object\"}");

        var handler = new RecordingMessageHandler(request => request.RequestUri?.AbsolutePath == "/admin/store/reload"
            ? throw new HttpRequestException("tenant-secret reload failure")
            : new HttpResponseMessage(HttpStatusCode.OK));
        var service = CreateService(policiesRoot, handler: handler);

        var result = await service.PublishAsync();

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.IssueCode).IsEqualTo(PolicyPackageIssueCode.ReloadFailed);
        await Assert.That(result.Message).Contains("failed to reload");
        await Assert.That(string.Join(' ', result.Warnings)).DoesNotContain("tenant-secret");
        await Assert.That(handler.Requests.Select(r => r.RequestUri?.AbsolutePath ?? string.Empty)).IsEquivalentTo([
            "/admin/schema",
            "/admin/policy",
            "/admin/store/reload"
        ]);
    }

    [Test]
    /// <summary>
    /// Status now queries the store rather than assuming freshness. The package identity and hash are
    /// still reported, and the request that verifies presence is the listing call.
    /// </summary>
    public async Task GetStatusAsync_WithConfiguredAdminApi_QueriesStoreAndReportsPackageIdentity()
    {
        var policiesRoot = CreatePackageRoot();
        await File.WriteAllTextAsync(Path.Combine(policiesRoot, "islamuevent_event.yaml"), CreatePolicyYaml("islamuevent_event"));
        await File.WriteAllTextAsync(Path.Combine(policiesRoot, "_schemas", "islamuevent_event.json"), "{\"type\":\"object\"}");

        var handler = StoreHandler(
            "{\"policyIds\":[\"resource.islamuevent_event.vdefault\"]}",
            StoredPolicies(("resource.islamuevent_event.vdefault", "13466950985171780168")));
        var service = CreateService(policiesRoot, handler: handler);

        var result = await service.GetStatusAsync();

        await Assert.That(result.IssueCode).IsEqualTo(PolicyPackageIssueCode.None);
        await Assert.That(result.IsHealthy).IsTrue();
        await Assert.That(result.PackageId).IsEqualTo("test-policy-package");
        await Assert.That(result.ContentHash.Length).IsEqualTo(64);
        await Assert.That(result.ObservedRevision).IsNotNull();

        // Presence and content are two separate questions and need two separate calls.
        await Assert.That(handler.Requests.Select(request => request.RequestUri!.AbsolutePath))
            .IsEquivalentTo(new[] { "/admin/policies", "/admin/policy" });
    }

    [Test]
    public async Task GetStatusAsync_WithByoAdminEndpointMissingCredentials_ReturnsNotConfiguredWithoutSecretLeak()
    {
        var policiesRoot = CreatePackageRoot();
        await File.WriteAllTextAsync(Path.Combine(policiesRoot, "islamuevent_event.yaml"), CreatePolicyYaml("islamuevent_event"));
        await File.WriteAllTextAsync(Path.Combine(policiesRoot, "_schemas", "islamuevent_event.json"), "{\"type\":\"object\"}");

        var handler = new RecordingMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var service = CreateService(
            policiesRoot,
            handler: handler,
            resolvedConfiguration: new CerbosConfiguration
            {
                Endpoint = "tenant-grpc.example:3593",
                Mode = CerbosMode.CustomEndpoint,
                AdminEndpoint = "https://tenant-cerbos.example",
                AdminUsername = "tenant-user",
                AdminPassword = null,
                IsInstanceDefault = false
            });

        var result = await service.GetStatusAsync();

        await Assert.That(result.IssueCode).IsEqualTo(PolicyPackageIssueCode.AdminApiNotConfigured);
        await Assert.That(result.IsHealthy).IsFalse();
        await Assert.That(string.Join(' ', result.Warnings)).Contains("requires both admin username and admin password");
        await Assert.That(string.Join(' ', result.Warnings)).DoesNotContain("tenant-user");
        await Assert.That(handler.Requests).IsEmpty();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);

        GC.SuppressFinalize(this);
    }

    private string CreatePackageRoot()
    {
        var policiesRoot = Path.Combine(_tempRoot, "policies");
        Directory.CreateDirectory(Path.Combine(policiesRoot, "_schemas"));
        return policiesRoot;
    }

    /// <summary>
    /// An empty store means the package was never published. Since Phase 2 removed the local carve-out,
    /// nothing answers those checks as a fallback — every one of them fails closed — so this has to be
    /// reported as a mismatch an operator can act on, not as an unknown they can ignore.
    /// </summary>
    [Test]
    public async Task GetStatusAsync_WhenStoreIsEmpty_ReportsPackageMismatch()
    {
        var policiesRoot = await CreatePopulatedPackageRootAsync();
        var handler = new RecordingMessageHandler(_ => JsonResponse("{\"policyIds\":[]}"));
        var service = CreateService(policiesRoot, handler: handler);

        var status = await service.GetStatusAsync();

        await Assert.That(status.IssueCode).IsEqualTo(PolicyPackageIssueCode.PackageMismatch);
        await Assert.That(status.IsHealthy).IsFalse();
        await Assert.That(status.Message).Contains("empty");
    }

    [Test]
    public async Task GetStatusAsync_WhenStoreHoldsFewerPoliciesThanDeclared_ReportsPartialPublish()
    {
        var policiesRoot = await CreatePopulatedPackageRootAsync();
        var handler = new RecordingMessageHandler(_ =>
            JsonResponse("{\"policyIds\":[\"resource.islamuevent_event.vdefault\"]}"));
        var service = CreateService(policiesRoot, handler: handler);

        var status = await service.GetStatusAsync();

        await Assert.That(status.IssueCode).IsEqualTo(PolicyPackageIssueCode.PackageMismatch);
        await Assert.That(status.Message).Contains("partial").Or.Contains("but this package declares");
    }

    [Test]
    public async Task GetStatusAsync_WhenStoreCoversDeclaredPolicies_ReportsHealthyWithObservedRevision()
    {
        var policiesRoot = await CreatePopulatedPackageRootAsync();
        var handler = StoreHandler(
            "{\"policyIds\":[\"derived_roles.islamuevent_explore_admin_roles\",\"resource.islamuevent_event.vdefault\"]}",
            StoredPolicies(
                ("derived_roles.islamuevent_explore_admin_roles", "17613918467673392044"),
                ("resource.islamuevent_event.vdefault", "13466950985171780168")));
        var service = CreateService(policiesRoot, handler: handler);

        var status = await service.GetStatusAsync();

        await Assert.That(status.IssueCode).IsEqualTo(PolicyPackageIssueCode.None);
        await Assert.That(status.IsHealthy).IsTrue();
        await Assert.That(status.ObservedRevision).IsNotNull();

        // A healthy store with a readable revision has nothing left to caveat. The old "content equality
        // is unverifiable" warning was retired when the per-policy hash made it verifiable.
        await Assert.That(status.Warnings).IsEmpty();
    }

    /// <summary>
    /// The whole point of the revision: an operator edits a policy in the store without changing its
    /// identifier. A listing still shows the same identifiers, so only the content hash can catch it.
    /// </summary>
    [Test]
    public async Task GetStatusAsync_WhenAStoredPolicyBodyChanges_ReportsADifferentRevision()
    {
        var policiesRoot = await CreatePopulatedPackageRootAsync();
        const string ListJson =
            "{\"policyIds\":[\"derived_roles.islamuevent_explore_admin_roles\",\"resource.islamuevent_event.vdefault\"]}";

        var before = await CreateService(
            policiesRoot,
            handler: StoreHandler(
                ListJson,
                StoredPolicies(
                    ("derived_roles.islamuevent_explore_admin_roles", "17613918467673392044"),
                    ("resource.islamuevent_event.vdefault", "13466950985171780168")))).GetStatusAsync();

        var after = await CreateService(
            policiesRoot,
            handler: StoreHandler(
                ListJson,
                StoredPolicies(
                    ("derived_roles.islamuevent_explore_admin_roles", "17613918467673392044"),
                    ("resource.islamuevent_event.vdefault", "6065751633899809269")))).GetStatusAsync();

        await Assert.That(before.IssueCode).IsEqualTo(PolicyPackageIssueCode.None);
        await Assert.That(after.IssueCode).IsEqualTo(PolicyPackageIssueCode.None);
        await Assert.That(before.ObservedRevision).IsNotNull();
        await Assert.That(after.ObservedRevision).IsNotEqualTo(before.ObservedRevision);
    }

    /// <summary>
    /// A complete store whose hashes cannot be read is still healthy for publication purposes, but the
    /// revision is null and the caveat has to say so — that null is what makes sensitive actions deny.
    /// </summary>
    [Test]
    public async Task GetStatusAsync_WhenPolicyHashesCannotBeRead_ReportsNoRevisionAndWarns()
    {
        var policiesRoot = await CreatePopulatedPackageRootAsync();
        var handler = StoreHandler(
            "{\"policyIds\":[\"derived_roles.islamuevent_explore_admin_roles\",\"resource.islamuevent_event.vdefault\"]}");
        var service = CreateService(policiesRoot, handler: handler);

        var status = await service.GetStatusAsync();

        await Assert.That(status.IssueCode).IsEqualTo(PolicyPackageIssueCode.None);
        await Assert.That(status.ObservedRevision).IsNull();
        await Assert.That(string.Join(" ", status.Warnings)).Contains("in-place edit");
    }

    /// <summary>
    /// A store that cannot be listed leaves freshness unverified. That must read as uncertainty rather
    /// than health, because Phase 3 requires sensitive writes to deny on revision uncertainty.
    /// </summary>
    [Test]
    public async Task GetStatusAsync_WhenStoreCannotBeListed_ReportsStatusUnknown()
    {
        var policiesRoot = await CreatePopulatedPackageRootAsync();
        var handler = new RecordingMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var service = CreateService(policiesRoot, handler: handler);

        var status = await service.GetStatusAsync();

        await Assert.That(status.IssueCode).IsEqualTo(PolicyPackageIssueCode.PackageStatusUnknown);
        await Assert.That(string.Join(" ", status.Warnings)).Contains("explicit status operation");
    }

    private async Task<string> CreatePopulatedPackageRootAsync()
    {
        var policiesRoot = CreatePackageRoot();
        await File.WriteAllTextAsync(
            Path.Combine(policiesRoot, "islamuevent_event.yaml"),
            "resourcePolicy:\n  resource: islamuevent_event\n");
        await File.WriteAllTextAsync(
            Path.Combine(policiesRoot, "derived_roles.yaml"),
            "derivedRoles:\n  name: islamuevent_explore_admin_roles\n");
        await File.WriteAllTextAsync(
            Path.Combine(policiesRoot, "_schemas", "islamuevent_principal.json"),
            "{\"type\":\"object\"}");
        return policiesRoot;
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    /// <summary>
    /// Status makes two different Admin API calls — <c>/admin/policies</c> to list identifiers and
    /// <c>/admin/policy</c> to read each policy's content hash. They return different shapes, so a
    /// handler that answers both with one body would silently exercise only the listing path.
    /// </summary>
    private static RecordingMessageHandler StoreHandler(string listJson, string? fetchJson = null) =>
        new(request => request.RequestUri!.AbsolutePath switch
        {
            "/admin/policies" => JsonResponse(listJson),
            "/admin/policy" when fetchJson is not null => JsonResponse(fetchJson),
            _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        });

    /// <summary>Builds an <c>/admin/policy</c> body carrying the Cerbos-computed hash per policy.</summary>
    private static string StoredPolicies(params (string Id, string Hash)[] policies) =>
        "{\"policies\":["
        + string.Join(
            ',',
            policies.Select(policy =>
                $"{{\"metadata\":{{\"hash\":\"{policy.Hash}\",\"storeIdentifier\":\"{policy.Id}\"}}}}"))
        + "]}";

    private static CerbosPolicyPackageService CreateService(
        string policiesRoot,
        int maxPoliciesPerRequest = 100,
        RecordingMessageHandler? handler = null,
        CerbosConfiguration? resolvedConfiguration = null,
        ICerbosConfigResolver? configResolver = null,
        string adminUsername = "admin",
        string adminPassword = "secret")
    {
        var options = Options.Create(new CerbosPolicyPackageOptions
        {
            PoliciesPath = policiesRoot,
            ProductNamespacePrefix = "islamuevent_",
            PackageId = "test-policy-package",
            MaxPoliciesPerRequest = maxPoliciesPerRequest
        });
        var adminOptions = Options.Create(new CerbosAdminApiSettings
        {
            Endpoints = ["https://cerbos.example"],
            AdminUsername = adminUsername,
            AdminPassword = adminPassword
        });

        handler ??= new RecordingMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        configResolver ??= Substitute.For<ICerbosConfigResolver>();
        configResolver.ResolveAsync(Arg.Any<CancellationToken>()).Returns(resolvedConfiguration ?? new CerbosConfiguration
        {
            Endpoint = "instance-cerbos.example:3593",
            Mode = CerbosMode.Instance,
            IsInstanceDefault = true
        });

        return new CerbosPolicyPackageService(
            options,
            adminOptions,
            configResolver,
            new CerbosAdminEndpointValidator(options),
            new StaticHttpClientFactory(new HttpClient(handler)),
            Substitute.For<ILogger<CerbosPolicyPackageService>>());
    }

    private static string CreatePolicyYaml(string resourceKind)
    {
        return $"apiVersion: api.cerbos.dev/v1\nresourcePolicy:\n  resource: {resourceKind}\n  version: default\n  rules: []\n";
    }

    private static async Task<string> ReadEntryAsync(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName)
            ?? throw new InvalidOperationException($"ZIP entry '{entryName}' was not found.");
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private sealed class StaticHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public StaticHttpClientFactory(HttpClient client)
        {
            _client = client;
        }

        public HttpClient CreateClient(string name)
        {
            return _client;
        }
    }

    private sealed class RecordingMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public RecordingMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri,
                request.Headers.Authorization,
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));

            return _responseFactory(request);
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri? RequestUri,
        AuthenticationHeaderValue? Authorization,
        string Body);
}
