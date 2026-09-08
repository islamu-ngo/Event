namespace Event.Persistence.IntegrationTests.SetupLive;

using Event.Persistence.IntegrationTests.Database;
using Explore.Domain.SetupLive;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

public sealed class SetupLiveModelInvariantTests
{
    [Test]
    [Arguments("PostgreSql")]
    [Arguments("MariaDb")]
    [Arguments("MySql")]
    [Arguments("Sqlite")]
    [Arguments("SqlServer")]
    public async Task ModelContractIsIdenticalAcrossEveryPrimaryProvider(
        string provider)
    {
        using ExploreDbContext context =
            ExploreDbContextModelProviderTests.CreateContext(provider);
        IModel model = context.GetService<IDesignTimeModel>().Model;
        IEntityType enrollment = RequireEntity<SetupTargetEnrollment>(model);
        IEntityType claim = RequireEntity<SetupEnrollmentIssuanceClaim>(model);
        IEntityType operation = RequireEntity<SetupSecretBindingOperation>(model);

        await Assert.That(LogicalTableName(enrollment)).IsEqualTo(
            "setup_target_enrollments");
        await Assert.That(LogicalTableName(claim)).IsEqualTo(
            "setup_enrollment_issuance_claims");
        await Assert.That(LogicalTableName(operation)).IsEqualTo(
            "setup_secret_binding_operations");
        string? expectedSchema = provider is "PostgreSql" or "SqlServer"
            ? RelationalModelNamespace.DefaultSchema
            : null;
        string expectedPrefix = expectedSchema is null ? "ie_" : string.Empty;
        await Assert.That(enrollment.GetTableName()).IsEqualTo(
            expectedPrefix + "setup_target_enrollments");
        await Assert.That(claim.GetTableName()).IsEqualTo(
            expectedPrefix + "setup_enrollment_issuance_claims");
        await Assert.That(operation.GetTableName()).IsEqualTo(
            expectedPrefix + "setup_secret_binding_operations");
        await Assert.That(enrollment.GetSchema()).IsEqualTo(expectedSchema);
        await Assert.That(claim.GetSchema()).IsEqualTo(expectedSchema);
        await Assert.That(operation.GetSchema()).IsEqualTo(expectedSchema);
        await Assert.That(enrollment.FindDeclaredQueryFilter(QueryFilterNames.Tenant))
            .IsNotNull();
        await Assert.That(claim.FindDeclaredQueryFilter(QueryFilterNames.Tenant))
            .IsNotNull();
        await Assert.That(operation.FindDeclaredQueryFilter(QueryFilterNames.Tenant))
            .IsNotNull();

        await Assert.That(enrollment.FindProperty(nameof(
            SetupTargetEnrollment.ConcurrencyStamp))!.IsConcurrencyToken)
            .IsTrue();
        await Assert.That(operation.FindProperty(nameof(
            SetupSecretBindingOperation.ConcurrencyStamp))!.IsConcurrencyToken)
            .IsTrue();
        await Assert.That(MaxLength(enrollment, nameof(
            SetupTargetEnrollment.ChallengeDigest))).IsEqualTo(64);
        await Assert.That(MaxLength(enrollment, nameof(
            SetupTargetEnrollment.CapabilityDigest))).IsEqualTo(64);
        await Assert.That(MaxLength(enrollment, nameof(
            SetupTargetEnrollment.ScopeDigest))).IsEqualTo(64);
        await Assert.That(MaxLength(claim, nameof(
            SetupEnrollmentIssuanceClaim.RequestFingerprint))).IsEqualTo(64);
        await Assert.That(MaxLength(operation, nameof(
            SetupSecretBindingOperation.RequestFingerprint))).IsEqualTo(64);
        await Assert.That(MaxLength(operation, nameof(
            SetupSecretBindingOperation.SecretValueCommitment))).IsEqualTo(64);
        await Assert.That(MaxLength(operation, nameof(
            SetupSecretBindingOperation.BindingKey))).IsEqualTo(32);
        string expectedCollation = provider switch
        {
            "PostgreSql" => "C",
            "Sqlite" => "BINARY",
            "SqlServer" => "Latin1_General_100_BIN2",
            "MariaDb" or "MySql" => "ascii_bin",
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };
        IProperty[] ordinalEvidence =
        [
            enrollment.FindProperty(nameof(SetupTargetEnrollment.ChallengeDigest))!,
            enrollment.FindProperty(nameof(SetupTargetEnrollment.CapabilityDigest))!,
            enrollment.FindProperty(nameof(SetupTargetEnrollment.ScopeDigest))!,
            claim.FindProperty(nameof(SetupEnrollmentIssuanceClaim.RequestFingerprint))!,
            operation.FindProperty(nameof(SetupSecretBindingOperation.BindingKey))!,
            operation.FindProperty(nameof(SetupSecretBindingOperation.RequestFingerprint))!,
            operation.FindProperty(nameof(SetupSecretBindingOperation.SecretValueCommitment))!
        ];
        foreach (IProperty property in ordinalEvidence)
        {
            await Assert.That(property.GetCollation()).IsEqualTo(expectedCollation);
            await Assert.That(property.GetCharSet()).IsEqualTo(
                provider is "MariaDb" or "MySql" ? "ascii" : null);
        }

        await Assert.That(HasUniqueIndex(
            claim,
            nameof(SetupEnrollmentIssuanceClaim.TenantId),
            nameof(SetupEnrollmentIssuanceClaim.OperationKey))).IsTrue();
        await Assert.That(HasUniqueIndex(
            operation,
            nameof(SetupSecretBindingOperation.TenantId),
            nameof(SetupSecretBindingOperation.OperationKey))).IsTrue();
        await Assert.That(HasEnrollmentForeignKey(
            claim,
            nameof(SetupEnrollmentIssuanceClaim.TenantId),
            nameof(SetupEnrollmentIssuanceClaim.EnrollmentId),
            nameof(SetupEnrollmentIssuanceClaim.ActorId))).IsTrue();
        await Assert.That(HasEnrollmentForeignKey(
            operation,
            nameof(SetupSecretBindingOperation.TenantId),
            nameof(SetupSecretBindingOperation.EnrollmentId),
            nameof(SetupSecretBindingOperation.ActorId))).IsTrue();

        await AssertExactPropertiesAsync(enrollment,
            nameof(SetupTargetEnrollment.ActorId),
            nameof(SetupTargetEnrollment.CapabilityDigest),
            nameof(SetupTargetEnrollment.ChallengeDigest),
            nameof(SetupTargetEnrollment.ConcurrencyStamp),
            nameof(SetupTargetEnrollment.CreatedAt),
            nameof(SetupTargetEnrollment.CreatedBy),
            nameof(SetupTargetEnrollment.ExpiresAt),
            nameof(SetupTargetEnrollment.ExpiredAt),
            nameof(SetupTargetEnrollment.Generation),
            nameof(SetupTargetEnrollment.Id),
            nameof(SetupTargetEnrollment.RevokedAt),
            nameof(SetupTargetEnrollment.ScopeDigest),
            nameof(SetupTargetEnrollment.State),
            nameof(SetupTargetEnrollment.TenantId),
            nameof(SetupTargetEnrollment.UpdatedAt),
            nameof(SetupTargetEnrollment.UpdatedBy));
        await AssertExactPropertiesAsync(claim,
            nameof(SetupEnrollmentIssuanceClaim.ActorId),
            nameof(SetupEnrollmentIssuanceClaim.ClaimedAt),
            nameof(SetupEnrollmentIssuanceClaim.EnrollmentGeneration),
            nameof(SetupEnrollmentIssuanceClaim.EnrollmentId),
            nameof(SetupEnrollmentIssuanceClaim.Id),
            nameof(SetupEnrollmentIssuanceClaim.OperationKey),
            nameof(SetupEnrollmentIssuanceClaim.RequestFingerprint),
            nameof(SetupEnrollmentIssuanceClaim.TenantId));
        await AssertExactPropertiesAsync(operation,
            nameof(SetupSecretBindingOperation.ActorId),
            nameof(SetupSecretBindingOperation.BindingKey),
            nameof(SetupSecretBindingOperation.CommitmentKeyVersion),
            nameof(SetupSecretBindingOperation.ConcurrencyStamp),
            nameof(SetupSecretBindingOperation.CreatedAt),
            nameof(SetupSecretBindingOperation.CreatedBy),
            nameof(SetupSecretBindingOperation.EnrollmentGeneration),
            nameof(SetupSecretBindingOperation.EnrollmentId),
            nameof(SetupSecretBindingOperation.Id),
            nameof(SetupSecretBindingOperation.OperationKey),
            nameof(SetupSecretBindingOperation.Outcome),
            nameof(SetupSecretBindingOperation.RequestFingerprint),
            nameof(SetupSecretBindingOperation.SecretValueCommitment),
            nameof(SetupSecretBindingOperation.SettledAt),
            nameof(SetupSecretBindingOperation.State),
            nameof(SetupSecretBindingOperation.TenantId),
            nameof(SetupSecretBindingOperation.UpdatedAt),
            nameof(SetupSecretBindingOperation.UpdatedBy));
        await Assert.That(CheckConstraintNames(enrollment)).Contains(
            "ck_setup_target_enrollments_generation");
        await Assert.That(CheckConstraintNames(claim)).Contains(
            "ck_setup_enrollment_claims_generation");
        await Assert.That(CheckConstraintNames(operation)).Contains(
            "ck_setup_secret_operations_versions");

        await Assert.That(typeof(ExploreDbContext).GetProperty(
            "SetupTargetEnrollments")?.PropertyType).IsEqualTo(
                typeof(DbSet<SetupTargetEnrollment>));
        await Assert.That(typeof(ExploreDbContext).GetProperty(
            "SetupEnrollmentIssuanceClaims")?.PropertyType).IsEqualTo(
                typeof(DbSet<SetupEnrollmentIssuanceClaim>));
        await Assert.That(typeof(ExploreDbContext).GetProperty(
            "SetupSecretBindingOperations")?.PropertyType).IsEqualTo(
                typeof(DbSet<SetupSecretBindingOperation>));
    }

    private static IEntityType RequireEntity<TEntity>(IModel model) =>
        model.FindEntityType(typeof(TEntity))
        ?? throw new InvalidOperationException(
            $"The Setup live model is missing {typeof(TEntity).Name}.");

    private static string LogicalTableName(IEntityType entity)
    {
        string tableName = entity.GetTableName()
            ?? throw new InvalidOperationException("Entity has no table mapping.");
        return tableName.StartsWith("ie_", StringComparison.Ordinal)
            ? tableName["ie_".Length..]
            : tableName;
    }

    private static int? MaxLength(IEntityType entity, string propertyName) =>
        entity.FindProperty(propertyName)?.GetMaxLength();

    private static string[] CheckConstraintNames(IEntityType entity) =>
        entity.GetCheckConstraints()
            .Select(constraint => constraint.Name!)
            .ToArray();

    private static async Task AssertExactPropertiesAsync(
        IEntityType entity,
        params string[] expected)
    {
        string[] actual = entity.GetProperties()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        await Assert.That(actual).IsEquivalentTo(expected);
    }

    private static bool HasUniqueIndex(
        IEntityType entity,
        params string[] propertyNames) =>
        entity.GetIndexes().Any(index =>
            index.IsUnique
            && index.Properties.Select(property => property.Name)
                .SequenceEqual(propertyNames, StringComparer.Ordinal));

    private static bool HasEnrollmentForeignKey(
        IEntityType entity,
        params string[] propertyNames) =>
        entity.GetForeignKeys().Any(foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(SetupTargetEnrollment)
            && foreignKey.Properties.Select(property => property.Name)
                .SequenceEqual(propertyNames, StringComparer.Ordinal));
}
