using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Explore.Persistence.Database.ProviderPrimitives;

/// <summary>
/// Captures the physical connection without transferring its lifetime or transaction ownership.
/// The caller retains context-lease, authority, and transaction-state validation.
/// </summary>
internal sealed class RelationalConnectionIdentity
{
    private readonly DbConnection connection;

    private RelationalConnectionIdentity(DbConnection connection) => this.connection = connection;

    internal static RelationalConnectionIdentity Capture(ExploreDbContext context) =>
        new(context.Database.GetDbConnection());

    internal ConnectionState State => connection.State;

    internal DbConnection Connection => connection;

    internal string DataSource => connection.DataSource;

    internal string Database => connection.Database;

    internal int? BackendProcessId => connection is NpgsqlConnection postgres ? postgres.ProcessID : null;

    internal int PostgresProcessId => ((NpgsqlConnection)connection).ProcessID;

    internal event StateChangeEventHandler StateChange
    {
        add => connection.StateChange += value;
        remove => connection.StateChange -= value;
    }

    internal bool Matches(ExploreDbContext context) =>
        ReferenceEquals(connection, context.Database.GetDbConnection());

    internal bool Owns(DbTransaction transaction) =>
        ReferenceEquals(connection, transaction.Connection);

    /// <summary>Closes the captured connector after its caller proves ownership of a failed initial opening.</summary>
    internal Task CloseAfterFailedOpenAsync() => connection.CloseAsync();
}
