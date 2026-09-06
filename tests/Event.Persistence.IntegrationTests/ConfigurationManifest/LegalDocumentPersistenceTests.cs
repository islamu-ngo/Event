// ABOUTME: Specifies portable persistence for legal aggregate and publication evidence.
// ABOUTME: Verifies entity-returning boundaries, graph fidelity, and five-provider model parity.

namespace Event.Persistence.IntegrationTests.ConfigurationManifest;

using Explore.Domain;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.ConfigurationManifest.LegalDocuments;
using Explore.Application.Features.LegalDocuments.Handlers.Queries;
using Explore.Application.Features.LegalDocuments.Requests.Queries;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Secrets.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Explore.Domain.ValueObjects;

public sealed class LegalDocumentPersistenceTests
{
    private static readonly DateTime OccurredAt =
        new(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task Model_MapsAggregateGraphWithoutAcceptanceFacts()
    {
        await using ExploreDbContext context = CreateModelContext(
            PrimaryDatabaseProvider.Sqlite);
        IModel model = context.GetService<IDesignTimeModel>().Model;
        Type[] entityTypes =
        [
            typeof(LegalDocument),
            typeof(LegalDocumentVersion),
            typeof(LegalDocumentLocalizedSource),
            typeof(LegalDocumentPublication)
        ];

        foreach (Type entityType in entityTypes)
            await Assert.That(model.FindEntityType(entityType)).IsNotNull();

        string[] forbiddenProperties =
        [
            "AcceptedAt",
            "AcceptedBy",
            "AcceptanceHistory",
            "AcceptanceRecordId",
            "UserId",
            "SubjectId"
        ];
        foreach (Type entityType in entityTypes)
        {
            IEntityType mapped = model.FindEntityType(entityType)!;
            await Assert.That(mapped.GetProperties()
                    .Select(property => property.Name)
                    .Intersect(forbiddenProperties, StringComparer.Ordinal))
                .IsEmpty();
        }
    }

    [Test]
    public async Task Sqlite_RoundTripPreservesPublishedEvidenceAndLocalizedSource()
    {
        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = ":memory:"
            }.ToString());
        await connection.OpenAsync();
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(connection)
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var context = new ExploreDbContext(options);
        await context.Database.EnsureCreatedAsync();

        LegalDocument document = PublishedTenantDocument(Guid.CreateVersion7());
        context.Set<LegalDocument>().Add(document);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        LegalDocument persisted = await context.Set<LegalDocument>()
            .AsNoTracking()
            .Include(candidate => candidate.Versions)
                .ThenInclude(version => version.Sources)
            .Include(candidate => candidate.Publications)
            .SingleAsync(candidate => candidate.Id == document.Id);

        await Assert.That(persisted.State)
            .IsEqualTo(LegalDocumentLifecycleState.Published);
        await Assert.That(persisted.Versions).HasSingleItem();
        await Assert.That(persisted.Versions[0].Sources).HasSingleItem();
        await Assert.That(persisted.Versions[0].Sources[0].LanguageTag)
            .IsEqualTo("en");
        await Assert.That(persisted.Publications).HasSingleItem();
        await Assert.That(persisted.Publications[0].ContentDigest)
            .IsEqualTo(persisted.Versions[0].ContentDigest);
    }

    [Test]
    public async Task RepositoryContract_ReturnsDomainEntitiesAndRequiresExplicitTarget()
    {
        Type applicationContract = typeof(IConfigurationManifestOperationRepository).Assembly
            .GetType("Explore.Application.Contracts.Persistence.ILegalDocumentRepository")
            ?? throw new InvalidOperationException(
                "ILegalDocumentRepository is missing.");
        Type persistenceType = typeof(ExploreDbContext).Assembly
            .GetType("Explore.Persistence.Repositories.LegalDocumentRepository")
            ?? throw new InvalidOperationException(
                "LegalDocumentRepository is missing.");

        await Assert.That(applicationContract.IsAssignableFrom(persistenceType)).IsTrue();
        foreach (var method in applicationContract.GetMethods())
        {
            string signature = method.ReturnType.ToString();
            await Assert.That(signature.Contains(
                    nameof(LegalDocument),
                    StringComparison.Ordinal)
                || method.Name == "AddAsync")
                .IsTrue();
        }

        string[] methodNames = applicationContract.GetMethods()
            .Select(method => method.Name)
            .ToArray();
        await Assert.That(methodNames).Contains("GetForUpdateAsync");
        await Assert.That(methodNames).Contains("GetByIdForUpdateAsync");
        await Assert.That(methodNames).Contains("GetPublishedAsync");
        await Assert.That(methodNames).Contains("AddAsync");
    }

    [Test]
    public async Task Repository_TargetCoordinatesPreventCrossTenantReads()
    {
        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = ":memory:"
            }.ToString());
        await connection.OpenAsync();
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(connection)
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var context = new ExploreDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var repository = new LegalDocumentRepository(context);
        Guid firstTenantId = Guid.CreateVersion7();
        Guid secondTenantId = Guid.CreateVersion7();
        LegalDocument first = PublishedTenantDocument(firstTenantId);
        LegalDocument second = PublishedTenantDocument(secondTenantId);
        await repository.AddAsync(first, CancellationToken.None);
        await repository.AddAsync(second, CancellationToken.None);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        LegalDocument? firstRead = await repository.GetPublishedAsync(
            LegalDocumentScope.Tenant,
            firstTenantId,
            LegalDocumentKind.TenantTerms,
            CancellationToken.None);
        LegalDocument? crossTenantRead = await repository.GetByIdForUpdateAsync(
            first.Id,
            LegalDocumentScope.Tenant,
            secondTenantId,
            CancellationToken.None);

        await Assert.That(firstRead?.Id).IsEqualTo(first.Id);
        await Assert.That(crossTenantRead).IsNull();
    }

    [Test]
    public async Task PublicQuery_ComposesPersistedPublicationWithTargetIdentity()
    {
        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = ":memory:"
            }.ToString());
        await connection.OpenAsync();
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(connection)
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var context = new ExploreDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var repository = new LegalDocumentRepository(context);
        LegalDocument document = PublishedInstanceDocument();
        await repository.AddAsync(document, CancellationToken.None);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        InstanceOperatorIdentity identity = InstanceOperatorIdentity.Create(
            new InstanceOperatorIdentityOptions
            {
                OperatorId = Guid.CreateVersion7(),
                PublicName = "Community Operator",
                LegalName = "Operator & Community",
                IsOfficialInstance = false,
                OfficialOrigin = "https://example.test",
                OperatorKindCode = TenantDirectoryOperatorKinds.RegisteredOrganization,
                JurisdictionCountryCode = "BE",
                RegistrationIdentifier = "registry-reference",
                PublicContactEmail = "public@example.test",
                WebsiteUrl = "https://example.test",
                LegalNoticeUrl = "https://example.test/legal",
                TermsUrl = "https://example.test/terms",
                PrivacyUrl = "https://example.test/privacy"
            });
        var handler = new GetPublicLegalDocumentQueryHandler(
            repository,
            new LegalDocumentRenderingService(),
            new FixedTenantContext(Guid.CreateVersion7()),
            new UnexpectedTenantIdentityEvaluator(),
            identity);

        PublicLegalDocumentQueryResult result = await handler.Handle(
            new GetPublicLegalDocumentQuery("terms-of-service", "en"),
            CancellationToken.None);

        await Assert.That(result.IsAvailable).IsTrue();
        await Assert.That(result.Document?.OwnerRoleCode)
            .IsEqualTo("instance_operator");
        await Assert.That(result.Document?.RenderedHtml)
            .Contains("Operator &amp; Community");
        await Assert.That(result.Document?.RenderedHtml)
            .DoesNotContain("instance-identity:v1");
    }

    [Test]
    [Arguments(PrimaryDatabaseProvider.PostgreSql)]
    [Arguments(PrimaryDatabaseProvider.Sqlite)]
    [Arguments(PrimaryDatabaseProvider.SqlServer)]
    [Arguments(PrimaryDatabaseProvider.MariaDb)]
    [Arguments(PrimaryDatabaseProvider.MySql)]
    public async Task ProviderModel_ContainsLegalTablesAndHasNoPendingChanges(
        PrimaryDatabaseProvider provider)
    {
        await using ExploreDbContext context = CreateModelContext(provider);
        IModel model = context.GetService<IDesignTimeModel>().Model;
        string prefix = provider is PrimaryDatabaseProvider.PostgreSql
            or PrimaryDatabaseProvider.SqlServer
            ? string.Empty
            : "ie_";

        await Assert.That(context.Database.HasPendingModelChanges()).IsFalse();
        await Assert.That(model.FindEntityType(typeof(LegalDocument))!.GetTableName())
            .IsEqualTo($"{prefix}legal_documents");
        await Assert.That(model.FindEntityType(typeof(LegalDocumentVersion))!.GetTableName())
            .IsEqualTo($"{prefix}legal_document_versions");
        await Assert.That(model.FindEntityType(typeof(LegalDocumentLocalizedSource))!.GetTableName())
            .IsEqualTo($"{prefix}legal_document_localized_sources");
        await Assert.That(model.FindEntityType(typeof(LegalDocumentPublication))!.GetTableName())
            .IsEqualTo($"{prefix}legal_document_publications");
    }

    private static LegalDocument PublishedTenantDocument(Guid tenantId)
    {
        var document = LegalDocument.CreateDraft(
            LegalDocumentScope.Tenant,
            tenantId,
            LegalDocumentKind.TenantTerms,
            LegalDocumentAudience.Public,
            [
                LegalDocumentLocalizedSource.Create(
                    "en",
                    "Policy",
                    "Portable summary",
                    "# Policy\n\nRepository-native source.")
            ],
            LegalDocumentTemplateProvenance.Create(
                "template.policy",
                "1.0.0",
                LegalDocumentTemplateSourceKind.ProjectOwned,
                "ISLAMU-Internal",
                "project-reviewed"),
            "target-tenant-identity:v1",
            requiresFreshAcceptance: true,
            OccurredAt);
        document.SubmitForReview(OccurredAt.AddMinutes(1));
        document.Approve(
            Guid.CreateVersion7(),
            "review-evidence:test",
            OccurredAt.AddMinutes(2));
        document.Schedule(
            OccurredAt.AddMinutes(4),
            OccurredAt.AddMinutes(3));
        document.Publish(OccurredAt.AddMinutes(4));
        return document;
    }

    private static LegalDocument PublishedInstanceDocument()
    {
        var document = LegalDocument.CreateDraft(
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
            "review-evidence:test",
            OccurredAt.AddMinutes(2));
        document.Schedule(
            OccurredAt.AddMinutes(4),
            OccurredAt.AddMinutes(3));
        document.Publish(OccurredAt.AddMinutes(4));
        return document;
    }

    private static ExploreDbContext CreateModelContext(
        PrimaryDatabaseProvider provider)
    {
        var optionsBuilder = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(
            optionsBuilder,
            CreateOptions(provider));
        return new ExploreDbContext(optionsBuilder.Options);
    }

    private static PrimaryDatabaseConnectionOptions CreateOptions(
        PrimaryDatabaseProvider provider)
    {
        if (provider == PrimaryDatabaseProvider.Sqlite)
        {
            return new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Migrator,
                Provider = provider,
                Database = Path.Combine(
                    Path.GetTempPath(),
                    $"legal-document-model-{Guid.CreateVersion7():N}.db")
            };
        }

        string ephemeralCredential = Guid.CreateVersion7().ToString("N");
        return new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.Migrator,
            Provider = provider,
            Host = "localhost",
            Database = "legal_document_model",
            Username = ephemeralCredential,
            Password = ephemeralCredential,
            TlsMode = PrimaryDatabaseTlsMode.Prefer,
            ServerFlavor = provider switch
            {
                PrimaryDatabaseProvider.MariaDb => PrimaryDatabaseServerFlavor.MariaDb,
                PrimaryDatabaseProvider.MySql => PrimaryDatabaseServerFlavor.MySql,
                _ => null
            },
            ServerVersion = provider switch
            {
                PrimaryDatabaseProvider.MariaDb => new Version(11, 4),
                PrimaryDatabaseProvider.MySql => new Version(8, 4),
                _ => null
            }
        };
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
                "Instance legal composition must not resolve tenant identity.");
    }
}
