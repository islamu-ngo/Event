namespace Event.Api.IntegrationTests.Features;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Settings.Documents.Payloads;
using Explore.Persistence;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

public partial class InstanceOperatorIdentityControllerTests
{
    private const string ManifestUrl = BaseUrl + "/manifest";

    [Test]
    public async Task Portability_IdempotencyReplayRechecksRevokedAdministratorGrant()
    {
        using var factory = new OnboardingWebApplicationFactory();
        using HttpClient client = factory.CreateClient();
        Guid actor = await SeedPlatformAdminAsync(factory);
        await SeedPortableIdentityAsync(factory);
        AuthorizeOperatorClient(client, actor);

        OperatorIdentityManifest manifest = OperatorIdentityManifestJson.Parse(
            await client.GetByteArrayAsync(ManifestUrl));
        var import = new ImportInstanceOperatorIdentityCommand(
            manifest, manifest.RevisionHash);
        string idempotencyKey = Guid.CreateVersion7().ToString("D");

        using (var first = new HttpRequestMessage(HttpMethod.Post, ManifestUrl + "/import"))
        {
            first.Headers.Add("Idempotency-Key", idempotencyKey);
            first.Content = JsonContent.Create(import);
            using HttpResponseMessage accepted = await client.SendAsync(first);
            await Assert.That(accepted.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }

        using (IServiceScope scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            database.PlatformUserRoles.RemoveRange(
                await database.PlatformUserRoles.Where(role => role.UserId == actor).ToListAsync());
            await database.SaveChangesAsync();
        }

        using var replay = new HttpRequestMessage(HttpMethod.Post, ManifestUrl + "/import");
        replay.Headers.Add("Idempotency-Key", idempotencyKey);
        replay.Content = JsonContent.Create(import);
        using HttpResponseMessage denied = await client.SendAsync(replay);
        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Portability_AdminRoundTripAndStaleImportUseRealPersistenceAndProblemDetails()
    {
        using var factory = new OnboardingWebApplicationFactory();
        using HttpClient client = factory.CreateClient();
        Guid actor = await SeedPlatformAdminAsync(factory);
        await SeedPortableIdentityAsync(factory);
        AuthorizeOperatorClient(client, actor);

        using HttpResponseMessage exported = await client.GetAsync(ManifestUrl);
        await Assert.That(exported.StatusCode).IsEqualTo(HttpStatusCode.OK);
        OperatorIdentityManifest manifest = OperatorIdentityManifestJson.Parse(await exported.Content.ReadAsByteArrayAsync());
        await Assert.That(exported.Headers.CacheControl?.NoStore).IsTrue();
        var request = new ImportInstanceOperatorIdentityCommand(manifest, manifest.RevisionHash);
        using HttpResponseMessage imported = await client.PostAsJsonAsync(ManifestUrl + "/import", request);
        await Assert.That(imported.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using HttpResponseMessage stale = await client.PostAsJsonAsync(ManifestUrl + "/import", request);
        await Assert.That(stale.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(stale.Content.Headers.ContentType!.MediaType).IsEqualTo("application/problem+json");
        using JsonDocument problem = JsonDocument.Parse(await stale.Content.ReadAsStringAsync());
        await Assert.That(problem.RootElement.GetProperty("status").GetInt32()).IsEqualTo(409);

        using IServiceScope scope = factory.Services.CreateScope();
        ExploreDbContext database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        OutboxMessage audit = await database.OutboxMessages.SingleAsync(
            message => message.EventType == OperatorIdentityImportAudit.EventType);
        await Assert.That(audit.Payload!.Contains("Independent", StringComparison.Ordinal)).IsFalse();
        await Assert.That(JsonSerializer.Deserialize<OperatorIdentityImportAudit>(audit.Payload!)!.ActorUserId).IsEqualTo(actor);
    }

    [Test]
    public async Task Portability_UnauthorizedAndMalformedRequestsCannotMutateIdentity()
    {
        using var factory = new OnboardingWebApplicationFactory();
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage anonymous = await client.GetAsync(ManifestUrl);
        await Assert.That(anonymous.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        AuthorizeOperatorClient(client, Guid.CreateVersion7());
        using HttpResponseMessage denied = await client.GetAsync(ManifestUrl);
        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);

        Guid actor = await SeedPlatformAdminAsync(factory);
        await SeedPortableIdentityAsync(factory);
        AuthorizeOperatorClient(client, actor);
        byte[] before = await client.GetByteArrayAsync(ManifestUrl);
        string[] malformed =
        [
            """{"manifest":{},"expectedRevisionHash":"invalid"}""",
            """{"manifest":null,"expectedRevisionHash":null}""",
            """{"manifest":{"document":{"operatorId":17}},"expectedRevisionHash":"invalid"}""",
            """{"manifest":{"document":{"legalName":"do-not-reflect@example.test"}},"expectedRevisionHash":"invalid"}"""
        ];
        foreach (string body in malformed)
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await client.PostAsync(ManifestUrl + "/import", content);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
            await Assert.That(response.Content.Headers.ContentType!.MediaType).IsEqualTo("application/problem+json");
            await Assert.That((await response.Content.ReadAsStringAsync()).Contains("do-not-reflect", StringComparison.Ordinal)).IsFalse();
        }
        await Assert.That((await client.GetByteArrayAsync(ManifestUrl)).SequenceEqual(before)).IsTrue();
    }

    [Test]
    public async Task Portability_AuditFailureAfterSavedIdentityRollsBackDatabaseTransaction()
    {
        using var factory = new OnboardingWebApplicationFactory();
        using var failing = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IOutboxRepository>();
            IOutboxRepository outbox = Substitute.For<IOutboxRepository>();
            outbox.CreateRange(Arg.Any<IReadOnlyCollection<OutboxMessage>>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromException<IReadOnlyList<OutboxMessage>>(new IOException("Audit persistence unavailable.")));
            services.AddScoped(_ => outbox);
        }));
        using HttpClient client = failing.CreateClient();
        Guid actor = await SeedPlatformAdminAsync(factory);
        await SeedPortableIdentityAsync(factory);
        AuthorizeOperatorClient(client, actor);
        byte[] before = await client.GetByteArrayAsync(ManifestUrl);
        OperatorIdentityManifest manifest = OperatorIdentityManifestJson.Parse(before);
        using HttpResponseMessage failed = await client.PostAsJsonAsync(ManifestUrl + "/import",
            new ImportInstanceOperatorIdentityCommand(manifest, manifest.RevisionHash));
        await Assert.That(failed.StatusCode).IsEqualTo(HttpStatusCode.InternalServerError);
        await Assert.That((await client.GetByteArrayAsync(ManifestUrl)).SequenceEqual(before)).IsTrue();
    }

    private static void AuthorizeOperatorClient(HttpClient client, Guid actor)
    {
        client.DefaultRequestHeaders.Remove(TestAuthHandler.AuthHeaderName);
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(actor, "Instance Admin",
                ("iss", OnboardingWebApplicationFactory.Issuer), ("idp", "keycloak")));
    }

    private static async Task SeedPortableIdentityAsync(OnboardingWebApplicationFactory factory)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        InstanceOperatorIdentityService identity = scope.ServiceProvider.GetRequiredService<InstanceOperatorIdentityService>();
        InstanceOperatorIdentityDocument current = await identity.GetCurrentAsync();
        await identity.SaveAsync(new InstanceOperatorIdentitySettings
        {
            PublicName = "Independent Operator", LegalName = "Independent ASBL",
            OperatorKindCode = "registered_organization", JurisdictionCountryCode = "BE",
            RegistrationIdentifier = "BE0123456789", PublicContactEmail = "contact@example.test",
            WebsiteUrl = "https://example.test", LegalNoticeUrl = "https://example.test/legal",
            TermsUrl = "https://example.test/terms", PrivacyUrl = "https://example.test/privacy",
            OfficialOrigin = "https://example.test"
        }, current.Settings?.Revision);
    }
}
