using Explore.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Npgsql;

namespace Explore.Persistence.Database;

internal static class RegistrationUniqueConflictClassifier
{
    private const string UniqueViolationSqlState = PostgresErrorCodes.UniqueViolation;
    private const int SqliteConstraint = 19;
    private const int SqliteConstraintPrimaryKey = 1555;
    private const int SqliteConstraintUnique = 2067;
    private const int SqlServerUniqueIndexViolation = 2601;
    private const int SqlServerUniqueConstraintViolation = 2627;
    private const int MySqlDuplicateEntry = 1062;

    internal static bool IsSubmissionIdentityConflict(
        ExploreDbContext context,
        DbUpdateException exception) =>
        IsExpectedConflict(
            exception,
            [
                RelationalConstraintDescriptorResolver.UniqueIndex<RegistrationSubmission>(
                    context,
                    nameof(RegistrationSubmission.TenantId),
                    nameof(RegistrationSubmission.RegistrationAttemptId),
                    nameof(RegistrationSubmission.BusinessDeduplicationKey)),
                RelationalConstraintDescriptorResolver.UniqueIndex<RegistrationSubmission>(
                    context,
                    nameof(RegistrationSubmission.TenantId),
                    nameof(RegistrationSubmission.RegistrationProviderBindingId),
                    nameof(RegistrationSubmission.ProviderSubmissionId),
                    nameof(RegistrationSubmission.ProviderResponseRevision))
            ]);

    internal static bool IsRevisionIdentityConflict(
        ExploreDbContext context,
        DbUpdateException exception) =>
        IsExpectedConflict(
            exception,
            [
                RelationalConstraintDescriptorResolver.UniqueIndex<RegistrationSubmissionRevision>(
                    context,
                    nameof(RegistrationSubmissionRevision.TenantId),
                    nameof(RegistrationSubmissionRevision.RegistrationSubmissionId),
                    nameof(RegistrationSubmissionRevision.RevisionNumber))
            ]);

    internal static bool IsProviderUniqueConflict(DbUpdateException exception)
    {
        for (Exception? current = exception.InnerException; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: UniqueViolationSqlState } ||
                current is SqliteException
                {
                    SqliteErrorCode: SqliteConstraint,
                    SqliteExtendedErrorCode: SqliteConstraintPrimaryKey or SqliteConstraintUnique
                } ||
                current is SqlException { Number: SqlServerUniqueIndexViolation or SqlServerUniqueConstraintViolation } ||
                current is MySqlException mySql && IsMySqlDuplicate(mySql.Number, mySql.Message))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsMySqlDuplicate(int number, string physicalKeyName) =>
        RegistrationUniqueConflictMessageParser.IsMySqlDuplicate(
            number,
            physicalKeyName);

    private static bool IsExpectedConflict(
        DbUpdateException exception,
        IReadOnlyList<RelationalConstraintDescriptor> expectedConstraints)
    {
        for (Exception? current = exception.InnerException; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres &&
                postgres.SqlState == UniqueViolationSqlState &&
                RegistrationUniqueConflictMessageParser.MatchesConstraint(
                    postgres.ConstraintName,
                    expectedConstraints.Select(constraint => constraint.Name)))
            {
                return true;
            }

            if (current is SqliteException sqlite &&
                sqlite.SqliteErrorCode == SqliteConstraint &&
                sqlite.SqliteExtendedErrorCode is SqliteConstraintPrimaryKey or SqliteConstraintUnique &&
                RegistrationUniqueConflictMessageParser.MatchesSqliteColumns(
                    sqlite.Message,
                    expectedConstraints.Select(constraint => constraint.QualifiedColumns)))
            {
                return true;
            }

            if (current is SqlException sqlServer &&
                sqlServer.Number is SqlServerUniqueIndexViolation or SqlServerUniqueConstraintViolation &&
                RegistrationUniqueConflictMessageParser.MatchesQuotedConstraint(
                    sqlServer.Message,
                    expectedConstraints.Select(constraint => constraint.Name)))
            {
                return true;
            }

            if (current is MySqlException mySql &&
                mySql.Number == MySqlDuplicateEntry &&
                RegistrationUniqueConflictMessageParser.MatchesQuotedConstraint(
                    mySql.Message,
                    expectedConstraints.Select(constraint => constraint.Name)))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool MatchesQuotedConstraint(
        string message,
        IEnumerable<string> expected) =>
        RegistrationUniqueConflictMessageParser.MatchesQuotedConstraint(
            message,
            expected);

    internal static bool MatchesSqliteColumns(
        string message,
        IEnumerable<IReadOnlyList<string>> expectedColumnSets) =>
        RegistrationUniqueConflictMessageParser.MatchesSqliteColumns(
            message,
            expectedColumnSets);
}
