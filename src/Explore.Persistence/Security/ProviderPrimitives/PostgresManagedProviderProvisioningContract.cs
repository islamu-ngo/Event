using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using Model = Explore.Persistence.Security.ManagedProviderProvisioningDataModel;

namespace Explore.Persistence.Security;

/// <summary>
/// Fixed human provisioning DATA on the original enrolled Global connection.
/// Installation is migration-owned; ordinary invocation performs read-only attestation.
/// </summary>
public static partial class PostgresManagedProviderProvisioningContract
{
    internal static string FunctionSql(ExploreDbContext context, string operation) =>
        new HumanPrincipalSourceSql(context).Name(Prefix + operation);
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql()) throw new NotSupportedException("human_provisioning_native_provider_required");
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = BuildSql(context, enrollment);
        await command.ExecuteNonQueryAsync(cancellationToken);
        if (owned is not null) await owned.CommitAsync(cancellationToken);
    }

    public static async Task RequireReadyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        Model.RequireInventory(context);
        if (!context.Database.IsNpgsql() || context.Database.GetDbConnection().State != ConnectionState.Open)
            throw new NotSupportedException("human_provisioning_original_open_postgres_required");
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"DO $ready$ BEGIN {Guard(context, enrollment)} {ExactBodies(context, enrollment)} END $ready$;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static async Task<string> InvokeAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        string operation, Model.Request request, string? before, CancellationToken token)
    {
        if (!Routines.Any(routine => routine.Public && routine.Name == operation)
            || (operation == "acquire") != (before is not null))
            throw new InvalidOperationException("human_provisioning_closed_stage_required");
        await RequireReadyAsync(context, enrollment, token);
        if (context.TenantFilterTenantId is not null || context.IsTenantFilterBypassed || context.IsConfigurationInstanceImportOwner
            || System.Transactions.Transaction.Current is not null
            || operation != "read" && context.Database.CurrentTransaction?.GetDbTransaction()
                is not { IsolationLevel: IsolationLevel.Serializable })
            throw new UnauthorizedAccessException("human_provisioning_original_global_owner_required");
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"SELECT {FunctionSql(context, operation)}(@request{(before is null ? "" : ",@before")})::text";
        command.Parameters.Add(new NpgsqlParameter("request", NpgsqlDbType.Jsonb) { Value = Model.SerializeRequest(request) });
        if (before is not null) command.Parameters.Add(new NpgsqlParameter("before", NpgsqlDbType.Jsonb) { Value = before });
        return (string)(await command.ExecuteScalarAsync(token) ?? throw new InvalidOperationException("human_provisioning_native_result_missing"));
    }

    private static IEnumerable<(string Name, string Signature, string Arguments, string Result, bool Public, string Body)> Functions(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        Routines.Select(routine => (routine.Name, routine.Signature, routine.Arguments, routine.Result, routine.Public,
            routine.Name switch
            {
                "read" => ReadBody(context, enrollment),
                "acquire" => AcquireBody(context, enrollment),
                "compile" => CompileBody(context, enrollment),
                "program" => ProgramBody(context),
                "apply" => ApplyBody(context, enrollment),
                "complete" => CompleteBody(context, enrollment),
                "changed" => ChangedBody(context, enrollment),
                "settlement" => SettlementBody(context),
                _ => throw new InvalidOperationException("human_provisioning_native_inventory_drift")
            }));
}
