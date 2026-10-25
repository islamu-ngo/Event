using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Benchmarks.Benchmarks;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Seed;
using Microsoft.EntityFrameworkCore;

namespace Event.Benchmarks.Api;

internal static class BenchmarkApiSeedData
{
    internal static readonly Guid BenchmarkUserId = Guid.Parse("018f6d10-7b7b-7f20-8c61-3c3e7f1b6a11");

    private static readonly Guid BenchmarkActorId = Guid.Parse("018f6d10-7b7b-7f20-8c61-3c3e7f1b6a12");
    private static readonly Guid BenchmarkEventIdStart = Guid.Parse("018f6d10-7b7b-7f20-8c61-3c3e7f1b7000");
    private static readonly DateTime SeedTimestamp = new(2026, 5, 28, 0, 0, 0, DateTimeKind.Utc);
    private const string TimezoneId = "Europe/Brussels";
    private const int EventCount = 24;

    internal static async Task<NativeBenchmarkDataset> SeedNativeAsync(HttpClient client,
        PostgreSqlApiBenchmarkHostFactory factory, string runId, CancellationToken cancellationToken)
    {
        using (var initial = await ReadNativeAsync(client, HttpMethod.Post, "/api/auth/local/login", new
        {
            identifier = factory.BootstrapIdentifier, password = factory.BootstrapPassword
        }, null, cancellationToken))
        {
            if (initial.RootElement.GetProperty("success").GetBoolean())
                throw new InvalidOperationException("benchmark_native_replacement_required");
            string challenge = initial.RootElement.GetProperty("replacementChallenge").GetProperty("token").GetString()
                ?? throw new InvalidOperationException("benchmark_native_challenge_missing");
            using var replaced = await SendNativeAsync(client, HttpMethod.Post, "/api/auth/local/credential-replacement",
                new { newPassword = factory.ReplacementPassword }, challenge, HttpStatusCode.NoContent, cancellationToken);
        }
        var session = await LoginNativeAsync(client, factory, cancellationToken);
        string slug = "benchmark-" + runId;
        Guid tenantId;
        using (var tenant = await ReadNativeAsync(client, HttpMethod.Post, "/api/Tenant", new
        {
            fullName = "Benchmark native community", slug, isActive = true, assignCurrentUserAsTenantAdmin = true,
            directoryOperatorIdentity = new
            {
                publicName = "Benchmark native community", legalName = "Benchmark native community",
                operatorKindCode = "registered_organization", jurisdictionCountryCode = "BE",
                registrationIdentifier = runId, publicContactEmail = "benchmark@example.invalid",
                legalNoticeUrl = "https://example.invalid/legal", termsUrl = "https://example.invalid/terms",
                privacyUrl = "https://example.invalid/privacy"
            }
        }, session.Token, cancellationToken))
            tenantId = tenant.RootElement.GetProperty("id").GetGuid();
        if (tenantId == Guid.Empty)
            throw new InvalidOperationException("benchmark_native_tenant_missing");
        client.DefaultRequestHeaders.Add("X-Tenant-Slug", slug);
        using (var tenant = await ReadNativeAsync(client, HttpMethod.Get, $"/api/Tenant/{tenantId:D}", null,
            session.Token, cancellationToken))
            if (!tenant.RootElement.GetProperty("isActive").GetBoolean()
                || tenant.RootElement.GetProperty("slug").GetString() != slug)
                throw new InvalidOperationException("benchmark_native_tenant_not_active");

        var events = new Dictionary<Guid, string>();
        DateTimeOffset starts = DateTimeOffset.UtcNow.AddDays(7);
        for (int index = 1; index <= EventCount; index++)
        {
            session = await LoginNativeAsync(client, factory, cancellationToken);
            string title = $"Benchmark native event {index:D2}";
            Guid id;
            using (var created = await ReadNativeAsync(client, HttpMethod.Post, "/api/Event", new
            {
                title, slug = $"benchmark-api-event-{index:D2}", description = "Project-owned InformationOnly benchmark event.",
                eventFormatId = (int)EventFormatEnum.Digital, eventTimeZoneId = "UTC",
                participationConfiguration = new
                {
                    participationHandlingModeId = (int)ParticipationHandlingModeEnum.InformationOnly,
                    advanceRegistrationObligationId = 1
                },
                sessions = new[] { new { tempKey = $"session-{index:D2}", title = "Benchmark session",
                    startTime = starts, endTime = starts.AddHours(1), endTimeType = 0 } }
            }, session.Token, cancellationToken, HttpStatusCode.Created))
                id = created.RootElement.GetProperty("id").GetGuid();
            if (id == Guid.Empty || !events.TryAdd(id, title))
                throw new InvalidOperationException("benchmark_native_event_identity_invalid");
            using var detail = await ReadNativeAsync(client, HttpMethod.Get, $"/api/Event/{id:D}/management-detail",
                null, session.Token, cancellationToken);
            if (detail.RootElement.GetProperty("title").GetString() != title)
                throw new InvalidOperationException("benchmark_native_event_title_invalid");
            using var published = await ReadNativeAsync(client, HttpMethod.Post, $"/api/Event/{id:D}/publish", new
            {
                expectedConcurrencyStamp = detail.RootElement.GetProperty("concurrencyStamp").GetGuid()
            }, session.Token, cancellationToken);
            if (published.RootElement.GetProperty("id").GetGuid() != id)
                throw new InvalidOperationException("benchmark_native_publication_identity_invalid");
        }
        return new NativeBenchmarkDataset(tenantId, slug, session.UserId, events);
    }

    internal static async Task<NativeBenchmarkSession> LoginNativeAsync(HttpClient client,
        PostgreSqlApiBenchmarkHostFactory factory, CancellationToken cancellationToken)
    {
        using var response = await ReadNativeAsync(client, HttpMethod.Post, "/api/auth/local/login", new
        {
            identifier = factory.BootstrapIdentifier, password = factory.ReplacementPassword
        }, null, cancellationToken);
        var root = response.RootElement;
        if (!root.GetProperty("success").GetBoolean())
            throw new InvalidOperationException("benchmark_native_login_failed");
        string token = root.GetProperty("token").GetString()
            ?? throw new InvalidOperationException("benchmark_native_token_missing");
        Guid subject = root.GetProperty("userId").GetGuid();
        var expires = root.GetProperty("expiresAt").GetDateTimeOffset();
        if (subject == Guid.Empty || expires <= DateTimeOffset.UtcNow.AddSeconds(30))
            throw new InvalidOperationException("benchmark_native_session_window_invalid");
        return new NativeBenchmarkSession(subject, token, expires);
    }

    internal static async Task ValidateNativeAsync(HttpClient anonymous, HttpClient authenticated,
        NativeBenchmarkDataset dataset, NativeBenchmarkSession session, CancellationToken cancellationToken)
    {
        if (session.UserId != dataset.UserId)
            throw new InvalidOperationException("benchmark_native_subject_changed");
        foreach (var scenario in ApiBenchmarkScenario.ReadScenarios)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, scenario.Path);
            if (scenario.UseMinimalResponse)
                request.Headers.Add("Prefer", "return=minimal");
            if (scenario.RequiresAuthentication)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
            using var response = await (scenario.RequiresAuthentication ? authenticated : anonymous)
                .SendAsync(request, cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException("benchmark_native_warmup_status_invalid");
            using var body = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(cancellationToken));
            var root = body.RootElement;
            bool valid = scenario.Name switch
            {
                "event-list-minimal" or "event-list-hal" => root.GetProperty("snapshotCount").GetInt32() == EventCount
                    && ValidNativePage(root, dataset, discovery: true),
                "my-events-minimal" => root.GetProperty("totalCount").GetInt32() == EventCount
                    && ValidNativePage(root, dataset, discovery: false),
                "lookup-category-types" => root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0
                    && root.EnumerateArray().All(row => !string.IsNullOrWhiteSpace(row.GetProperty("fullName").GetString())
                        && row.GetProperty("categories").ValueKind == JsonValueKind.Array),
                "system-onboarding-status" => !root.GetProperty("requiresOnboarding").GetBoolean()
                    && root.GetProperty("deploymentMode").GetString() == "MultiTenant",
                "event-creation-context" => root.GetProperty("canCreate").GetBoolean()
                    && root.GetProperty("allowPersonalPublishing").GetBoolean()
                    && root.GetProperty("publisherOptions").EnumerateArray().Any(option =>
                        option.GetProperty("publisherMode").GetString() == "personal"
                        && option.GetProperty("canPublish").GetBoolean()),
                _ => false
            };
            if (scenario.Name == "event-list-hal")
                valid &= root.GetProperty("_links").TryGetProperty("self", out _);
            if (!valid)
                throw new InvalidOperationException("benchmark_native_response_invariant_failed");
        }
        foreach (var (id, title) in dataset.Events)
        {
            using var detail = await ReadNativeAsync(anonymous, HttpMethod.Get, $"/api/Event/{id:D}", null, null, cancellationToken);
            if (detail.RootElement.GetProperty("id").GetGuid() != id
                || detail.RootElement.GetProperty("title").GetString() != title)
                throw new InvalidOperationException("benchmark_native_settled_event_invalid");
        }
    }

    private static bool ValidNativePage(JsonElement root, NativeBenchmarkDataset dataset, bool discovery)
    {
        var items = root.GetProperty("_embedded").GetProperty("items");
        if (items.GetArrayLength() != 20)
            return false;
        var ids = new HashSet<Guid>();
        foreach (var item in items.EnumerateArray())
        {
            var row = discovery ? item.GetProperty("event") : item;
            Guid id = row.GetProperty("id").GetGuid();
            if (!ids.Add(id) || !dataset.Events.TryGetValue(id, out string? title)
                || row.GetProperty("title").GetString() != title)
                return false;
        }
        return true;
    }

    private static async Task<JsonDocument> ReadNativeAsync(HttpClient client, HttpMethod method, string path,
        object? body, string? token, CancellationToken cancellationToken, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await SendNativeAsync(client, method, path, body, token, expected, cancellationToken);
        return JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(cancellationToken));
    }

    private static async Task<HttpResponseMessage> SendNativeAsync(HttpClient client, HttpMethod method, string path,
        object? body, string? token, HttpStatusCode expected, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (method != HttpMethod.Get)
            request.Headers.Add("Idempotency-Key", Guid.CreateVersion7().ToString("N"));
        var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == expected)
            return response;
        response.Dispose();
        throw new InvalidOperationException("benchmark_native_setup_status_invalid");
    }

    public static async Task SeedAsync(ExploreDbContext globalContext, ITenantExploreDbContextFactory tenants,
        CancellationToken cancellationToken)
    {
        await EnsureTenantAsync(globalContext, cancellationToken);
        await EnsureBenchmarkUserAndActorAsync(globalContext, cancellationToken);
        await using var context = await tenants.CreateDbContextAsync(SeedIds.DefaultTenantId, cancellationToken);
        if (await context.Events.AnyAsync(evt => EF.Functions.Like(evt.Slug, "benchmark-api-event-%"), cancellationToken))
        {
            return;
        }

        context.Events.AddRange(CreateEvents());
        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureTenantAsync(ExploreDbContext context, CancellationToken cancellationToken)
    {
        if (await context.Tenants.AnyAsync(tenant => tenant.Id == SeedIds.DefaultTenantId, cancellationToken))
        {
            return;
        }

        context.Tenants.Add(new Tenant
        {
            Id = SeedIds.DefaultTenantId,
            FullName = "Benchmark API Tenant",
            Slug = "benchmark-api",
            Description = "Tenant used by Event.Benchmarks API endpoint scenarios.",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
            CreatedAt = SeedTimestamp
        });

        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureBenchmarkUserAndActorAsync(ExploreDbContext context, CancellationToken cancellationToken)
    {
        var user = await context.Users
            .SingleOrDefaultAsync(candidate => candidate.Id == BenchmarkUserId, cancellationToken);
        if (user is null)
        {
            user = new User
            {
                Id = BenchmarkUserId,
                EmailVerified = true,
                CreatedAt = SeedTimestamp,
                Pii = new UserPii
                {
                    Email = "benchmark-api-user@example.test",
                    FirstName = "Benchmark",
                    LastName = "User"
                }
            };
            context.Users.Add(user);
            await context.SaveChangesAsync(cancellationToken);
        }

        if (await context.Actors.AnyAsync(actor => actor.UserId == user.Id, cancellationToken))
        {
            return;
        }

        var actor = new Actor
        {
            Id = BenchmarkActorId,
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            UserId = BenchmarkUserId,
            CreatedAt = SeedTimestamp,
            Pii = new ActorPii { DisplayName = "Benchmark API Organizer" }
        };
        var identity = new AtprotoIdentity(Explore.Domain.ValueObjects.AtprotoDid.Parse("did:plc:benchmark-api-organizer"))
        {
            Id = Guid.CreateVersion7(),
            ActorId = actor.Id,
            Actor = actor,

            Handle = "benchmark-api-organizer",
            PdsHost = "https://pds.benchmark.example.test",
            IsActive = true,
            LastResolvedAt = SeedTimestamp,
            CreatedAt = SeedTimestamp
        };

        context.AddRange(actor, identity);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static IEnumerable<Explore.Domain.Event> CreateEvents()
    {
        for (var index = 0; index < EventCount; index++)
        {
            Guid eventId = IncrementGuid(BenchmarkEventIdStart, index);
            var sessionStartUtc = DateTimeOffset.UtcNow.AddDays(index + 1).AddHours(index % 6);
            var sessionDate = DateOnly.FromDateTime(sessionStartUtc.UtcDateTime);
            bool requiresRegistration = index % 3 == 0;

            yield return new Explore.Domain.Event(EventStatusEnum.Published)
            {
                Id = eventId,
                Title = $"Benchmark API Event {index + 1:00}",
                Subtitle = "Representative benchmark event",
                Description = "Seeded by Event.Benchmarks so API list benchmarks serialize non-empty event payloads.",
                Content = "This deterministic benchmark event gives API endpoint benchmarks realistic data volume without relying on Development seed data.",
                Slug = $"benchmark-api-event-{index + 1:00}",
                PublicCode = FormattableString.Invariant($"bench{index + 1:D7}"),
                ActorId = BenchmarkActorId,
                Actor = null!,
                EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
                OrganizerActorId = BenchmarkActorId,
                TenantId = SeedIds.DefaultTenantId,
                Tenant = null!,
                EventStatus = null!,
                VisibilityTypeId = (int)VisibilityTypeEnum.Public,
                VisibilityType = null!,
                EventFormatId = (int)EventFormatEnum.Local,
                EventFormat = null!,
                FirstSessionDate = sessionDate,
                LastSessionDate = sessionDate,
                FirstSessionStartUtc = sessionStartUtc,
                LastSessionStartUtc = sessionStartUtc.AddHours(2),
                Timezone = TimezoneId,
                EventTimeZoneId = TimezoneId,
                TotalViews = index * 17,
                ParticipationConfiguration = EventParticipationConfiguration.Create(
                    eventId,
                    SeedIds.DefaultTenantId,
                    requiresRegistration
                        ? (int)ParticipationHandlingModeEnum.PlatformManaged
                        : (int)ParticipationHandlingModeEnum.InformationOnly,
                    requiresRegistration
                        ? (int)AdvanceRegistrationObligationEnum.Required
                        : (int)AdvanceRegistrationObligationEnum.NotApplicable,
                    requiresRegistration ? (int)IdentityAccessModeEnum.AccountRequired : null,
                    guestRecoveryPolicy: null,
                    SeedTimestamp),
                RegistrationPolicyId = requiresRegistration ? (int)EventRegistrationPolicyEnum.SessionSelectionOnly : null,
                SessionCount = 1,
                CreatedAt = SeedTimestamp.AddMinutes(index),
                CreatedBy = BenchmarkUserId,
                ConcurrencyStamp = IncrementGuid(BenchmarkEventIdStart, index + EventCount)
            };
        }
    }

    private static Guid IncrementGuid(Guid baseGuid, int offset)
    {
        var bytes = baseGuid.ToByteArray();
        var value = BitConverter.ToInt32(bytes, 0);
        BitConverter.GetBytes(value + offset).CopyTo(bytes, 0);
        return new Guid(bytes);
    }
}

internal sealed class NativeBenchmarkSession(Guid userId, string token, DateTimeOffset expiresAt)
{
    internal Guid UserId { get; } = userId;
    internal string Token { get; } = token;
    internal DateTimeOffset ExpiresAt { get; } = expiresAt;
}

internal sealed class NativeBenchmarkDataset(Guid tenantId, string tenantSlug, Guid userId,
    IReadOnlyDictionary<Guid, string> events)
{
    internal Guid TenantId { get; } = tenantId;
    internal string TenantSlug { get; } = tenantSlug;
    internal Guid UserId { get; } = userId;
    internal IReadOnlyDictionary<Guid, string> Events { get; } = events;
}
