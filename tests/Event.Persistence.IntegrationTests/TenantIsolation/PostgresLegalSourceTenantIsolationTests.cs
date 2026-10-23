using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class PostgresLegalSourceTenantIsolationTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments("document")]
    [Arguments("version")]
    [Arguments("localization")]
    public async Task OrdinaryTenantCannotReadForeignLegalSourceEvenWithoutQueryFilters(string relation)
    {
        await using var rows =
            await PostgresAuthenticationLookupAuthorityTests.AuthenticationRows.CreateAsync(fixture);
        await using var seed = fixture.CreateDbContext();
        var document = LegalDocument.CreateImportedDraft(
            LegalDocumentScope.Tenant, rows.B, LegalDocumentKind.TenantTerms,
            LegalDocumentAudience.Public,
            [LegalDocumentLocalizedSource.Create("en", "Foreign terms", "Foreign summary", "Foreign source")],
            null, $"sha256:{new string('b', 64)}", true, DateTime.UtcNow);
        seed.LegalDocuments.Add(document);
        var ownDocument = LegalDocument.CreateImportedDraft(
            LegalDocumentScope.Tenant, rows.A, LegalDocumentKind.TenantTerms,
            LegalDocumentAudience.Public,
            [LegalDocumentLocalizedSource.Create("en", "Own terms", "Own summary", "Own source")],
            null, $"sha256:{new string('a', 64)}", true, DateTime.UtcNow);
        seed.LegalDocuments.Add(ownDocument);
        await seed.SaveChangesAsync();
        await using var request = rows.Context(rows.A);
        await rows.AssertOrdinaryRoleAsync(request);
        string login = await request.Database.SqlQueryRaw<string>(
            "SELECT current_user::text AS \"Value\"").SingleAsync();
        string tables = string.Join(", ", new[] {
            typeof(LegalDocument), typeof(LegalDocumentVersion), typeof(LegalDocumentLocalizedSource)
        }.Select(type =>
        {
            var entity = seed.Model.FindEntityType(type)!;
            return $"\"{entity.GetSchema()}\".\"{entity.GetTableName()}\"";
        }));
        await using var administration = new NpgsqlConnection(fixture.ConnectionString);
        await administration.OpenAsync();
        await using var permission = administration.CreateCommand();
        permission.CommandText = $"GRANT SELECT ON {tables} TO \"{login}\"";
        await permission.ExecuteNonQueryAsync();
        try
        {
            bool visible = relation switch
            {
                "document" => await request.LegalDocuments.IgnoreQueryFilters()
                    .AnyAsync(row => row.Id == document.Id),
                "version" => await request.LegalDocumentVersions.IgnoreQueryFilters()
                    .AnyAsync(row => row.LegalDocumentId == document.Id),
                "localization" => await request.LegalDocumentLocalizedSources.IgnoreQueryFilters()
                    .AnyAsync(row => row.LegalDocumentVersionId == document.Versions[0].Id),
                _ => throw new ArgumentOutOfRangeException(nameof(relation))
            };
            await Assert.That(visible).IsFalse();
            bool ownVisible = relation switch
            {
                "document" => await request.LegalDocuments.IgnoreQueryFilters()
                    .AnyAsync(row => row.Id == ownDocument.Id),
                "version" => await request.LegalDocumentVersions.IgnoreQueryFilters()
                    .AnyAsync(row => row.LegalDocumentId == ownDocument.Id),
                "localization" => await request.LegalDocumentLocalizedSources.IgnoreQueryFilters()
                    .AnyAsync(row => row.LegalDocumentVersionId == ownDocument.Versions[0].Id),
                _ => throw new ArgumentOutOfRangeException(nameof(relation))
            };
            await Assert.That(ownVisible).IsTrue();
            await Assert.That(request.TenantFilterTenantId).IsEqualTo((Guid?)rows.A);
        }
        finally
        {
            permission.CommandText = $"REVOKE SELECT ON {tables} FROM \"{login}\"";
            await permission.ExecuteNonQueryAsync();
        }
    }
}
