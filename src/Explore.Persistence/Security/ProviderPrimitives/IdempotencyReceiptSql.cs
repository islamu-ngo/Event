using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

internal sealed class IdempotencyReceiptSql(ExploreDbContext context)
{
    internal string Schema => context.GetService<IDbContextOptions>()
        .FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? context.Model.FindEntityType(typeof(IdempotencyRecord))!.GetSchema() ?? "public";

    internal string Name(string name) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier(name, Schema);

    internal string Table(Type type)
    {
        var entity = context.Model.FindEntityType(type)!;
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            entity.GetTableName()!, context.Database.IsNpgsql() ? Schema : entity.GetSchema());
    }

    internal string ColumnName(Type type, string property)
    {
        var entity = context.Model.FindEntityType(type)!;
        return entity.FindProperty(property)!.GetColumnName(
            StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!;
    }

    internal string Column(Type type, string property) => Quote(ColumnName(type, property));
    internal string Quote(string name) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier(name);
}
