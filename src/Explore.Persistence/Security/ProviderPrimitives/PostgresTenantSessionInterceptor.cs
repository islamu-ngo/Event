using System.Data.Common;
using Explore.Application.Contracts.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Explore.Persistence.Security;

public sealed class PostgresTenantSessionInterceptor : DbConnectionInterceptor
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

        ITenantContext? tenantContext = exploreDbContext.TenantContext;
        if (tenantContext is null)
        {
            return null;
        }

        var tenantId = tenantContext.TenantId;
        return tenantId == Guid.Empty ? null : tenantId;
    }
}
