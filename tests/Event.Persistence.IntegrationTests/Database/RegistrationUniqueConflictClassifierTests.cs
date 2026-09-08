using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Database;

public sealed class RegistrationUniqueConflictClassifierTests
{
    [Test]
    public async Task PostgresExpectedSubmissionConstraints_AreClassifiedFromMetadata()
    {
        await using ExploreDbContext context = CreateContext("PostgreSql");
        RelationalConstraintDescriptor[] constraints = SubmissionConstraints(context);

        foreach (RelationalConstraintDescriptor constraint in constraints)
        {
            await Assert.That(RegistrationUniqueConflictClassifier.IsSubmissionIdentityConflict(
                context,
                new DbUpdateException(
                    "Expected unique race.",
                    CreatePostgresUniqueViolation(constraint.Name)))).IsTrue();
        }
    }

    [Test]
    public async Task PostgresWrongStateAndConstraint_AreNotClassified()
    {
        await using ExploreDbContext context = CreateContext("PostgreSql");
        string expectedConstraint = SubmissionConstraints(context)[0].Name;
        DbUpdateException check = new("Check violation.", new PostgresException(
            "check constraint failed", "ERROR", "ERROR", PostgresErrorCodes.CheckViolation,
            constraintName: expectedConstraint));
        DbUpdateException unrelated = new("Unrelated unique violation.", CreatePostgresUniqueViolation(
            "ux_unrelated_registration_constraint"));

        await Assert.That(RegistrationUniqueConflictClassifier.IsSubmissionIdentityConflict(context, check)).IsFalse();
        await Assert.That(RegistrationUniqueConflictClassifier.IsSubmissionIdentityConflict(context, unrelated)).IsFalse();
    }

    [Test]
    public async Task SqliteFileBackedExpectedSubmissionAndRevisionDuplicates_AreClassified()
    {
        await using ExploreDbContext context = CreateContext("Sqlite");
        string databasePath = Path.Combine(Path.GetTempPath(), $"registration-unique-{Guid.NewGuid():N}.db");
        try
        {
            await using var connection = new SqliteConnection($"Data Source={databasePath}");
            await connection.OpenAsync();
            await ExecuteAsync(connection, """
                CREATE TABLE ie_registration_submissions (
                    tenant_id TEXT NOT NULL,
                    registration_attempt_id TEXT NOT NULL,
                    business_deduplication_key TEXT NOT NULL,
                    registration_provider_binding_id TEXT NULL,
                    provider_submission_id TEXT NULL,
                    provider_response_revision TEXT NULL
                );
                CREATE UNIQUE INDEX ux_registration_submissions_native_identity
                    ON ie_registration_submissions (tenant_id, registration_attempt_id, business_deduplication_key)
                    WHERE registration_provider_binding_id IS NULL;
                CREATE UNIQUE INDEX ux_registration_submissions_provider_identity
                    ON ie_registration_submissions (tenant_id, registration_provider_binding_id, provider_submission_id, provider_response_revision)
                    WHERE registration_provider_binding_id IS NOT NULL;
                CREATE TABLE ie_registration_submission_revisions (
                    tenant_id TEXT NOT NULL,
                    registration_submission_id TEXT NOT NULL,
                    revision_number INTEGER NOT NULL
                );
                CREATE UNIQUE INDEX ux_registration_submission_revisions_submission_revision_number
                    ON ie_registration_submission_revisions (tenant_id, registration_submission_id, revision_number);
                """);

            await ExecuteAsync(connection, "INSERT INTO ie_registration_submissions VALUES ('tenant', 'attempt', 'dedupe', NULL, NULL, NULL);");
            SqliteException native = await DuplicateAsync(connection,
                "INSERT INTO ie_registration_submissions VALUES ('tenant', 'attempt', 'dedupe', NULL, NULL, NULL);");
            await ExecuteAsync(connection, "INSERT INTO ie_registration_submissions VALUES ('tenant', 'attempt-2', 'dedupe-2', 'binding', 'provider', 'revision');");
            SqliteException provider = await DuplicateAsync(connection,
                "INSERT INTO ie_registration_submissions VALUES ('tenant', 'attempt-3', 'dedupe-3', 'binding', 'provider', 'revision');");
            await ExecuteAsync(connection, "INSERT INTO ie_registration_submission_revisions VALUES ('tenant', 'submission', 1);");
            SqliteException revision = await DuplicateAsync(connection,
                "INSERT INTO ie_registration_submission_revisions VALUES ('tenant', 'submission', 1);");

            await Assert.That(native.SqliteErrorCode).IsEqualTo(19);
            await Assert.That(native.SqliteExtendedErrorCode).IsEqualTo(2067);
            await Assert.That(native.Message).Contains(
                "UNIQUE constraint failed: ie_registration_submissions.tenant_id, ie_registration_submissions.registration_attempt_id, ie_registration_submissions.business_deduplication_key");
            await Assert.That(RegistrationUniqueConflictClassifier.IsSubmissionIdentityConflict(
                context,
                new DbUpdateException("SQLite duplicate.", native))).IsTrue();
            await Assert.That(RegistrationUniqueConflictClassifier.IsSubmissionIdentityConflict(
                context,
                new DbUpdateException("SQLite duplicate.", provider))).IsTrue();
            await Assert.That(RegistrationUniqueConflictClassifier.IsRevisionIdentityConflict(
                context,
                new DbUpdateException("SQLite duplicate.", revision))).IsTrue();
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Test]
    public async Task SqliteWrongColumnsAndCheckCode_AreNotClassified()
    {
        await using ExploreDbContext context = CreateContext("Sqlite");
        SqliteException wrongColumns = new(
            "SQLite Error 19: 'UNIQUE constraint failed: ie_registration_submissions.tenant_id, ie_registration_submissions.http_idempotency_key_hash'.",
            19,
            2067);
        SqliteException wrongTable = new(
            "SQLite Error 19: 'UNIQUE constraint failed: other_registration_submissions.tenant_id, other_registration_submissions.registration_attempt_id, other_registration_submissions.business_deduplication_key'.",
            19,
            2067);
        SqliteException check = new(
            "SQLite Error 19: 'CHECK constraint failed: ck_registration_submissions_provider_tuple'.",
            19,
            275);

        await Assert.That(RegistrationUniqueConflictClassifier.IsSubmissionIdentityConflict(
            context,
            new DbUpdateException("SQLite duplicate.", wrongColumns))).IsFalse();
        await Assert.That(RegistrationUniqueConflictClassifier.IsSubmissionIdentityConflict(
            context,
            new DbUpdateException("SQLite duplicate.", wrongTable))).IsFalse();
        await Assert.That(RegistrationUniqueConflictClassifier.IsSubmissionIdentityConflict(
            context,
            new DbUpdateException("SQLite check.", check))).IsFalse();
    }

    [Test]
    [Arguments("IX_ie_incoming_webhook_messages_tenant_id_provider_idem_236751E3")]
    [Arguments("ix_ie_incoming_webhook_messages_tenant_provider_idem_236751e3")]
    public async Task MySqlAndMariaDbDuplicateCode_AcceptsGeneratedShortenedPhysicalKey(string physicalKey)
    {
        await Assert.That(RegistrationUniqueConflictClassifier.IsMySqlDuplicate(1062, physicalKey)).IsTrue();
        await Assert.That(RegistrationUniqueConflictClassifier.IsMySqlDuplicate(1061, physicalKey)).IsFalse();
    }

    private static PostgresException CreatePostgresUniqueViolation(string constraintName) => new(
        "duplicate key value violates unique constraint", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation,
        constraintName: constraintName);

    private static RelationalConstraintDescriptor[] SubmissionConstraints(ExploreDbContext context) =>
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
    ];

    private static ExploreDbContext CreateContext(string provider)
        => ExploreDbContextModelProviderTests.CreateContext(provider);

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<SqliteException> DuplicateAsync(SqliteConnection connection, string sql) =>
        (await Assert.That(() => ExecuteAsync(connection, sql)).Throws<SqliteException>())!;
}
