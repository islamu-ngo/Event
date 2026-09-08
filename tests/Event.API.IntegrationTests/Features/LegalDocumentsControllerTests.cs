namespace Event.Api.IntegrationTests.Features;

using Explore.API.Controllers;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.LegalDocuments;
using Explore.Application.Features.ConfigurationManifest.LegalDocuments;
using Explore.Application.Features.LegalDocuments.Handlers.Queries;
using Explore.Application.Features.LegalDocuments.Requests.Queries;
using Explore.Domain;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

public sealed class LegalDocumentsControllerTests
{
    private static readonly DateTime OccurredAt =
        new(2026, 8, 30, 16, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task Get_PublishedInstanceDocumentReturnsRenderedRoleLabeledDto()
    {
        await using TestBoundary boundary = await TestBoundary.CreateAsync(
            seedPublishedDocument: true);

        ActionResult<PublicLegalDocumentDto> response =
            await boundary.Controller.Get(
                "terms-of-service",
                CancellationToken.None);

        OkObjectResult ok = (OkObjectResult)response.Result!;
        PublicLegalDocumentDto document = (PublicLegalDocumentDto)ok.Value!;
        await Assert.That(ok.StatusCode).IsEqualTo(StatusCodes.Status200OK);
        await Assert.That(document.OwnerRoleCode).IsEqualTo("instance_operator");
        await Assert.That(document.RenderedHtml)
            .Contains("Operator &amp; Community");
        await Assert.That(document.RenderedHtml).DoesNotContain("{{");
    }

    [Test]
    public async Task Get_UnknownKindReturnsNonCacheableValueSafe404()
    {
        await using TestBoundary boundary = await TestBoundary.CreateAsync(
            seedPublishedDocument: false);

        ActionResult<PublicLegalDocumentDto> response =
            await boundary.Controller.Get(
                "unknown-kind",
                CancellationToken.None);

        ObjectResult result = (ObjectResult)response.Result!;
        ProblemDetails problem = (ProblemDetails)result.Value!;
        await Assert.That(result.StatusCode).IsEqualTo(StatusCodes.Status404NotFound);
        await Assert.That(problem.Extensions["code"]?.ToString())
            .IsEqualTo("legal_document_not_found");
        await Assert.That(boundary.Controller.Response.Headers.CacheControl.ToString())
            .IsEqualTo("no-store");
    }

    private sealed class TestBoundary : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _services;

        private TestBoundary(
            SqliteConnection connection,
            ServiceProvider services,
            LegalDocumentsController controller)
        {
            _connection = connection;
            _services = services;
            Controller = controller;
        }

        public LegalDocumentsController Controller { get; }

        public static async Task<TestBoundary> CreateAsync(
            bool seedPublishedDocument)
        {
            var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = ":memory:"
                }.ToString());
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<ExploreDbContext>()
                .UseSqlite(connection)
                .UseSnakeCaseNamingConvention()
                .Options;
            var context = new ExploreDbContext(options);
            await context.Database.EnsureCreatedAsync();
            if (seedPublishedDocument)
            {
                context.LegalDocuments.Add(PublishedDocument());
                await context.SaveChangesAsync();
                context.ChangeTracker.Clear();
            }

            var services = new ServiceCollection();
            services.AddSingleton(context);
            services.AddSingleton<ILegalDocumentRepository>(
                new LegalDocumentRepository(context));
            services.AddSingleton<LegalDocumentRenderingService>();
            services.AddSingleton<ITenantContext>(
                new FixedTenantContext(Guid.CreateVersion7()));
            services.AddSingleton<ITenantDirectoryOperatorReadinessEvaluator>(
                new UnexpectedTenantIdentityEvaluator());
            services.AddSingleton<IInstanceOperatorIdentity>(InstanceIdentity());
            services.AddTransient<
                IRequestHandler<
                    GetPublicLegalDocumentQuery,
                    PublicLegalDocumentQueryResult>,
                GetPublicLegalDocumentQueryHandler>();
            services.AddTransient<IMediator>(provider => new Mediator(provider));
            ServiceProvider provider = services.BuildServiceProvider();
            var controller = new LegalDocumentsController(
                provider.GetRequiredService<IMediator>())
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext()
                }
            };
            return new TestBoundary(connection, provider, controller);
        }

        public async ValueTask DisposeAsync()
        {
            await _services.DisposeAsync();
            await _connection.DisposeAsync();
        }

        private static LegalDocument PublishedDocument()
        {
            LegalDocument document = LegalDocument.CreateDraft(
                LegalDocumentScope.Instance,
                tenantId: null,
                LegalDocumentKind.TermsOfService,
                LegalDocumentAudience.Public,
                [
                    LegalDocumentLocalizedSource.Create(
                        "en",
                        "Published Terms",
                        "Reviewed public terms.",
                        "# Terms\n\nAccountable operator: {{accountable_identity}}.")
                ],
                templateProvenance: null,
                "instance-identity:v1",
                requiresFreshAcceptance: false,
                OccurredAt);
            document.SubmitForReview(OccurredAt.AddMinutes(1));
            document.Approve(
                Guid.CreateVersion7(),
                "review:evidence",
                OccurredAt.AddMinutes(2));
            document.Schedule(
                OccurredAt.AddMinutes(4),
                OccurredAt.AddMinutes(3));
            document.Publish(OccurredAt.AddMinutes(4));
            return document;
        }

        private static InstanceOperatorIdentity InstanceIdentity() =>
            InstanceOperatorIdentity.Create(
                new InstanceOperatorIdentityOptions
                {
                    OperatorId = Guid.CreateVersion7(),
                    PublicName = "Community Operator",
                    LegalName = "Operator & Community",
                    OfficialOrigin = "https://example.test",
                    OperatorKindCode =
                        TenantDirectoryOperatorKinds.RegisteredOrganization,
                    JurisdictionCountryCode = "BE",
                    PublicContactEmail = "public@example.test",
                    WebsiteUrl = "https://example.test",
                    LegalNoticeUrl = "https://example.test/legal",
                    TermsUrl = "https://example.test/terms",
                    PrivacyUrl = "https://example.test/privacy"
                });
    }

    private sealed class FixedTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId { get; } = tenantId;
    }

    private sealed class UnexpectedTenantIdentityEvaluator :
        ITenantDirectoryOperatorReadinessEvaluator
    {
        public Task<TenantDirectoryOperatorReadinessAssessment> EvaluateAsync(
            Guid tenantId,
            TenantDirectoryOperatorIdentityCapability capability,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "Instance legal requests must not resolve tenant identity.");
    }
}
