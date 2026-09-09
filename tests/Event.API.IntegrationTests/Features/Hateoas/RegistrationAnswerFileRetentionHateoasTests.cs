using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Hateoas;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Features.Hateoas;

public sealed class RegistrationAnswerFileRetentionHateoasTests
{
    private static readonly DateTime Deadline = new(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc);
    private const string FileName = "Alice-medical-answer.pdf";
    private const string ReleaseReason = "Approved by the review operator";
    private const string BaseUrl = "/api/registration-answer-files";

    [Test]
    [Arguments("live", false, true)]
    [Arguments("quarantined", false, true)]
    [Arguments("expired", false, false)]
    [Arguments("historical", false, false)]
    [Arguments("missing-submission", false, false)]
    [Arguments("missing-order", false, false)]
    [Arguments("foreign-submission-tenant", false, false)]
    [Arguments("foreign-submission-event", false, false)]
    [Arguments("foreign-order-tenant", false, false)]
    [Arguments("foreign-order-event", false, false)]
    [Arguments("account", false, true)]
    [Arguments("live", true, true)]
    [Arguments("expired", true, false)]
    [Arguments("historical", true, false)]
    [Arguments("missing-order", true, false)]
    [Arguments("account", true, true)]
    public async Task MetadataUsesOrderAuthorityWithoutChangingHeldFilesOrReleaseEvidence(
        string scenario, bool release, bool allowed)
    {
        var clock = new Clock { Now = scenario is "expired" or "account" ? Deadline : Deadline.AddTicks(-1) };
        await using var factory = new FileFactory(clock);
        using var client = factory.CreateClient();
        var seed = await SeedAsync(factory, scenario, released: !release && scenario != "quarantined");

        using var response = await SendAsync(client, seed, release);
        await AssertResponseAsync(response, allowed, released: release || scenario != "quarantined");
        if (scenario == "quarantined")
        {
            using var scope = factory.Services.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IStorageObjectRepository>();
            await Assert.That(await repository.IsRegistrationAnswerFileQuarantinedAsync(seed.StorageId, CancellationToken.None)).IsTrue();
            var reader = scope.ServiceProvider.GetRequiredService<IStorageObjectContentReader>();
            await Assert.That(await reader.OpenAsync(seed.StorageId, false, CancellationToken.None)).IsNull();
        }
        if (release)
        {
            using var repeated = await SendAsync(client, seed, true, reason: "A later reason must not replace the audit");
            await AssertResponseAsync(repeated, allowed, released: true);
        }
        await AssertRetainedAsync(factory, seed, released: release || scenario != "quarantined",
            release ? clock.Now : Deadline.AddDays(-1));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MinimalResponsesRedactAtExactExpiry(bool release)
    {
        var clock = new Clock { Now = Deadline };
        await using var factory = new FileFactory(clock);
        using var client = factory.CreateClient();
        var seed = await SeedAsync(factory, "live", released: !release);
        using var response = await SendAsync(client, seed, release, minimal: true);
        await AssertResponseAsync(response, false, released: true, minimal: true);
    }

    [Test]
    [Arguments("preparation", false)]
    [Arguments("preparation", true)]
    [Arguments("hal", false)]
    [Arguments("hal", true)]
    public async Task ExpiryCrossedDuringRealPreparatoryOrHalAwaitRedactsFinalResponse(string boundary, bool release)
    {
        var clock = new Clock { Now = Deadline.AddTicks(-1) };
        var barrier = new Barrier();
        await using var factory = new FileFactory(clock, boundary, barrier);
        using var client = factory.CreateClient();
        var seed = await SeedAsync(factory, "live", released: !release);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task entered = barrier.Entered.Task;
        Task<HttpResponseMessage> pending = SendAsync(client, seed, release, minimal: boundary == "preparation");
        try
        {
            await entered.WaitAsync(timeout.Token);
            clock.Now = Deadline;
            barrier.Release.TrySetResult();
            using var response = await pending.WaitAsync(timeout.Token);
            await AssertResponseAsync(response, false, released: true, minimal: boundary == "preparation");
            await AssertRetainedAsync(factory, seed, true, release ? Deadline.AddTicks(-1) : Deadline.AddDays(-1));
        }
        finally { barrier.Release.TrySetResult(); }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OrdinaryUsersCannotReadOrReleaseAdministrativeFiles(bool release)
    {
        var clock = new Clock { Now = Deadline.AddTicks(-1) };
        await using var factory = new FileFactory(clock);
        using var client = factory.CreateClient();
        var seed = await SeedAsync(factory, "quarantined", false);
        using var request = new HttpRequestMessage(release ? HttpMethod.Post : HttpMethod.Get,
            $"{BaseUrl}/{seed.FileId}{(release ? "/release" : "")}");
        request.Headers.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(seed.UserId));
        if (release) request.Content = JsonContent.Create(new { reason = ReleaseReason });
        using var response = await client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await AssertRetainedAsync(factory, seed, false, Deadline.AddDays(-1));
    }

    private static async Task AssertResponseAsync(HttpResponseMessage response, bool allowed, bool released, bool minimal = false)
    {
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement resource = json.RootElement;
        await Assert.That(resource.GetProperty("safeDisplayName").GetString()).IsEqualTo(allowed ? FileName : string.Empty);
        if (!allowed) await Assert.That(resource.GetRawText()).DoesNotContain(FileName);
        await Assert.That(resource.TryGetProperty("releaseReason", out var reason) ? reason.GetString() : null)
            .IsEqualTo(released ? ReleaseReason : null);
        await Assert.That(resource.GetProperty("quarantineState").GetString()).IsEqualTo(released ? "released" : "quarantined");
        await Assert.That(resource.EnumerateObject().Select(value => value.Name).Except(new[]
        {
            "id", "registrationSubmissionId", "registrationFormFieldId", "storageObjectId", "safeDisplayName",
            "contentType", "extension", "size", "quarantineState", "scanStatus", "quarantinedAt", "releasedBy",
            "releasedAt", "releaseReason", "_links"
        })).IsEmpty();
        if (!minimal)
        {
            JsonElement links = resource.GetProperty("_links");
            await Assert.That(links.TryGetProperty("self", out _)).IsTrue();
            await Assert.That(links.TryGetProperty("storage-object", out _)).IsTrue();
            await Assert.That(links.TryGetProperty("release", out _)).IsEqualTo(!released);
        }
    }

    private static async Task AssertRetainedAsync(FileFactory factory, Seed seed, bool released, DateTime releasedAt)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var file = await db.RegistrationAnswerFiles.SingleAsync(value => value.Id == seed.FileId);
        var storage = await db.StorageObjects.SingleAsync(value => value.Id == seed.StorageId);
        await Assert.That(file.SafeDisplayName).IsEqualTo(FileName);
        await Assert.That(file.IsDeleted).IsFalse();
        await Assert.That(storage.SafeDisplayName).IsEqualTo(FileName);
        await Assert.That(storage.LifecycleState).IsEqualTo(StorageObjectLifecycleStates.Active);
        await Assert.That(file.IsReleased).IsEqualTo(released);
        var audits = await db.RegistrationAnswerFileReleases.Where(value => value.RegistrationAnswerFileId == seed.FileId).ToListAsync();
        await Assert.That(audits.Count).IsEqualTo(released ? 1 : 0);
        if (released)
        {
            await Assert.That(audits[0].Reason).IsEqualTo(ReleaseReason);
            await Assert.That(file.ReleasedAt).IsEqualTo(releasedAt);
            await Assert.That(file.ReleasedBy).IsEqualTo(seed.UserId);
        }
    }

    private static async Task<Seed> SeedAsync(FileFactory factory, string scenario, bool released)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var tenant = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
        DateTime created = Deadline.AddDays(-1);
        Guid eventId = Guid.CreateVersion7();
        bool account = scenario == "account";
        Guid orderTenantId = scenario == "foreign-order-tenant" ? Guid.CreateVersion7() : tenant.TenantId;
        Guid orderEventId = scenario == "foreign-order-event" ? Guid.CreateVersion7() : eventId;
        var order = RegistrationOrder.Create(orderTenantId, orderEventId, account ? tenant.UserId : null, null,
            BookingPartyTypeEnum.Individual, Guid.CreateVersion7(), RegistrationParticipationSnapshot.Create(
                Guid.CreateVersion7(), (int)ParticipationHandlingModeEnum.PlatformManaged,
                (int)AdvanceRegistrationObligationEnum.Required,
                (int)(account ? IdentityAccessModeEnum.AccountRequired : IdentityAccessModeEnum.GuestAllowed),
                GuestRecoveryPolicyEnum.EmailOptional), null,
            account ? null : CapabilityTokenHash.Create(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))),
            "EUR", created, created.AddHours(1), account || scenario == "historical" ? null : Deadline);
        Guid submissionTenantId = scenario == "foreign-submission-tenant" ? Guid.CreateVersion7() : tenant.TenantId;
        Guid submissionEventId = scenario == "foreign-submission-event" ? Guid.CreateVersion7() : eventId;
        var attempt = RegistrationAttempt.Create(submissionTenantId, submissionEventId, order.Id, Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            CapabilityTokenHash.Create(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))), null, null,
            created, created.AddHours(1));
        var submission = RegistrationSubmission.Create(attempt,
            RegistrationEvidenceHash.Create(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))), created,
            null, null, null, null);
        Guid storageId = Guid.CreateVersion7();
        var storage = new StorageObject
        {
            Id = storageId,
            TenantId = tenant.TenantId,
            Tenant = null!,
            FileTypeId = (int)FileTypeEnum.Document,
            FileType = null!,
            Uri = $"/api/storageobject/{storageId}/content",
            ObjectKey = $"tenants/{tenant.TenantId:N}/{storageId:N}.pdf",
            Provider = StorageProviders.Local,
            FullName = FileName,
            SafeDisplayName = FileName,
            Extension = "pdf",
            ContentType = "application/pdf",
            Size = 16,
            Visibility = StorageObjectVisibilities.AuthenticatedTenant,
            Purpose = StorageObjectPurposes.Document,
            LifecycleState = StorageObjectLifecycleStates.Active,
            CreatedBy = tenant.UserId,
            CreatedAt = created,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
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
        if (released) db.Add(file.ReleaseManually(tenant.UserId, ReleaseReason, created));
        // Legacy held files may have no storage ownership markers; the file's own lineage remains authoritative.
        storage.OwningResourceKind = null;
        storage.OwningResourceId = null;
        db.AddRange(attempt, storage, form, file);
        if (scenario != "missing-submission") db.Add(submission);
        if (scenario != "missing-order") db.Add(order);
        await db.SaveChangesAsync();
        return new(file.Id, storageId, tenant.TenantId, tenant.UserId);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, Seed seed, bool release, bool minimal = false, string reason = ReleaseReason)
    {
        using var request = new HttpRequestMessage(release ? HttpMethod.Post : HttpMethod.Get,
            $"{BaseUrl}/{seed.FileId}{(release ? "/release" : "")}");
        request.Headers.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(seed.UserId, "File administrator",
                (ClaimTypes.Role, "Admin"), ("explore:admin:tenant", seed.TenantId.ToString())));
        if (minimal) request.Headers.Add("Prefer", "return=minimal");
        if (release) request.Content = JsonContent.Create(new { reason });
        return await client.SendAsync(request);
    }

    private sealed record Seed(Guid FileId, Guid StorageId, Guid TenantId, Guid UserId);

    private sealed class Clock : TimeProvider
    {
        public DateTime Now { get; set; }
        public override DateTimeOffset GetUtcNow() => new(Now);
    }

    private sealed class Barrier
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task WaitAsync(CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class FileFactory(Clock clock, string? boundary = null, Barrier? barrier = null)
        : AuthenticatedWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
                if (boundary == "preparation")
                {
                    services.RemoveAll<IRegistrationAnswerFileRepository>();
                    services.AddScoped<IRegistrationAnswerFileRepository>(provider => new RepositoryBarrier(
                        ActivatorUtilities.CreateInstance<RegistrationAnswerFileRepository>(provider), barrier!));
                }
                if (boundary == "hal")
                {
                    services.RemoveAll<IHateoasAuthorizationEvaluator>();
                    services.AddScoped<IHateoasAuthorizationEvaluator>(provider => new HalBarrier(
                        ActivatorUtilities.CreateInstance<HateoasAuthorizationEvaluator>(provider), barrier!));
                }
            });
        }
    }

    private sealed class RepositoryBarrier(RegistrationAnswerFileRepository inner, Barrier barrier)
        : IRegistrationAnswerFileRepository
    {
        public Task<RegistrationAnswerFile?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
            inner.GetAsync(tenantId, id, cancellationToken);

        public Task<RegistrationOrder?> GetOrderAsync(RegistrationAnswerFile file, CancellationToken cancellationToken) =>
            inner.GetOrderAsync(file, cancellationToken);

        public async Task<RegistrationAnswerFileRelease?> GetReleaseAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
        {
            var release = await inner.GetReleaseAsync(tenantId, id, cancellationToken);
            await barrier.WaitAsync(cancellationToken);
            return release;
        }

        public Task<RegistrationAnswerFileReleaseResult?> ReleaseAsync(
            Guid tenantId, Guid id, Guid releasedBy, string reason, DateTime releasedAt, CancellationToken cancellationToken) =>
            inner.ReleaseAsync(tenantId, id, releasedBy, reason, releasedAt, cancellationToken);
    }

    private sealed class HalBarrier(HateoasAuthorizationEvaluator inner, Barrier barrier) : IHateoasAuthorizationEvaluator
    {
        public async Task<IReadOnlyList<bool>> AreLinksAllowedAsync(
            IReadOnlyList<LinkDefinition> definitions, ClaimsPrincipal? user, HttpContext httpContext)
        {
            var decisions = await inner.AreLinksAllowedAsync(definitions, user, httpContext);
            await barrier.WaitAsync(httpContext.RequestAborted);
            return decisions;
        }
    }
}
