using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Seed;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed class OrganizationTenantEvidenceRepositorySqliteTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CreateRetainsOnlyEvidenceWithoutInsertingOrUpdatingPrincipalGraphs(bool trackedParticipation)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = ":memory:",
            ForeignKeys = true
        }.ToString());
        await connection.OpenAsync();
        Guid tenantId = Guid.CreateVersion7();
        await using var context = CreateContext(connection, tenantId);
        await context.Database.EnsureCreatedAsync();
        await LookupTableSeeder.SeedAsync(context);
        var seeded = await SeedAsync(context, tenantId);
        context.ChangeTracker.Clear();

        var participation = await new OrganizationTenantRepository(context)
            .GetByOrganizationAndTenant(seeded.OrganizationId, tenantId);
        if (!trackedParticipation) context.ChangeTracker.Clear();
        var document = (await new StorageObjectRepository(context).GetEvidenceDocumentAsync(seeded.DocumentId, default))!;
        string fileTypeName = document.FileType.FullName;
        Guid documentStamp = document.ConcurrencyStamp;
        document.FullName = "Detached metadata must not be published";
        document.FileType.FullName = "Detached lookup must not be published";
        if (!trackedParticipation) participation!.Organization.Pii.FullName = "Detached organization must not be published";
        var evidence = OrganizationTenantEvidence.CreatePending(participation!, document);
        var repository = new OrganizationTenantEvidenceRepository(context);

        await new EfCoreUnitOfWork(context).ExecuteSerializableAsync(_ => repository.Create(evidence));

        await Assert.That(context.Entry(document).State).IsEqualTo(EntityState.Detached);
        await Assert.That(context.Entry(document.FileType).State).IsEqualTo(EntityState.Detached);
        await using var readback = CreateContext(connection, tenantId);
        var retained = (await new OrganizationTenantEvidenceRepository(readback).GetDetailsAsync(evidence.Id, false, default))!;
        await Assert.That(retained.DocumentStorageObjectId).IsEqualTo(seeded.DocumentId);
        await Assert.That(retained.OrganizationTenantId).IsEqualTo(participation!.Id);
        await Assert.That(retained.ReviewStatusId).IsEqualTo((int)ApprovalStatusEnum.Pending);
        await Assert.That(retained.DocumentStorageObject!.FullName).IsEqualTo("Retained document");
        await Assert.That(retained.DocumentStorageObject.FileType.FullName).IsEqualTo(fileTypeName);
        await Assert.That(retained.DocumentStorageObject.ConcurrencyStamp).IsEqualTo(documentStamp);
        var organization = await readback.Organizations.Include(item => item.Pii).SingleAsync(item => item.Id == seeded.OrganizationId);
        await Assert.That(organization.Pii.FullName).IsEqualTo("Retained organization");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CreateCannotMaterializeMissingOrForeignDocumentThroughItsNavigation(bool foreignDocument)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = ":memory:",
            ForeignKeys = true
        }.ToString());
        await connection.OpenAsync();
        Guid tenantId = Guid.CreateVersion7();
        await using var context = CreateContext(connection, tenantId);
        await context.Database.EnsureCreatedAsync();
        await LookupTableSeeder.SeedAsync(context);
        var seeded = await SeedAsync(context, tenantId);
        Guid foreignTenantId = Guid.CreateVersion7();
        var foreign = await SeedAsync(context, foreignTenantId);
        context.ChangeTracker.Clear();
        var participation = (await new OrganizationTenantRepository(context)
            .GetByOrganizationAndTenant(seeded.OrganizationId, tenantId))!;
        StorageObject document;
        if (foreignDocument)
        {
            await using var foreignContext = CreateContext(connection, foreignTenantId);
            document = (await new StorageObjectRepository(foreignContext).GetEvidenceDocumentAsync(foreign.DocumentId, default))!;
            document.TenantId = tenantId;
        }
        else
        {
            document = (await new StorageObjectRepository(context).GetEvidenceDocumentAsync(seeded.DocumentId, default))!;
            document.Id = Guid.CreateVersion7();
        }
        document.ObjectKey = $"untrusted/{Guid.CreateVersion7():N}";
        var evidence = OrganizationTenantEvidence.CreatePending(participation, document);
        var repository = new OrganizationTenantEvidenceRepository(context);

        var failure = await Assert.That(async () => await new EfCoreUnitOfWork(context)
            .ExecuteSerializableAsync(_ => repository.Create(evidence))).Throws<DbUpdateException>();
        await Assert.That((failure?.InnerException as SqliteException)?.SqliteExtendedErrorCode).IsEqualTo(787);
        await Assert.That(await repository.ListByParticipationAsync(participation.Id, default)).IsEmpty();
        await Assert.That(await new StorageObjectRepository(context).GetEvidenceDocumentAsync(document.Id, default)).IsNull();
        await using var foreignReadback = CreateContext(connection, foreignTenantId);
        await Assert.That((await new StorageObjectRepository(foreignReadback).GetEvidenceDocumentAsync(foreign.DocumentId, default))!.ObjectKey)
            .IsEqualTo($"tenants/{foreignTenantId:N}/evidence.pdf");
    }

    [Test]
    public async Task DuplicateDocumentConstraintStillProtectsRetainedEvidence()
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = ":memory:",
            ForeignKeys = true
        }.ToString());
        await connection.OpenAsync();
        Guid tenantId = Guid.CreateVersion7();
        await using var context = CreateContext(connection, tenantId);
        await context.Database.EnsureCreatedAsync();
        await LookupTableSeeder.SeedAsync(context);
        var seeded = await SeedAsync(context, tenantId);
        context.ChangeTracker.Clear();
        var participation = (await new OrganizationTenantRepository(context)
            .GetByOrganizationAndTenant(seeded.OrganizationId, tenantId))!;
        var document = (await new StorageObjectRepository(context).GetEvidenceDocumentAsync(seeded.DocumentId, default))!;
        var repository = new OrganizationTenantEvidenceRepository(context);
        var retained = OrganizationTenantEvidence.CreatePending(participation, document);
        await new EfCoreUnitOfWork(context).ExecuteSerializableAsync(_ => repository.Create(retained));
        var duplicate = OrganizationTenantEvidence.CreatePending(participation, document);

        var failure = await Assert.That(async () => await new EfCoreUnitOfWork(context)
            .ExecuteSerializableAsync(_ => repository.Create(duplicate))).Throws<DbUpdateException>();
        await Assert.That((failure?.InnerException as SqliteException)?.SqliteExtendedErrorCode).IsEqualTo(2067);
        var evidence = await repository.ListByParticipationAsync(participation.Id, default);
        await Assert.That(evidence.Count).IsEqualTo(1);
        await Assert.That(evidence.Single().Id).IsEqualTo(retained.Id);
    }

    private static ExploreDbContext CreateContext(SqliteConnection connection, Guid tenantId) =>
        new(TestDbContextOptions.Create<ExploreDbContext>().UseSqlite(connection).UseSnakeCaseNamingConvention().Options)
        {
            TenantContext = new FixedTenantContext(tenantId)
        };

    private static async Task<(Guid OrganizationId, Guid DocumentId)> SeedAsync(ExploreDbContext context, Guid tenantId)
    {
        var tenant = new Tenant
        {
            Id = tenantId,
            FullName = "Evidence tenant",
            Slug = $"evidence-{tenantId:N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        Guid organizationId = Guid.CreateVersion7();
        var organization = new Organization
        {
            Id = organizationId,
            Pii = new OrganizationPii { OrganizationId = organizationId, FullName = "Retained organization" }
        };
        var participation = new OrganizationTenant
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = tenant,
            OrganizationId = organizationId,
            Organization = organization,
            ApprovalStatusId = (int)ApprovalStatusEnum.Pending,
            ApprovalStatus = null!
        };
        var document = new StorageObject
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = tenant,
            FileTypeId = (int)FileTypeEnum.Document,
            FileType = null!,
            FullName = "Retained document",
            SafeDisplayName = "evidence.pdf",
            Uri = string.Empty,
            ObjectKey = $"tenants/{tenantId:N}/evidence.pdf",
            Provider = StorageProviders.Local,
            Extension = "pdf",
            ContentType = "application/pdf",
            Size = 5,
            LifecycleState = StorageObjectLifecycleStates.Active,
            Visibility = StorageObjectVisibilities.PrivateOwner,
            Purpose = StorageObjectPurposes.Document,
            OwningResourceKind = StorageOwningResourceKinds.OrganizationTenant,
            OwningResourceId = participation.Id
        };
        context.AddRange(participation, document);
        await context.SaveChangesAsync();
        return (organizationId, document.Id);
    }

    private sealed record FixedTenantContext(Guid TenantId) : ITenantContext;
}
