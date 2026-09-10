using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Models.Storage;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features.Hateoas;

public sealed class StorageRetentionHateoasTests
{
    private static readonly DateTime Deadline = new(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc);
    private const string BaseUrl = "/api/storageobject";

    [Test]
    [Arguments("live-csv", true, false)]
    [Arguments("expired-csv", false, false)]
    [Arguments("missing-order-bound", false, false)]
    [Arguments("missing-artifact-bound", false, false)]
    [Arguments("missing-lineage", false, false)]
    [Arguments("foreign-lineage", false, false)]
    [Arguments("account-csv", true, true)]
    [Arguments("ordinary", true, true)]
    [Arguments("live-answer", true, false)]
    [Arguments("expired-answer", false, false)]
    [Arguments("quarantined-answer", false, false)]
    public async Task MetadataLinksAgreeWithContentBoundaryWithoutChangingWireShape(
        string scenario, bool contentAllowed, bool presignedAllowed)
    {
        var clock = new Clock { Now = Deadline.AddTicks(-1) };
        await using var factory = new StorageFactory(clock);
        using var client = factory.CreateClient();
        var (id, userId) = await SeedAsync(factory, scenario);
        if (scenario.StartsWith("expired-", StringComparison.Ordinal)) clock.Now = Deadline;

        using var detail = await GetAsync(client, $"{BaseUrl}/{id}", userId);
        using var list = await GetAsync(client, BaseUrl, userId);
        await Assert.That(detail.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(list.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var detailJson = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        using var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        JsonElement item = listJson.RootElement.GetProperty("_embedded").GetProperty("items")
            .EnumerateArray().Single(value => value.GetProperty("id").GetGuid() == id);
        await AssertLinksAsync(detailJson.RootElement, contentAllowed, presignedAllowed);
        await AssertLinksAsync(item, contentAllowed, false);
        await AssertNamesAsync(detailJson.RootElement, contentAllowed);
        await AssertNamesAsync(item, contentAllowed);
        await Assert.That(detailJson.RootElement.EnumerateObject().Select(value => value.Name)
            .Except(new[] { "id", "fileTypeId", "fileTypeFullName", "fileTypeMasterCode", "uri", "provider",
                "fullName", "safeDisplayName", "extension", "contentType", "sha256Checksum", "size", "visibility",
                "purpose", "lifecycleState", "owningResourceKind", "owningResourceId", "tenantId", "tenantFullName",
                "actorId", "actorDisplayName", "isDeleted", "deletedAt", "quarantinedAt", "quarantineReason", "_links" })).IsEmpty();
        await Assert.That(item.EnumerateObject().Select(value => value.Name)
            .Except(new[] { "id", "fileTypeId", "fileTypeFullName", "uri", "provider", "fullName", "safeDisplayName",
                "extension", "contentType", "size", "visibility", "purpose", "lifecycleState", "tenantId", "_links" })).IsEmpty();

        using var content = await GetAsync(client, $"{BaseUrl}/{id}/content", userId);
        using var presigned = await GetAsync(client, $"{BaseUrl}/{id}/presigned-url", userId);
        await Assert.That(content.StatusCode).IsEqualTo(contentAllowed ? HttpStatusCode.OK : HttpStatusCode.NotFound);
        await Assert.That(presigned.StatusCode).IsEqualTo(presignedAllowed ? HttpStatusCode.OK : HttpStatusCode.NotFound);
        if (contentAllowed) await Assert.That(await content.Content.ReadAsStringAsync()).IsEqualTo("retained content");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExpiryDuringHalAuthorizationRemovesContentAtFinalProjection(bool collection)
    {
        var clock = new Clock { Now = Deadline.AddTicks(-1) };
        var authorization = new AuthorizationBarrier();
        await using var factory = new StorageFactory(clock) { AuthorizationProviderOverride = authorization };
        using var client = factory.CreateClient();
        var (id, userId) = await SeedAsync(factory, "live-csv");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task signal = authorization.Entered.Task;
        Task<HttpResponseMessage> pending = GetAsync(client, collection ? BaseUrl : $"{BaseUrl}/{id}", userId);
        try
        {
            await signal.WaitAsync(timeout.Token);
            clock.Now = Deadline;
            authorization.Release.TrySetResult();
            using var response = await pending.WaitAsync(timeout.Token);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            JsonElement resource = collection
                ? json.RootElement.GetProperty("_embedded").GetProperty("items").EnumerateArray()
                    .Single(value => value.GetProperty("id").GetGuid() == id)
                : json.RootElement;
            await AssertLinksAsync(resource, false, false);
            await AssertNamesAsync(resource, false);
        }
        finally { authorization.Release.TrySetResult(); }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExpiredMetadataMinimalResponseStillRedactsFilenames(bool collection)
    {
        var clock = new Clock { Now = Deadline };
        await using var factory = new StorageFactory(clock);
        using var client = factory.CreateClient();
        var (id, userId) = await SeedAsync(factory, "expired-csv");
        using var response = await GetAsync(client, collection ? BaseUrl : $"{BaseUrl}/{id}", userId, minimal: true);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement resource = collection
            ? json.RootElement.GetProperty("_embedded").GetProperty("items").EnumerateArray()
                .Single(value => value.GetProperty("id").GetGuid() == id)
            : json.RootElement;
        await AssertNamesAsync(resource, false);
    }

    private static async Task AssertNamesAsync(JsonElement resource, bool allowed)
    {
        await Assert.That(resource.GetProperty("fullName").GetString()).IsEqualTo(allowed ? "retained.csv" : string.Empty);
        await Assert.That(resource.GetProperty("safeDisplayName").GetString()).IsEqualTo(allowed ? "retained.csv" : string.Empty);
        if (!allowed)
        {
            await Assert.That(resource.GetProperty("uri").GetString()).IsEqualTo(string.Empty);
            await Assert.That(resource.GetRawText()).DoesNotContain("retained.csv");
        }
    }

    private static async Task AssertLinksAsync(JsonElement resource, bool content, bool presigned)
    {
        JsonElement links = resource.GetProperty("_links");
        await Assert.That(links.TryGetProperty("content", out _)).IsEqualTo(content);
        await Assert.That(links.TryGetProperty("presigned-download", out _)).IsEqualTo(presigned);
        await Assert.That(links.TryGetProperty("self", out _)).IsTrue();
        await Assert.That(links.TryGetProperty("edit", out _)).IsTrue();
        await Assert.That(links.TryGetProperty("delete", out _)).IsTrue();
    }

    private static async Task<(Guid Id, Guid UserId)> SeedAsync(StorageFactory factory, string scenario)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var tenant = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
        DateTime created = Deadline.AddDays(-1);
        Guid eventId = Guid.CreateVersion7();
        bool account = scenario == "account-csv";
        var order = RegistrationOrder.Create(tenant.TenantId, eventId, account ? tenant.UserId : null, null,
            BookingPartyTypeEnum.Individual, Guid.CreateVersion7(), RegistrationParticipationSnapshot.Create(
                Guid.CreateVersion7(), (int)ParticipationHandlingModeEnum.PlatformManaged,
                (int)AdvanceRegistrationObligationEnum.Required,
                (int)(account ? IdentityAccessModeEnum.AccountRequired : IdentityAccessModeEnum.GuestAllowed),
                GuestRecoveryPolicyEnum.EmailOptional), null,
            account ? null : CapabilityTokenHash.Create(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))),
            "EUR", created, created.AddHours(1), account || scenario == "missing-order-bound" ? null : Deadline);
        var attempt = RegistrationAttempt.Create(tenant.TenantId, eventId, order.Id, Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            CapabilityTokenHash.Create(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))), null, null,
            created, created.AddHours(1));
        var submission = RegistrationSubmission.Create(attempt,
            RegistrationEvidenceHash.Create(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))), created,
            null, null, null, null);
        if (scenario == "foreign-lineage")
        {
            var other = await TenantScenarioSeed.SeedSecondaryTenantWithUserAsync(db, "Other storage tenant");
            submission.TenantId = other.TenantId;
        }
        Guid id = Guid.CreateVersion7();
        var storage = new StorageObject
        {
            Id = id,
            TenantId = tenant.TenantId,
            Tenant = null!,
            FileTypeId = (int)FileTypeEnum.Document,
            FileType = null!,
            Uri = $"{BaseUrl}/{id}/content",
            ObjectKey = $"tenants/{tenant.TenantId:N}/{id:N}.csv",
            Provider = StorageProviders.Local,
            FullName = "retained.csv",
            SafeDisplayName = "retained.csv",
            Extension = "csv",
            ContentType = "text/csv",
            Size = 16,
            Visibility = StorageObjectVisibilities.AuthenticatedTenant,
            Purpose = StorageObjectPurposes.Document,
            LifecycleState = StorageObjectLifecycleStates.Active,
            CreatedBy = tenant.UserId,
            CreatedAt = created,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        if (scenario.EndsWith("-answer", StringComparison.Ordinal))
        {
            var form = RegistrationForm.Create(tenant.TenantId, eventId, "native", "retention", "Retention", created);
            var version = RegistrationFormVersion.Create(form, 1, "en", null, null, created);
            var section = RegistrationFormSection.Create(Guid.CreateVersion7(), version, 1, "Files", created);
            version.AddSection(section);
            var field = RegistrationFormField.Create(Guid.CreateVersion7(), section, 1, "native", "document", "Document",
                RegistrationFieldTypeEnum.File, (int)RegistrationRetentionPolicyEnum.LegalHold,
                RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers, false, false, created);
            version.AddField(section, field);
            form.AddVersion(version);
            var file = RegistrationAnswerFile.Create(tenant.TenantId, submission.Id, field, storage, created);
            if (scenario != "quarantined-answer")
                db.Add(file.ReleaseManually(tenant.UserId, "Approved for operational use", created));
            // Existing released files may predate storage ownership markers.
            storage.OwningResourceKind = null;
            storage.OwningResourceId = null;
            db.AddRange(form, file);
        }
        else if (scenario != "ordinary")
        {
            storage.OwningResourceKind = "registration_submission_sink";
            storage.OwningResourceId = scenario == "missing-lineage" ? Guid.CreateVersion7() : submission.Id;
            storage.RegistrationContentRetentionUntilUtc = scenario == "missing-artifact-bound" ? null
                : account ? created : Deadline;
        }
        db.AddRange(order, attempt, submission, storage);
        await db.SaveChangesAsync();
        return (id, tenant.UserId);
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string path, Guid userId, bool minimal = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(userId));
        if (minimal) request.Headers.Add("Prefer", "return=minimal");
        return await client.SendAsync(request);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTime Now { get; set; }
        public override DateTimeOffset GetUtcNow() => new(Now);
    }

    private sealed class StorageFactory(Clock clock) : AuthenticatedWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            AuthorizationProviderOverride ??= new StubAuthorizationProvider();
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
                var provider = Substitute.For<IFileStorageProvider>();
                provider.OpenReadAsync(Arg.Any<FileStorageReadInput>(), Arg.Any<CancellationToken>())
                    .Returns(_ => new FileStorageReadResult(new MemoryStream("retained content"u8.ToArray()),
                        "text/csv", 16, new DateTimeOffset(Deadline.AddDays(-1))));
                var resolver = Substitute.For<IFileStorageProviderResolver>();
                resolver.GetRequired(StorageProviders.Local).Returns(provider);
                services.RemoveAll<IFileStorageProviderResolver>();
                services.AddSingleton(resolver);
                var storage = Substitute.For<IObjectStorageService>();
                storage.GeneratePresignedDownloadUrl(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>())
                    .Returns("https://storage.example.test/download");
                services.RemoveAll<IObjectStorageService>();
                services.AddSingleton(storage);
            });
        }
    }

    private sealed class AuthorizationBarrier : IAuthorizationProvider
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<AuthorizationDecision> AuthorizeAsync(AuthorizationRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(AuthorizationDecision.Allow(AuthorizationProviderMetadata.Local));

        public async Task<IReadOnlyList<AuthorizationDecision>> AuthorizeBatchAsync(
            IReadOnlyList<AuthorizationRequest> requests, CancellationToken cancellationToken = default)
        {
            if (requests.Any(request => request.Capability.Action == AuthorizationActions.StorageObjects.Download))
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return requests.Select(_ => AuthorizationDecision.Allow(AuthorizationProviderMetadata.Local)).ToArray();
        }
    }
}
