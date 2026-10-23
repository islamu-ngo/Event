using System.Data.Common;
using Explore.Application.Contracts.Infrastructure;
using Explore.Persistence.Identity;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Explore.Persistence.Security;

public sealed class PostgresTenantSessionInterceptor : DbConnectionInterceptor, IDbCommandInterceptor
{
    public const string CurrentTenantSettingName = "app.current_tenant_id";

    public static readonly PostgresTenantSessionInterceptor Instance = new();
    internal static readonly PostgresTenantSessionInterceptor RuntimeInstance = new(true);
    private readonly bool _verifyRuntimeAuthority;
    private readonly string? _enrolledGlobalLogin;
    private readonly IdentityWriterProfile? _identityWriter;

    public PostgresTenantSessionInterceptor() { }

    private PostgresTenantSessionInterceptor(bool verifyRuntimeAuthority, string? enrolledGlobalLogin = null)
    {
        _verifyRuntimeAuthority = verifyRuntimeAuthority;
        _enrolledGlobalLogin = enrolledGlobalLogin;
    }

    internal static PostgresTenantSessionInterceptor ForGlobalLogin(string enrolledGlobalLogin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(enrolledGlobalLogin);
        return new PostgresTenantSessionInterceptor(true, enrolledGlobalLogin);
    }

    private PostgresTenantSessionInterceptor(IdentityWriterProfile identityWriter)
    {
        _verifyRuntimeAuthority = true;
        _identityWriter = identityWriter;
    }

    internal static PostgresTenantSessionInterceptor ForLocalIdentityWriter(PostgresLocalIdentityWriterEnrollment enrollment,
        IdentityDatabaseTopology topology, LocalIdentityWriterInventory.Store store)
    {
        ArgumentNullException.ThrowIfNull(enrollment);
        enrollment.Validate();
        if (!Enum.IsDefined(topology)) throw new ArgumentOutOfRangeException(nameof(topology));
        if (!Enum.IsDefined(store)) throw new ArgumentOutOfRangeException(nameof(store));
        return new(new IdentityWriterProfile(enrollment, topology, store));
    }

    private DbCommand CreateReadinessCommand(DbConnection connection, DbContext context) =>
        _identityWriter is { } writer
            ? PostgresLocalIdentityWriterPermissions.CreateReadinessCommand(connection, context,
                writer.Enrollment, writer.Topology, writer.Store)
            : context is ExploreDbContext application
                ? PostgresTenantRuntimeReadiness.CreateCommand(connection, application, _enrolledGlobalLogin)
                : throw new InvalidOperationException("postgres_tenant_runtime_context_required");

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (_verifyRuntimeAuthority && eventData.Context is { } context)
        {
            if (context is ExploreDbContext application) application.IsPostgresRuntimeReady = false;
            using var readiness = CreateReadinessCommand(connection, context);
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
        if (_verifyRuntimeAuthority && eventData.Context is { } context)
        {
            if (context is ExploreDbContext application) application.IsPostgresRuntimeReady = false;
            await using var readiness = CreateReadinessCommand(connection, context);
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

    private sealed record IdentityWriterProfile(PostgresLocalIdentityWriterEnrollment Enrollment, IdentityDatabaseTopology Topology,
        LocalIdentityWriterInventory.Store Store);
}
