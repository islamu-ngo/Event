using System.Data.Common;
using Explore.Application.Contracts.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Explore.Persistence.Security;

public sealed class PostgresTenantSessionInterceptor : DbConnectionInterceptor, IDbCommandInterceptor
{
    public const string CurrentTenantSettingName = "app.current_tenant_id";

    public static readonly PostgresTenantSessionInterceptor Instance = new();
    internal static readonly PostgresTenantSessionInterceptor RuntimeInstance = new(true);
    private readonly bool _verifyRuntimeAuthority;

    public PostgresTenantSessionInterceptor() { }

    private PostgresTenantSessionInterceptor(bool verifyRuntimeAuthority) =>
        _verifyRuntimeAuthority = verifyRuntimeAuthority;

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (_verifyRuntimeAuthority && eventData.Context is ExploreDbContext context)
        {
            context.IsPostgresRuntimeReady = false;
            using var readiness = PostgresTenantRuntimeReadiness.CreateCommand(connection, context);
            PostgresTenantRuntimeReadiness.RequireReady(readiness.ExecuteScalar());
        }
        ApplyTenantSessionState(connection, eventData.Context);
        if (_verifyRuntimeAuthority && eventData.Context is ExploreDbContext ready)
            ready.IsPostgresRuntimeReady = true;
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (_verifyRuntimeAuthority && eventData.Context is ExploreDbContext context)
        {
            context.IsPostgresRuntimeReady = false;
            await using var readiness = PostgresTenantRuntimeReadiness.CreateCommand(connection, context);
            PostgresTenantRuntimeReadiness.RequireReady(await readiness.ExecuteScalarAsync(cancellationToken));
        }
        await ApplyTenantSessionStateAsync(connection, eventData.Context, cancellationToken);
        if (_verifyRuntimeAuthority && eventData.Context is ExploreDbContext ready)
            ready.IsPostgresRuntimeReady = true;
    }

    private static void ApplyTenantSessionState(DbConnection connection, DbContext? dbContext)
    {
        using var command = CreateSetConfigCommand(connection, ResolveTenantId(dbContext));
        command.ExecuteNonQuery();
    }

    private static async Task ApplyTenantSessionStateAsync(
        DbConnection connection,
        DbContext? dbContext,
        CancellationToken cancellationToken)
    {
        using var command = CreateSetConfigCommand(connection, ResolveTenantId(dbContext));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static DbCommand CreateSetConfigCommand(DbConnection connection, Guid? tenantId)
    {
        var command = connection.CreateCommand();
        command.CommandText = "select set_config(@setting_name, @tenant_id, false)";
        command.CommandTimeout = 5;

        command.Parameters.Add(CreateParameter(command, "setting_name", CurrentTenantSettingName));
        command.Parameters.Add(CreateParameter(command, "tenant_id", tenantId?.ToString() ?? string.Empty));

        return command;
    }

    private static DbParameter CreateParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        return parameter;
    }

    private static Guid? ResolveTenantId(DbContext? dbContext)
    {
        if (dbContext is not ExploreDbContext exploreDbContext)
        {
            return null;
        }

        return exploreDbContext.BindTenantLease();
    }

    InterceptionResult<DbDataReader> IDbCommandInterceptor.ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Validate(eventData.Context);
        return result;
    }

    ValueTask<InterceptionResult<DbDataReader>> IDbCommandInterceptor.ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(eventData.Context);
        return ValueTask.FromResult(result);
    }

    InterceptionResult<int> IDbCommandInterceptor.NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Validate(eventData.Context);
        return result;
    }

    ValueTask<InterceptionResult<int>> IDbCommandInterceptor.NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(eventData.Context);
        return ValueTask.FromResult(result);
    }

    InterceptionResult<object> IDbCommandInterceptor.ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Validate(eventData.Context);
        return result;
    }

    ValueTask<InterceptionResult<object>> IDbCommandInterceptor.ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Validate(DbContext? context)
    {
        if (context is ExploreDbContext exploreDbContext)
        {
            if (_verifyRuntimeAuthority && !exploreDbContext.IsPostgresRuntimeReady)
                throw new InvalidOperationException("postgres_tenant_runtime_authority_not_ready");
            exploreDbContext.ValidateTenantLease();
        }
    }
}
