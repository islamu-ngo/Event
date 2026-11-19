using System.Text.Json;
using Explore.Domain;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class InstanceBootstrapStateQuery
{
    internal static async Task<InstanceBootstrapState?> ReadAsync(
        ExploreDbContext context, Func<PostgresNativeQueueEnrollment> enrollment, CancellationToken cancellationToken)
    {
        if (!context.Database.IsNpgsql())
            return await context.InstanceBootstrapStates.AsNoTracking()
                .OrderByDescending(row => row.Generation)
                .ThenByDescending(row => row.CreatedAt)
                .ThenByDescending(row => row.Id)
                .FirstOrDefaultAsync(cancellationToken);

        await context.Database.OpenConnectionAsync(cancellationToken);
        var state = await PostgresProviderConfigurationContract.ReadAsync(
            context, enrollment(), null, cancellationToken);
        using var document = JsonDocument.Parse(state.Bootstrap);
        var rows = document.RootElement.EnumerateArray()
            .OrderByDescending(row => row.GetProperty(nameof(InstanceBootstrapState.Generation)).GetInt64())
            .ThenByDescending(row => row.GetProperty(nameof(InstanceBootstrapState.CreatedAt)).GetDateTime())
            .ThenByDescending(row => row.GetProperty(nameof(InstanceBootstrapState.Id)).GetGuid())
            .ToArray();
        if (rows.Length == 0)
            return null;

        return MaterializeJsonRow<InstanceBootstrapState>(context, rows[0]);
    }

    internal static TEntity MaterializeJsonRow<TEntity>(ExploreDbContext context, JsonElement row) where TEntity : class
    {
        var entity = context.Model.FindEntityType(typeof(TEntity))!;
        var properties = entity.GetProperties().ToArray();
        object?[] values = new object?[properties.Max(property => property.GetIndex()) + 1];
        foreach (var property in properties)
            values[property.GetIndex()] = row.GetProperty(property.Name).Deserialize(property.ClrType);
        var materializer = context.GetService<IStructuralTypeMaterializerSource>().GetMaterializer(entity);
        return (TEntity)materializer(new MaterializationContext(new ValueBuffer(values), context));
    }
}
