using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authorization;
using Explore.Domain;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel]
public sealed class AuthorizationPolicyPackageDownloadControllerTests
{
    private const string SetupPath = "/api/instanceonboarding/authz-provider-configuration/package";
    private const string AdminPath = "/api/instance/settings/authz-provider/package";
    private const string Policy = "resourcePolicy:\n  resource: islamuevent_event\n";
    private const string Schema = "{\"type\":\"object\"}";
    private static CancellationToken Token => TestContext.Current!.Execution.CancellationToken;

    [Test]
    public async Task SetupDownload_RejectsMissingAndInvalidAuthorityBeforeReturningRealArchive()
    {
        using var package = new PolicyDirectory();
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(incompleteSetup: true);
        await using var host = ConfigurePackage(factory, package.Path);
        using var client = CreateClient(host);

        using (var missing = await client.GetAsync(SetupPath, Token))
            await AssertProblemAsync(missing, HttpStatusCode.Forbidden, "forbidden", package.Path);
        client.DefaultRequestHeaders.Add("X-Setup-Secret", Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        using (var invalid = await client.GetAsync(SetupPath, Token))
            await AssertProblemAsync(invalid, HttpStatusCode.Forbidden, "forbidden", package.Path);

        client.DefaultRequestHeaders.Remove("X-Setup-Secret");
        client.DefaultRequestHeaders.Add("X-Setup-Secret", factory.SetupSecret);
        using var response = await client.GetAsync(SetupPath, Token);
        await AssertArchiveAsync(response);
    }

    [Test]
    public async Task SetupDownload_AfterCompletionRejectsStaleSetupAuthority()
    {
        using var package = new PolicyDirectory();
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        await using var host = ConfigurePackage(factory, package.Path);
        using var client = CreateClient(host);
        client.DefaultRequestHeaders.Add("X-Setup-Secret", factory.SetupSecret);

        using var response = await client.GetAsync(SetupPath, Token);

        await AssertProblemAsync(response, HttpStatusCode.Gone, "setup_already_completed", package.Path);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AdminDownload_RequiresPersistedAdministratorAuthority(bool isAdministrator)
    {
        using var package = new PolicyDirectory();
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        var credentials = await factory.SeedLocalUserAsync(emailConfirmed: true);
        if (isAdministrator)
            await GrantAdministratorAsync(factory, credentials.Identifier);
        await using var host = ConfigurePackage(factory, package.Path);
        using var client = CreateClient(host);
        using var login = await client.PostAsJsonAsync("/api/auth/local/login", credentials, Token);
        await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var session = await JsonDocument.ParseAsync(await login.Content.ReadAsStreamAsync(Token), cancellationToken: Token);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.RootElement.GetProperty("token").GetString());

        using var response = await client.GetAsync(AdminPath, Token);

        if (isAdministrator)
            await AssertArchiveAsync(response);
        else
            await AssertProblemAsync(response, HttpStatusCode.Forbidden, "forbidden", package.Path);
    }

    [Test]
    public async Task AdminDownload_RejectsAnonymousRequest()
    {
        using var package = new PolicyDirectory();
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        await using var host = ConfigurePackage(factory, package.Path);
        using var client = CreateClient(host);

        using var response = await client.GetAsync(AdminPath, Token);

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "authentication_required", package.Path);
    }

    [Test]
    public async Task SetupDownload_WhenPackageMissingReturnsSafeUnavailableProblem()
    {
        using var package = new PolicyDirectory();
        string missingPath = System.IO.Path.Combine(package.Path, "private-missing-package");
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(incompleteSetup: true);
        await using var host = ConfigurePackage(factory, missingPath);
        using var client = CreateClient(host);
        client.DefaultRequestHeaders.Add("X-Setup-Secret", factory.SetupSecret);

        using var response = await client.GetAsync(SetupPath, Token);

        await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "authorization_policy_package_unavailable", missingPath);
    }

    private static WebApplicationFactory<Program> ConfigurePackage(LocalAdmissionWebApplicationFactory factory, string path)
        => factory.WithWebHostBuilder(builder => builder
            .ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["PublicBaseUrl"] = "https://localhost" }))
            .ConfigureTestServices(services =>
            services.PostConfigure<CerbosPolicyPackageOptions>(options =>
            {
                options.PoliciesPath = path;
                options.PackageId = "download-contract";
            })));

    private static HttpClient CreateClient(WebApplicationFactory<Program> host)
        => host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

    private static async Task GrantAdministratorAsync(LocalAdmissionWebApplicationFactory factory, string identifier)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var identity = await database.LocalIdentityUsers.SingleAsync(user => user.Email == identifier, Token);
        var user = await database.Users.SingleAsync(user => user.Id == identity.Id, Token);
        var role = await database.Set<Role>().SingleAsync(item => item.MasterCode == "platform.admin", Token);
        database.PlatformUserRoles.Add(new PlatformUserRole
        {
            Id = Guid.CreateVersion7(), UserId = user.Id, User = user,
            RoleId = role.Id, Role = role, GrantedAt = DateTime.UtcNow
        });
        await database.SaveChangesAsync(Token);
    }

    private static async Task AssertArchiveAsync(HttpResponseMessage response)
    {
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/zip");
        await Assert.That(response.Content.Headers.ContentDisposition?.DispositionType).IsEqualTo("attachment");
        await using var content = await response.Content.ReadAsStreamAsync(Token);
        using var archive = new ZipArchive(content, ZipArchiveMode.Read);
        await Assert.That(archive.Entries.Select(entry => entry.FullName)).IsEquivalentTo(
            new[] { "islamuevent_event.yaml", "_schemas/islamuevent_event.json", "manifest.json", "INSTRUCTIONS.md" });
        await Assert.That(await ReadEntryAsync(archive, "islamuevent_event.yaml")).IsEqualTo(Policy);
        await Assert.That(await ReadEntryAsync(archive, "_schemas/islamuevent_event.json")).IsEqualTo(Schema);
        await Assert.That(string.IsNullOrWhiteSpace(await ReadEntryAsync(archive, "INSTRUCTIONS.md"))).IsFalse();
        var manifest = JsonSerializer.Deserialize<PolicyPackageManifest>(await ReadEntryAsync(archive, "manifest.json"))!;
        await Assert.That(manifest.PackageId).IsEqualTo("download-contract");
        await Assert.That(manifest.Artifacts.Select(artifact => artifact.LogicalId)).IsEquivalentTo(
            new[] { "islamuevent_event.yaml", "_schemas/islamuevent_event.json" });
        await Assert.That(response.Content.Headers.ContentDisposition?.FileNameStar)
            .IsEqualTo($"download-contract-{manifest.ContentHash[..12]}.zip");
    }

    private static async Task<string> ReadEntryAsync(ZipArchive archive, string name)
    {
        using var reader = new StreamReader(archive.GetEntry(name)!.Open());
        return await reader.ReadToEndAsync(Token);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code, string privatePath)
    {
        await Assert.That(response.StatusCode).IsEqualTo(status);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");
        await Assert.That(response.Content.Headers.ContentDisposition).IsNull();
        string body = await response.Content.ReadAsStringAsync(Token);
        await Assert.That(body).DoesNotContain(privatePath);
        using var problem = JsonDocument.Parse(body);
        await Assert.That(problem.RootElement.GetProperty("status").GetInt32()).IsEqualTo((int)status);
        await Assert.That(problem.RootElement.GetProperty("code").GetString()).IsEqualTo(code);
        await Assert.That(problem.RootElement.TryGetProperty("Content", out _)).IsFalse();
    }

    private sealed class PolicyDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"policy-download-{Guid.CreateVersion7():N}");

        public PolicyDirectory()
        {
            Directory.CreateDirectory(System.IO.Path.Combine(Path, "_schemas"));
            File.WriteAllText(System.IO.Path.Combine(Path, "islamuevent_event.yaml"), Policy);
            File.WriteAllText(System.IO.Path.Combine(Path, "_schemas", "islamuevent_event.json"), Schema);
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
