using System.Data.Common;
using Explore.Application.Contracts.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Explore.Persistence.Security;

public sealed class PostgresTenantSessionInterceptor : DbConnectionInterceptor, IDbCommandInterceptor
{
    public const string CurrentTenantSettingName = "app.current_tenant_id";

    public static readonly PostgresTenantSessionInterceptor Instance = new();

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ApplyTenantSessionState(connection, eventData.Context);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await ApplyTenantSessionStateAsync(connection, eventData.Context, cancellationToken);
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
            exploreDbContext.ValidateTenantLease();
        }
    }
}
