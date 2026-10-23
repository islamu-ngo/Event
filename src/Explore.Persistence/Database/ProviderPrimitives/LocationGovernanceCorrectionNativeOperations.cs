using System.Data;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Database.ProviderPrimitives;

/// <summary>Retains the fixed correction receipt in the existing governance transaction.</summary>
internal sealed class LocationGovernanceCorrectionNativeOperations
{
    private readonly ExploreDbContext context;

    private LocationGovernanceCorrectionNativeOperations(ExploreDbContext context) => this.context = context;

    internal static LocationGovernanceCorrectionNativeOperations? For(ExploreDbContext context) =>
        context.Database.IsNpgsql() ? new(context) : null;

    internal async Task RetainAsync(Guid messageId, Guid auditId, CancellationToken token)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = $"SELECT {PostgresLocationPrivacyGovernanceContract.RetainReceiptFunctionSql(context)}"
            + "(@tenant, @message, @audit)";
        foreach (var (name, value) in new[]
        {
            ("tenant", context.TenantFilterTenantId
                ?? throw new InvalidOperationException("Correction custody requires its exact tenant lease.")),
            ("message", messageId), ("audit", auditId)
        })
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.DbType = DbType.Guid;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }
        await command.ExecuteScalarAsync(token);
    }
}
