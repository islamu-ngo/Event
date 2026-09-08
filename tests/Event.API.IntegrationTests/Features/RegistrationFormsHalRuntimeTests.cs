using System.Net;
using System.Text;
using System.Text.Json;
using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.Services.Registration;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Schema;
using Explore.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using TUnit.Core.Interfaces;

namespace Event.Api.IntegrationTests.Features;

[ClassDataSource<RegistrationFormsRealRuntimeFixture>(Shared = SharedType.PerClass)]
[NotInParallel("RealRuntimeDb")]
public sealed class RegistrationFormsHalRuntimeTests(RegistrationFormsRealRuntimeFixture fixture)
{
    [Test]
    public async Task RealAuthoringStackAtomicallyReordersSectionsAndFields()
    {
        RegistrationFormsRuntimeScenario scenario = await fixture.SeedAsync();

        using HttpResponseMessage draft = await fixture.SendAsync(
            HttpMethod.Get,
            fixture.VersionRoute(scenario, scenario.ValidVersionId),
            scenario.OrganizerUserId);
        using JsonDocument draftDocument = await ReadJsonAsync(draft);
        JsonElement draftRoot = draftDocument.RootElement;
        JsonElement[] sections = [.. draftRoot.GetProperty("_embedded").GetProperty("sections").EnumerateArray()];
        Guid firstSectionId = sections[0].GetProperty("id").GetGuid();
        Guid secondSectionId = sections[1].GetProperty("id").GetGuid();
        JsonElement[] fields = [.. sections[0].GetProperty("_embedded").GetProperty("fields").EnumerateArray()];
        Guid firstFieldId = fields[0].GetProperty("id").GetGuid();
        Guid secondFieldId = fields[1].GetProperty("id").GetGuid();
        Guid foreignFieldId = sections[1].GetProperty("_embedded").GetProperty("fields")[0]
            .GetProperty("id").GetGuid();
        await Assert.That(draftRoot.GetProperty("_links").TryGetProperty("reorder-sections", out _)).IsTrue();
        await Assert.That(sections[0].GetProperty("_links").TryGetProperty("reorder-fields", out _)).IsTrue();

        using HttpResponseMessage reorderedSections = await fixture.ReorderSectionsAsync(
            scenario, scenario.OrganizerUserId, scenario.ValidStamp, [secondSectionId, firstSectionId]);
        using JsonDocument reorderedSectionsDocument = await ReadJsonAsync(reorderedSections);
        JsonElement reorderedSectionsRoot = reorderedSectionsDocument.RootElement;
        Guid sectionStamp = reorderedSectionsRoot.GetProperty("concurrencyStamp").GetGuid();
        JsonElement[] authoritativeSections = [.. reorderedSectionsRoot.GetProperty("_embedded")
            .GetProperty("sections").EnumerateArray()];
        await Assert.That(reorderedSections.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(authoritativeSections[0].GetProperty("id").GetGuid()).IsEqualTo(secondSectionId);
        await Assert.That(sectionStamp).IsNotEqualTo(scenario.ValidStamp);
        RegistrationFormPersistedOrderSnapshot afterSectionReorder =
            await fixture.GetOrderSnapshotAsync(scenario.ValidVersionId);

        using HttpResponseMessage stale = await fixture.ReorderFieldsAsync(
            scenario, scenario.OrganizerUserId, firstSectionId, scenario.ValidStamp, [secondFieldId, firstFieldId]);
        using JsonDocument staleProblem = await ReadJsonAsync(stale);
        await Assert.That(stale.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(staleProblem.RootElement.GetProperty("code").GetString()).IsEqualTo("concurrent_update");
        await fixture.AssertOrderSnapshotUnchangedAsync(scenario.ValidVersionId, afterSectionReorder);

        using HttpResponseMessage malformed = await fixture.ReorderFieldsAsync(
            scenario, scenario.OrganizerUserId, firstSectionId, sectionStamp, [firstFieldId, firstFieldId]);
        using JsonDocument malformedProblem = await ReadJsonAsync(malformed);
        await Assert.That(malformed.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(malformedProblem.RootElement.GetProperty("code").GetString())
            .IsEqualTo("registration_form_reorder_invalid");
        await fixture.AssertOrderSnapshotUnchangedAsync(scenario.ValidVersionId, afterSectionReorder);

        using HttpResponseMessage missing = await fixture.ReorderSectionsAsync(
            scenario, scenario.OrganizerUserId, sectionStamp, [secondSectionId]);
        using JsonDocument missingProblem = await ReadJsonAsync(missing);
        await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(missingProblem.RootElement.GetProperty("code").GetString())
            .IsEqualTo("registration_form_reorder_invalid");
        await fixture.AssertOrderSnapshotUnchangedAsync(scenario.ValidVersionId, afterSectionReorder);

        using HttpResponseMessage foreign = await fixture.ReorderFieldsAsync(
            scenario, scenario.OrganizerUserId, firstSectionId, sectionStamp, [firstFieldId, foreignFieldId]);
        using JsonDocument foreignProblem = await ReadJsonAsync(foreign);
        await Assert.That(foreign.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(foreignProblem.RootElement.GetProperty("code").GetString())
            .IsEqualTo("registration_form_reorder_invalid");
        await fixture.AssertOrderSnapshotUnchangedAsync(scenario.ValidVersionId, afterSectionReorder);

        using HttpResponseMessage reorderedFields = await fixture.ReorderFieldsAsync(
            scenario, scenario.OrganizerUserId, firstSectionId, sectionStamp, [secondFieldId, firstFieldId]);
        using JsonDocument reorderedFieldsDocument = await ReadJsonAsync(reorderedFields);
        JsonElement reorderedSection = reorderedFieldsDocument.RootElement.GetProperty("_embedded")
            .GetProperty("sections").EnumerateArray().Single(section => section.GetProperty("id").GetGuid() == firstSectionId);
        await Assert.That(reorderedFields.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(reorderedSection.GetProperty("_embedded").GetProperty("fields")[0]
            .GetProperty("id").GetGuid()).IsEqualTo(secondFieldId);
        RegistrationFormPersistedOrderSnapshot afterFieldReorder =
            await fixture.GetOrderSnapshotAsync(scenario.ValidVersionId);

        using HttpResponseMessage denied = await fixture.ReorderSectionsAsync(
            scenario, scenario.ContributorUserId,
            reorderedFieldsDocument.RootElement.GetProperty("concurrencyStamp").GetGuid(),
            [firstSectionId, secondSectionId]);
        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await fixture.AssertOrderSnapshotUnchangedAsync(scenario.ValidVersionId, afterFieldReorder);

        Guid fieldStamp = reorderedFieldsDocument.RootElement.GetProperty("concurrencyStamp").GetGuid();
        using HttpResponseMessage published = await fixture.PublishAsync(
            scenario, scenario.OrganizerUserId, scenario.ValidVersionId, fieldStamp);
        await Assert.That(published.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using HttpResponseMessage publishedVersion = await fixture.SendAsync(
            HttpMethod.Get, fixture.VersionRoute(scenario, scenario.ValidVersionId), scenario.OrganizerUserId);
        using JsonDocument publishedDocument = await ReadJsonAsync(publishedVersion);
        JsonElement publishedRoot = publishedDocument.RootElement;
        Guid publishedStamp = publishedRoot.GetProperty("concurrencyStamp").GetGuid();
        bool publishedHasSectionReorder = publishedRoot.TryGetProperty("_links", out JsonElement publishedLinks) &&
            publishedLinks.TryGetProperty("reorder-sections", out _);
        JsonElement[] publishedSections = [.. publishedRoot.GetProperty("_embedded")
            .GetProperty("sections").EnumerateArray()];
        await Assert.That(publishedHasSectionReorder).IsFalse();
        await Assert.That(publishedSections.All(section =>
            !section.TryGetProperty("_links", out JsonElement links) ||
            !links.TryGetProperty("reorder-fields", out _))).IsTrue();
        RegistrationFormPersistedOrderSnapshot afterPublication =
            await fixture.GetOrderSnapshotAsync(scenario.ValidVersionId);

        using HttpResponseMessage immutable = await fixture.ReorderSectionsAsync(
            scenario, scenario.OrganizerUserId, publishedStamp, [firstSectionId, secondSectionId]);
        using JsonDocument immutableProblem = await ReadJsonAsync(immutable);
        await Assert.That(immutable.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(immutableProblem.RootElement.GetProperty("code").GetString())
            .IsEqualTo("registration_form_version_immutable");
        await fixture.AssertOrderSnapshotUnchangedAsync(scenario.ValidVersionId, afterPublication);
    }

    [Test]
    public async Task RealAuthoringStackEnforcesAuthorizationPreflightPublicationAndConcurrency()
    {
        RegistrationFormsRuntimeScenario scenario = await fixture.SeedAsync();

        using HttpResponseMessage organizerDraft = await fixture.SendAsync(
            HttpMethod.Get,
            fixture.VersionRoute(scenario, scenario.ValidVersionId),
            scenario.OrganizerUserId);
        using JsonDocument organizerDraftDocument = await ReadJsonAsync(organizerDraft);
        await Assert.That(organizerDraft.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(organizerDraftDocument.RootElement.GetProperty("_links")
            .TryGetProperty("publish", out _)).IsTrue();

        using HttpResponseMessage contributorEvent = await fixture.SendAsync(
            HttpMethod.Get,
            $"/api/Event/{scenario.EventId:D}",
            scenario.ContributorUserId);
        using JsonDocument contributorEventDocument = await ReadJsonAsync(contributorEvent);
        await Assert.That(contributorEvent.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(contributorEventDocument.RootElement.GetProperty("_links")
            .TryGetProperty("manage-registration-workflow", out _)).IsFalse();

        using HttpResponseMessage contributorPublish = await fixture.PublishAsync(
            scenario,
            scenario.ContributorUserId,
            scenario.ValidVersionId,
            scenario.ValidStamp);
        await Assert.That(contributorPublish.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);

        using HttpResponseMessage invalidPublish = await fixture.PreflightAsync(
            scenario,
            scenario.OrganizerUserId,
            scenario.InvalidVersionId);
        using JsonDocument invalidProblem = await ReadJsonAsync(invalidPublish);
        await Assert.That(invalidPublish.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(invalidPublish.Content.Headers.ContentType?.MediaType)
            .IsEqualTo("application/problem+json");
        await Assert.That(invalidProblem.RootElement.GetProperty("code").GetString())
            .IsEqualTo("registration_form_preflight_failed");

        using HttpResponseMessage published = await fixture.PublishAsync(
            scenario,
            scenario.OrganizerUserId,
            scenario.ValidVersionId,
            scenario.ValidStamp);
        await Assert.That(published.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await fixture.AssertPinnedArtifactsAsync(scenario.ValidVersionId);

        using HttpResponseMessage stale = await fixture.PublishAsync(
            scenario,
            scenario.OrganizerUserId,
            scenario.ValidVersionId,
            scenario.ValidStamp);
        using JsonDocument staleProblem = await ReadJsonAsync(stale);
        await Assert.That(stale.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(staleProblem.RootElement.GetProperty("code").GetString())
            .IsEqualTo("concurrent_update");

        using HttpResponseMessage publishedVersion = await fixture.SendAsync(
            HttpMethod.Get,
            fixture.VersionRoute(scenario, scenario.ValidVersionId),
            scenario.OrganizerUserId);
        using JsonDocument publishedDocument = await ReadJsonAsync(publishedVersion);
        JsonElement root = publishedDocument.RootElement;
        await Assert.That(publishedVersion.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(root.GetProperty("_links").TryGetProperty("publish", out _)).IsFalse();
        JsonElement section = root.GetProperty("_embedded").GetProperty("sections")[0];
        await Assert.That(section.TryGetProperty("_links", out _)).IsFalse();
        await Assert.That(section.GetProperty("_embedded").GetProperty("fields")[0]
            .TryGetProperty("_links", out _)).IsFalse();
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
}

public sealed class RegistrationFormsRealRuntimeFixture : IAsyncInitializer, IAsyncDisposable
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("registration_forms_http")
        .WithUsername("postgres")
        .WithPassword(Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)))
        .Build();

    private PostgreSqlApiWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using (var context = new ExploreDbContext(
            new DbContextOptionsBuilder<ExploreDbContext>()
                .UseNpgsql(_container.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options))
        {
            await context.Database.EnsureCreatedAsync();
            await PostgresModelConstraintApplier.ApplyAsync(context);
            await LookupTableSeeder.SeedAsync(context);
        }

        _factory = new PostgreSqlApiWebApplicationFactory(
            _container.GetConnectionString(),
            new Dictionary<string, string?>
            {
                ["Testing:HostProfile"] = TestHostProfile.RealRuntime,
                ["RateLimiting:DisableInTesting"] = "true",
                ["Logging:LogLevel:Default"] = "Critical"
            },
            services =>
            {
                services.RemoveAll<IAuthorizationProvider>();
                services.AddScoped<IAuthorizationProvider, FallbackAuthorizationService>();
            });
        _client = _factory.CreateClient();
    }

    public async Task<RegistrationFormsRuntimeScenario> SeedAsync()
    {
        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        ExploreDbContext context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        TenantScenarioSeed.TenantScenarioResult organizer =
            await TenantScenarioSeed.SeedActiveTenantWithUserAsync(context);
        TenantScenarioSeed.TenantScenarioResult contributor =
            await TenantScenarioSeed.SeedActiveTenantWithUserAsync(context);
        var @event = new EventBuilder()
            .WithTitle("Registration authoring real HTTP")
            .WithActorId(contributor.ActorId)
            .WithTenantId(organizer.TenantId)
            .WithStatus(EventStatusEnum.Published)
            .WithVisibility(VisibilityTypeEnum.Public)
            .Build();
        @event.EventProvenanceTypeId = 1;
        @event.OrganizerActorId = organizer.ActorId;

        RegistrationForm valid = Form(organizer.TenantId, @event.Id, "valid", withField: true);
        RegistrationForm invalid = Form(organizer.TenantId, @event.Id, "invalid", withField: false);
        context.Events.Add(@event);
        context.RegistrationForms.AddRange(valid, invalid);
        await context.SaveChangesAsync();

        RegistrationFormVersion validVersion = valid.Versions.Single();
        RegistrationFormVersion invalidVersion = invalid.Versions.Single();
        return new(
            organizer.UserId,
            contributor.UserId,
            @event.Id,
            valid.Id,
            validVersion.Id,
            validVersion.ConcurrencyStamp,
            invalid.Id,
            invalidVersion.Id,
            invalidVersion.ConcurrencyStamp);
    }

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string route, Guid userId)
    {
        using var request = new HttpRequestMessage(method, route);
        request.Headers.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(userId, "Registration form author"));
        return await _client.SendAsync(request);
    }

    public async Task<HttpResponseMessage> PublishAsync(
        RegistrationFormsRuntimeScenario scenario,
        Guid userId,
        Guid versionId,
        Guid stamp)
    {
        Guid formId = versionId == scenario.ValidVersionId ? scenario.ValidFormId : scenario.InvalidFormId;
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/events/{scenario.EventId:D}/registration-forms/{formId:D}/versions/{versionId:D}/publish");
        request.Headers.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(userId, "Registration form author"));
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{stamp:D}\"");
        return await _client.SendAsync(request);
    }

    public async Task<HttpResponseMessage> PreflightAsync(
        RegistrationFormsRuntimeScenario scenario,
        Guid userId,
        Guid versionId)
    {
        Guid formId = versionId == scenario.ValidVersionId ? scenario.ValidFormId : scenario.InvalidFormId;
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/events/{scenario.EventId:D}/registration-forms/{formId:D}/versions/{versionId:D}/preflight");
        request.Headers.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(userId, "Registration form author"));
        return await _client.SendAsync(request);
    }

    public Task<HttpResponseMessage> ReorderSectionsAsync(
        RegistrationFormsRuntimeScenario scenario,
        Guid userId,
        Guid stamp,
        IReadOnlyList<Guid> orderedIds) => ReorderAsync(
            $"/api/events/{scenario.EventId:D}/registration-forms/{scenario.ValidFormId:D}/versions/{scenario.ValidVersionId:D}/sections/reorder",
            userId,
            stamp,
            orderedIds);

    public Task<HttpResponseMessage> ReorderFieldsAsync(
        RegistrationFormsRuntimeScenario scenario,
        Guid userId,
        Guid sectionId,
        Guid stamp,
        IReadOnlyList<Guid> orderedIds) => ReorderAsync(
            $"/api/events/{scenario.EventId:D}/registration-forms/{scenario.ValidFormId:D}/versions/{scenario.ValidVersionId:D}/sections/{sectionId:D}/fields/reorder",
            userId,
            stamp,
            orderedIds);

    public string VersionRoute(RegistrationFormsRuntimeScenario scenario, Guid versionId)
    {
        Guid formId = versionId == scenario.ValidVersionId ? scenario.ValidFormId : scenario.InvalidFormId;
        return $"/api/events/{scenario.EventId:D}/registration-forms/{formId:D}/versions/{versionId:D}";
    }

    public async Task AssertPinnedArtifactsAsync(Guid versionId)
    {
        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        ExploreDbContext context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        RegistrationFormVersion version = await context.RegistrationFormVersions
            .AsNoTracking()
            .Include(value => value.Sections)
            .ThenInclude(section => section.Fields)
            .ThenInclude(field => field.Options)
            .Include(value => value.Rules)
            .SingleAsync(value => value.Id == versionId);
        FormSchemaArtifactBundle expected = new FormSchemaArtifactGenerator().Generate(version);
        await Assert.That(version.StatusId).IsEqualTo((int)RegistrationFormStatusEnum.Published);
        await Assert.That(version.SchemaHash).IsEqualTo(expected.SchemaHash);
        await Assert.That(version.DataSchemaArtifact).IsEqualTo(expected.DataSchemaJson);
        await Assert.That(version.UiSchemaArtifact).IsEqualTo(expected.UiSchemaJson);
        await Assert.That(version.LogicSchemaArtifact).IsEqualTo(expected.LogicSchemaJson);
        await Assert.That(version.MappingArtifact).IsEqualTo(expected.MappingArtifactJson);
    }

    public async Task<RegistrationFormPersistedOrderSnapshot> GetOrderSnapshotAsync(Guid versionId)
    {
        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        ExploreDbContext context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var version = await context.RegistrationFormVersions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(value => value.Id == versionId)
            .Select(value => new { value.ConcurrencyStamp, value.StatusId })
            .SingleAsync();
        RegistrationFormPersistedOrdinal[] sections = await context.RegistrationFormSections
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(section => section.RegistrationFormVersionId == versionId)
            .OrderBy(section => section.Id)
            .Select(section => new RegistrationFormPersistedOrdinal(section.Id, section.Ordinal))
            .ToArrayAsync();
        RegistrationFormPersistedFieldOrdinal[] fields = await context.RegistrationFormFields
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(field => field.RegistrationFormVersionId == versionId)
            .OrderBy(field => field.Id)
            .Select(field => new RegistrationFormPersistedFieldOrdinal(
                field.Id, field.RegistrationFormSectionId, field.Ordinal))
            .ToArrayAsync();
        return new(version.ConcurrencyStamp, version.StatusId, sections, fields);
    }

    public async Task AssertOrderSnapshotUnchangedAsync(
        Guid versionId,
        RegistrationFormPersistedOrderSnapshot expected)
    {
        RegistrationFormPersistedOrderSnapshot actual = await GetOrderSnapshotAsync(versionId);
        await Assert.That(actual.ConcurrencyStamp).IsEqualTo(expected.ConcurrencyStamp);
        await Assert.That(actual.StatusId).IsEqualTo(expected.StatusId);
        await Assert.That(actual.Sections.SequenceEqual(expected.Sections)).IsTrue();
        await Assert.That(actual.Fields.SequenceEqual(expected.Fields)).IsTrue();
        await Assert.That(actual.Sections.All(section => section.Ordinal > 0)).IsTrue();
        await Assert.That(actual.Fields.All(field => field.Ordinal > 0)).IsTrue();
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
        await _container.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private static RegistrationForm Form(Guid tenantId, Guid eventId, string key, bool withField)
    {
        DateTime now = DateTime.UtcNow;
        RegistrationForm form = RegistrationForm.Create(
            tenantId, eventId, "platform.registration", key, $"{key} form", now);
        RegistrationFormVersion version = RegistrationFormVersion.Create(form, 1, "en", null, null, now);
        if (withField)
        {
            RegistrationFormSection section = RegistrationFormSection.Create(
                Guid.CreateVersion7(), version, 1, "Details", now);
            RegistrationFormSection secondSection = RegistrationFormSection.Create(
                Guid.CreateVersion7(), version, 2, "Additional", now);
            RegistrationFormField field = RegistrationFormField.Create(
                Guid.CreateVersion7(), section, 1, "platform.registration", "email", "Email",
                RegistrationFieldTypeEnum.Email, 1,
                RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers,
                false, true, now);
            RegistrationFormField secondField = RegistrationFormField.Create(
                Guid.CreateVersion7(), section, 2, "platform.registration", "name", "Name",
                RegistrationFieldTypeEnum.ShortText, 1,
                RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers,
                false, true, now);
            RegistrationFormField thirdField = RegistrationFormField.Create(
                Guid.CreateVersion7(), secondSection, 1, "platform.registration", "note", "Note",
                RegistrationFieldTypeEnum.LongText, 1,
                RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers,
                false, true, now);
            version.AddSection(section);
            version.AddSection(secondSection);
            version.AddField(section, field);
            version.AddField(section, secondField);
            version.AddField(secondSection, thirdField);
        }
        form.AddVersion(version);
        return form;
    }

    private async Task<HttpResponseMessage> ReorderAsync(
        string route,
        Guid userId,
        Guid stamp,
        IReadOnlyList<Guid> orderedIds)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, route);
        request.Headers.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(userId, "Registration form author"));
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{stamp:D}\"");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { orderedIds }), Encoding.UTF8, "application/json");
        return await _client.SendAsync(request);
    }
}

public sealed record RegistrationFormsRuntimeScenario(
    Guid OrganizerUserId,
    Guid ContributorUserId,
    Guid EventId,
    Guid ValidFormId,
    Guid ValidVersionId,
    Guid ValidStamp,
    Guid InvalidFormId,
    Guid InvalidVersionId,
    Guid InvalidStamp);

public sealed record RegistrationFormPersistedOrderSnapshot(
    Guid ConcurrencyStamp,
    int StatusId,
    RegistrationFormPersistedOrdinal[] Sections,
    RegistrationFormPersistedFieldOrdinal[] Fields);

public sealed record RegistrationFormPersistedOrdinal(Guid Id, int Ordinal);

public sealed record RegistrationFormPersistedFieldOrdinal(Guid Id, Guid SectionId, int Ordinal);
