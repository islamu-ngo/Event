using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.LegalDocuments;
using Explore.Application.Features.LegalDocuments.Requests.Queries;
using Explore.Application.Operations.Decorators;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings.Documents;
using Explore.Domain.Settings.Documents.Payloads;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class NativeLegalDocumentHttpTests
{
    private static readonly DateTime OccurredAt = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
    private const string IdentityReference = "private-identity-evidence";
    private const string ReviewReference = "private-review-evidence";
    private const string PublicMarkdown = "# Published\n\n{{accountable_identity}} / {{operator.public_name}} / {{operator.legal_notice_url}}\n\n[Privacy policy](https://policy.example.test/privacy/terms&conditions)";

    [Test]
    public async Task Controller_ConsumesOnlyTheClosedQueryAndPreservesPublicRouteMetadata()
    {
        var controller = typeof(LegalDocumentsController);
        await Assert.That(controller.GetConstructors().Single().GetParameters().Select(parameter => parameter.ParameterType).ToArray())
            .IsEquivalentTo(new[] { typeof(IQueryHandler<GetPublicLegalDocumentQuery, PublicLegalDocumentQueryResult>) });
        await Assert.That(controller.GetCustomAttribute<RouteAttribute>()!.Template).IsEqualTo("api/legal-documents");
        await Assert.That(controller.GetCustomAttribute<EndpointClassificationAttribute>()!.Class).IsEqualTo(EndpointClass.Public);
        var action = controller.GetMethod(nameof(LegalDocumentsController.Get))!;
        await Assert.That(action.IsDefined(typeof(AllowAnonymousAttribute))).IsTrue();
        await Assert.That(action.GetCustomAttribute<HttpGetAttribute>()!.Template).IsEqualTo("{kindCode}");
        await Assert.That(action.GetCustomAttribute<HttpGetAttribute>()!.Name).IsEqualTo(RouteNames.GetPublicLegalDocument);
        await Assert.That(action.GetCustomAttribute<OutputCacheAttribute>()!.PolicyName).IsEqualTo("PublicLegalDocuments");
        await Assert.That(action.GetCustomAttributes<ProducesResponseTypeAttribute>().Select(item => item.StatusCode).ToArray())
            .IsEquivalentTo(new[] { 200, 400, 404, 503 });
    }

    [Test]
    public async Task AnonymousHttp_ReturnsLastPublishedVersionAndLocaleWithoutDraftOrReviewDisclosure()
    {
        await using var factory = new LegalDocumentFactory();
        using var client = factory.CreateClient();
        var document = Draft(LegalDocumentKind.TermsOfService);
        Publish(document, OccurredAt);
        document.CreateRevision(LegalDocumentAudience.Public, Sources(PublicMarkdown), null, false, OccurredAt.AddMinutes(5));
        document.BindAccountableIdentity(IdentityReference, OccurredAt.AddMinutes(5));
        var publication = Publish(document, OccurredAt.AddMinutes(5));
        document.CreateRevision(LegalDocumentAudience.Public, Sources("# Unpublished\n\nprivate-draft-content"), null, false, OccurredAt.AddMinutes(10));
        await SeedAsync(factory, document);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/legal-documents/terms-of-service");
        request.Headers.AcceptLanguage.ParseAdd("fr");
        using var response = await client.SendAsync(request);
        var dto = await ReadAsync(response);
        await Assert.That(dto.Version).IsEqualTo(2);
        await Assert.That(dto.EffectiveAt).IsEqualTo(publication.EffectiveAt);
        await Assert.That(dto.ContentDigest).IsEqualTo(publication.ContentDigest);
        await Assert.That(dto.KindCode).IsEqualTo("terms-of-service");
        await Assert.That(dto.ScopeCode).IsEqualTo("instance");
        await Assert.That(dto.OwnerRoleCode).IsEqualTo("instance_operator");
        await Assert.That(dto.LanguageTag).IsEqualTo("fr");
        await Assert.That(dto.Title).IsEqualTo("Public fr");
        await Assert.That(dto.Summary).IsEqualTo("Summary fr");
        await Assert.That(dto.IsLocaleFallback).IsFalse();
        await Assert.That(dto.RenderedHtml).Contains("Test Instance Operator ASBL");
        await Assert.That(dto.RenderedHtml).Contains("https://instance.example.test/legal");
        await Assert.That(dto.RenderedHtml).Contains("href=\"https://policy.example.test/privacy/terms&amp;conditions\" rel=\"noopener noreferrer\"");
        await Assert.That(dto.RenderedHtml).DoesNotContain("{{");
        var json = await response.Content.ReadAsStringAsync();
        foreach (var withheld in new[] { "private-draft-content", IdentityReference, ReviewReference, "accountableIdentityReference", "reviewerId", "sources", "publications" })
            await Assert.That(json).DoesNotContain(withheld);
        using var englishRequest = new HttpRequestMessage(HttpMethod.Get, "/api/legal-documents/terms-of-service");
        englishRequest.Headers.AcceptLanguage.ParseAdd("en");
        using var englishResponse = await client.SendAsync(englishRequest);
        await Assert.That((await ReadAsync(englishResponse)).LanguageTag).IsEqualTo("en");
    }

    [Test]
    [Arguments("unknown-kind", "absent")]
    [Arguments("tenant-terms", "absent")]
    [Arguments("terms-of-service", "draft")]
    [Arguments("terms-of-service", "retired")]
    [Arguments("terms-of-service", "private")]
    public async Task AnonymousHttp_AbsentAndNonPublicDocumentsReturnNonCacheable404(string kind, string state)
    {
        await using var factory = new LegalDocumentFactory();
        using var client = factory.CreateClient();
        if (state != "absent")
        {
            var document = Draft(LegalDocumentKind.TermsOfService,
                audience: state == "private" ? LegalDocumentAudience.Administrators : LegalDocumentAudience.Public);
            if (state != "draft") Publish(document, OccurredAt);
            if (state == "retired") document.Retire(OccurredAt.AddMinutes(5));
            await SeedAsync(factory, document);
        }
        using var response = await client.GetAsync($"/api/legal-documents/{kind}");
        await ProblemAsync(response, HttpStatusCode.NotFound, "legal_document_not_found");
    }

    [Test]
    [Arguments("missing")]
    [Arguments("malformed")]
    [Arguments("incomplete")]
    [Arguments("unresolved-token")]
    public async Task AnonymousHttp_UnavailableTenantIdentityOrRenderingReturnsValueSafe503(string failure)
    {
        await using var factory = new LegalDocumentFactory();
        using var client = factory.CreateClient();
        var document = Draft(LegalDocumentKind.TenantTerms, PlatformDefaults.DefaultTenantId,
            markdown: failure == "unresolved-token" ? "# Terms\n\n{{operator.website_url}}" : PublicMarkdown);
        Publish(document, OccurredAt);
        string? identity = failure switch
        {
            "missing" => null,
            "malformed" => "{\"publicName\":[]}",
            "incomplete" => IdentityJson("Tenant & Community", "http://unsafe.example.test/legal"),
            _ => IdentityJson("Tenant & Community")
        };
        await SeedAsync(factory, document, identity);
        using var response = await client.GetAsync("/api/legal-documents/tenant-terms");
        await ProblemAsync(response, HttpStatusCode.ServiceUnavailable, "legal_document_rendering_unavailable");
        var json = await response.Content.ReadAsStringAsync();
        foreach (var withheld in new[] { "Tenant & Community", "unsafe.example.test", "website_url", "publicName", IdentityReference, ReviewReference })
            await Assert.That(json).DoesNotContain(withheld);
    }

    [Test]
    public async Task AnonymousHttp_UsesTrustedTenantIdentityAndPublicDisclosureRatherThanPaidCommerceReadiness()
    {
        await using var factory = new LegalDocumentFactory();
        using var client = factory.CreateClient();
        var document = Draft(LegalDocumentKind.TenantTerms, PlatformDefaults.DefaultTenantId);
        Publish(document, OccurredAt);
        await SeedAsync(factory, document, IdentityJson("Tenant & Community"));
        var foreign = Draft(LegalDocumentKind.TenantTerms, Guid.CreateVersion7(), markdown: "# Foreign\n\nforeign-private-selection");
        Publish(foreign, OccurredAt);
        await SeedAsync(factory, foreign, IdentityJson("Foreign Operator"));
        using var response = await client.GetAsync($"/api/legal-documents/tenant-terms?tenantId={foreign.TenantId}&languageTag=fr&accountable_identity=Forged");
        var dto = await ReadAsync(response);
        await Assert.That(dto.OwnerRoleCode).IsEqualTo("tenant_operator");
        await Assert.That(dto.ScopeCode).IsEqualTo("tenant");
        await Assert.That(dto.LanguageTag).IsEqualTo("en");
        await Assert.That(dto.RenderedHtml).Contains("Tenant &amp; Community");
        await Assert.That(dto.RenderedHtml).Contains("https://tenant.example.test/legal");
        foreach (var withheld in new[] { "Foreign Operator", "foreign-private-selection", "Forged", "Test Instance Operator" })
            await Assert.That(dto.RenderedHtml).DoesNotContain(withheld);
    }

    [Test]
    public async Task NativeScopes_UseRealDependenciesAndTenantAuthorityWithoutMutationAndPropagateCancellation()
    {
        await using var factory = new LegalDocumentFactory();
        using var client = factory.CreateClient();
        var firstDocument = Draft(LegalDocumentKind.TenantTerms, PlatformDefaults.DefaultTenantId);
        var secondDocument = Draft(LegalDocumentKind.TenantTerms, Guid.CreateVersion7());
        Publish(firstDocument, OccurredAt);
        Publish(secondDocument, OccurredAt);
        await SeedAsync(factory, firstDocument, IdentityJson("First & Operator"));
        await SeedAsync(factory, secondDocument, IdentityJson("Second & Operator"));
        using var first = factory.Services.CreateScope();
        using var second = factory.Services.CreateScope();
        first.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(firstDocument.TenantId!.Value);
        second.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(secondDocument.TenantId!.Value);
        var query = first.ServiceProvider.GetRequiredService<IQueryHandler<GetPublicLegalDocumentQuery, PublicLegalDocumentQueryResult>>();
        var other = second.ServiceProvider.GetRequiredService<IQueryHandler<GetPublicLegalDocumentQuery, PublicLegalDocumentQueryResult>>();
        await Assert.That(query).IsTypeOf<AuthorizationQueryHandlerDecorator<GetPublicLegalDocumentQuery, PublicLegalDocumentQueryResult>>();
        await Assert.That(ReferenceEquals(query, first.ServiceProvider.GetRequiredService<IQueryHandler<GetPublicLegalDocumentQuery, PublicLegalDocumentQueryResult>>())).IsTrue();
        await Assert.That(ReferenceEquals(query, other)).IsFalse();
        await Assert.That(first.ServiceProvider.GetRequiredService<ILegalDocumentRepository>()).IsTypeOf<LegalDocumentRepository>();
        await Assert.That(first.ServiceProvider.GetRequiredService<ITenantDirectoryOperatorReadinessEvaluator>()).IsTypeOf<TenantDirectoryOperatorReadinessEvaluator>();
        var result = await query.QueryAsync(new("tenant-terms", " FR-be "), default);
        var otherResult = await other.QueryAsync(new("tenant-terms", "en"), default);
        await Assert.That(result.Document!.LanguageTag).IsEqualTo("fr");
        await Assert.That(result.Document.IsLocaleFallback).IsTrue();
        await Assert.That(result.Document.RenderedHtml).Contains("First &amp; Operator");
        await Assert.That(otherResult.Document!.RenderedHtml).Contains("Second &amp; Operator");
        await Assert.That(first.ServiceProvider.GetRequiredService<ExploreDbContext>().ChangeTracker.HasChanges()).IsFalse();
        await Assert.That(second.ServiceProvider.GetRequiredService<ExploreDbContext>().ChangeTracker.HasChanges()).IsFalse();
        var invalidCulture = await query.QueryAsync(new("tenant-terms", " "), default);
        await Assert.That(invalidCulture.FailureCode).IsEqualTo("legal_document_rendering_unavailable");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var exception = await Assert.That(async () => await query.QueryAsync(new("tenant-terms", "en"), cancellation.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(exception!.CancellationToken).IsEqualTo(cancellation.Token);

        var controller = ActivatorUtilities.CreateInstance<LegalDocumentsController>(first.ServiceProvider);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            var action = await controller.Get("tenant-terms");
            await Assert.That(((PublicLegalDocumentDto)((OkObjectResult)action.Result!).Value!).LanguageTag).IsEqualTo("en");
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    private static LegalDocument Draft(LegalDocumentKind kind, Guid? tenantId = null,
        LegalDocumentAudience audience = LegalDocumentAudience.Public, string markdown = PublicMarkdown) =>
        LegalDocument.CreateDraft(tenantId is null ? LegalDocumentScope.Instance : LegalDocumentScope.Tenant,
            tenantId, kind, audience, Sources(markdown), null, IdentityReference, false, OccurredAt);

    private static LegalDocumentLocalizedSource[] Sources(string markdown) =>
        [LegalDocumentLocalizedSource.Create("en", "Public en", "Summary en", markdown),
         LegalDocumentLocalizedSource.Create("fr", "Public fr", "Summary fr", markdown)];

    private static LegalDocumentPublication Publish(LegalDocument document, DateTime at)
    {
        document.SubmitForReview(at.AddMinutes(1));
        document.Approve(Guid.CreateVersion7(), ReviewReference, at.AddMinutes(2));
        document.Schedule(at.AddMinutes(4), at.AddMinutes(3));
        return document.Publish(at.AddMinutes(4));
    }

    private static string IdentityJson(string legalName, string legalNoticeUrl = "https://tenant.example.test/legal") =>
        JsonSerializer.Serialize(new TenantDirectoryOperatorIdentitySettings
        {
            PublicName = "Tenant public name",
            LegalName = legalName,
            OperatorKindCode = "registered_organization",
            JurisdictionCountryCode = "BE",
            PublicContactEmail = "public@tenant.example.test",
            LegalNoticeUrl = legalNoticeUrl,
            PrivacyUrl = "https://tenant.example.test/privacy"
            // TermsUrl is deliberately absent: public disclosure must not require paid-commerce readiness.
        }, JsonSerializerOptions.Web);

    private static async Task SeedAsync(LegalDocumentFactory factory, LegalDocument document, string? identityJson = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        if (document.TenantId is { } tenantId)
        {
            var tenant = await db.Tenants.FindAsync(tenantId);
            if (tenant is null)
            {
                tenant = new TenantBuilder().WithId(tenantId).Build();
                db.Tenants.Add(tenant);
            }
            if (identityJson is not null)
            {
                var identity = TenantSettingsDocument.Create(tenantId, SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity,
                    TenantDirectoryOperatorIdentityDocumentDefaults.SchemaVersion,
                    TenantDirectoryOperatorIdentityDocumentDefaults.DefaultsVersion, identityJson);
                identity.Tenant = tenant;
                db.TenantSettingsDocuments.Add(identity);
            }
        }
        db.LegalDocuments.Add(document);
        await db.SaveChangesAsync();
    }

    private static async Task<PublicLegalDocumentDto> ReadAsync(HttpResponseMessage response)
    {
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<PublicLegalDocumentDto>())!;
    }

    private static async Task ProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        await Assert.That(response.StatusCode).IsEqualTo(status);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(json.RootElement.GetProperty("status").GetInt32()).IsEqualTo((int)status);
        await Assert.That(json.RootElement.GetProperty("code").GetString()).IsEqualTo(code);
    }

    private sealed class LegalDocumentFactory : AuthenticatedWebApplicationFactory
    {
        private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"native-legal-{Guid.CreateVersion7():N}.db");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveExploreDbContextRegistrations();
                var options = new DbContextOptionsBuilder<ExploreDbContext>();
                ConfigureDatabase(options);
                using (var db = new ExploreDbContext(options.Options))
                    db.Database.EnsureCreated();
                services.AddDbContextFactory<ExploreDbContext>(ConfigureDatabase);
                services.AddScoped(provider =>
                {
                    var db = provider.GetRequiredService<IDbContextFactory<ExploreDbContext>>().CreateDbContext();
                    db.TenantContext = provider.GetRequiredService<ITenantContext>();
                    db.CurrentUserService = provider.GetRequiredService<ICurrentUserService>();
                    return db;
                });
            });
        }

        private void ConfigureDatabase(DbContextOptionsBuilder options)
        {
            PrimaryDatabaseProviderComposition.ConfigureApplication(options, new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Runtime,
                Provider = PrimaryDatabaseProvider.Sqlite,
                Database = _databasePath
            });
            options.UseSnakeCaseNamingConvention();
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            var options = new DbContextOptionsBuilder<ExploreDbContext>();
            ConfigureDatabase(options);
            await using var db = new ExploreDbContext(options.Options);
            SqliteConnection.ClearPool((SqliteConnection)db.Database.GetDbConnection());
            File.Delete(_databasePath);
            File.Delete(_databasePath + "-wal");
            File.Delete(_databasePath + "-shm");
        }
    }
}
