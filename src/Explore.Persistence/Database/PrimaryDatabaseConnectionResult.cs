namespace Explore.Persistence.Database;

public sealed record PrimaryDatabaseConnectionResult(
    PrimaryDatabaseRole Role,
    PrimaryDatabaseProvider Provider,
    string ConnectionString,
    string RedactedConnectionString,
    string SafeSummary);
