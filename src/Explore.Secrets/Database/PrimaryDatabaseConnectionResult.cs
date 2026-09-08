namespace Explore.Secrets.Database;

public sealed record PrimaryDatabaseConnectionResult(
    PrimaryDatabaseRole Role,
    PrimaryDatabaseProvider Provider,
    string ConnectionString,
    string RedactedConnectionString,
    string SafeSummary);
