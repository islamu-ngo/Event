using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Explore.Persistence.Privacy.ErasureAuthority;

internal sealed class EmbeddedPrivacyErasureAuthorityConnectionInterceptor(
    int busyTimeoutSeconds) : DbConnectionInterceptor
{
    private static readonly ConcurrentDictionary<int, EmbeddedPrivacyErasureAuthorityConnectionInterceptor>
        Instances = new();

    public static EmbeddedPrivacyErasureAuthorityConnectionInterceptor For(int busyTimeoutSeconds) =>
        Instances.GetOrAdd(
            busyTimeoutSeconds,
            static value => new EmbeddedPrivacyErasureAuthorityConnectionInterceptor(value));

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) =>
        Apply(connection);

    public override Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(connection, cancellationToken);

    private void Apply(DbConnection connection)
    {
        using DbCommand command = CreateCommand(connection);
        command.ExecuteNonQuery();
    }

    private async Task ApplyAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using DbCommand command = CreateCommand(connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private DbCommand CreateCommand(DbConnection connection)
    {
        DbCommand command = connection.CreateCommand();
        command.CommandText =
            $"PRAGMA synchronous=FULL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout={busyTimeoutSeconds * 1000};";
        return command;
    }
}
