using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Explore.Persistence.Privacy.ErasureAuthority;

internal sealed class EmbeddedPrivacyErasureMigrationsSqlGenerator(
    MigrationsSqlGeneratorDependencies dependencies,
    IRelationalAnnotationProvider annotations)
    : SqliteMigrationsSqlGenerator(dependencies, annotations)
{
    public override IReadOnlyList<MigrationCommand> Generate(
        IReadOnlyList<MigrationOperation> operations,
        IModel? model = null,
        MigrationsSqlGenerationOptions options = MigrationsSqlGenerationOptions.Default)
    {
        var prepared = new List<MigrationOperation>(operations.Count);
        foreach (var operation in operations)
        {
            if (operation is AlterColumnOperation
                {
                    Table: "ie_authority_counter", Name: "store_id", Schema: null,
                    IsNullable: false, OldColumn: { IsNullable: true }, DefaultValueSql: { } defaultSql
                } column)
            {
                // SQLite's rebuild explicitly copies nulls instead of evaluating expression defaults.
                var sql = Dependencies.SqlGenerationHelper;
                var table = sql.DelimitIdentifier(column.Table);
                var name = sql.DelimitIdentifier(column.Name);
                prepared.Add(new SqlOperation
                {
                    Sql = $"UPDATE {table} SET {name} = {defaultSql} WHERE {name} IS NULL;"
                });
            }
            prepared.Add(operation);
        }
        return base.Generate(prepared, model, options);
    }
}
