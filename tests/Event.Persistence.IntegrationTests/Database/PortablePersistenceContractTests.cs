using System.Security.Cryptography;
using System.Text;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Database;

public sealed class PortablePersistenceContractTests
{
    [Test]
    [Arguments("PostgreSql", "PostgreSql")]
    [Arguments("Sqlite", "Sqlite")]
    [Arguments("SqlServer", "SqlServer")]
    [Arguments("MariaDb", "MySql")]
    [Arguments("MySql", "MySql")]
    public async Task ProviderClassifierReturnsExactRuntimeCapability(
        string provider,
        string expected)
    {
        await using ExploreDbContext context =
            ExploreDbContextModelProviderTests.CreateContext(provider);

        await Assert.That(
                RelationalProviderClassifier.Classify(context.Database)
                    .ToString())
            .IsEqualTo(expected);
    }

    [Test]
    [Arguments("Npgsql.EntityFrameworkCore.PostgreSQL", "tenant_schema", "events")]
    [Arguments("Microsoft.EntityFrameworkCore.SqlServer", "tenant_schema", "events")]
    [Arguments("Microsoft.EntityFrameworkCore.Sqlite", null, "ie_events")]
    [Arguments("Microting.EntityFrameworkCore.MySql", null, "ie_events")]
    [Arguments("unsupported", null, "events")]
    public async Task NamespacePolicyAppliesExactSchemaOrPrefix(
        string provider,
        string? expectedSchema,
        string expectedTable)
    {
        ModelBuilder builder = NamespaceModel();

        RelationalModelNamespace.Apply(
            builder,
            provider,
            "tenant_schema");
        RelationalModelNamespace.Apply(
            builder,
            provider,
            "tenant_schema");

        IMutableEntityType entity =
            builder.Model.FindEntityType(typeof(NamespaceEntity))!;
        IMutableEntityType alreadyPrefixed =
            builder.Model.FindEntityType(typeof(PrefixedNamespaceEntity))!;
        await Assert.That(builder.Model.GetDefaultSchema())
            .IsEqualTo(expectedSchema);
        await Assert.That(entity.GetTableName())
            .IsEqualTo(expectedTable);
        await Assert.That(alreadyPrefixed.GetTableName())
            .IsEqualTo("ie_existing");
    }

    [Test]
    public async Task MySqlIdentifierPolicyLeavesOtherProvidersUnchanged()
    {
        ModelBuilder builder = IdentifierModel();
        IMutableIndex index = builder.Model
            .FindEntityType(typeof(IdentifierDependent))!
            .GetIndexes()
            .Single();
        string original = index.GetDatabaseName()!;

        MySqlModelIdentifierPolicy.Apply(
            builder,
            "Microsoft.EntityFrameworkCore.SqlServer");

        await Assert.That(index.GetDatabaseName())
            .IsEqualTo(original);
    }

    [Test]
    public async Task MySqlIdentifierPolicySkipsViewOnlyEntityTypes()
    {
        ModelBuilder builder = new(new ConventionSet());
        var view = builder.Entity<ViewOnlyEntity>();
        view.ToView("identifier_view");
        view.Metadata.SetTableName(null);

        MySqlModelIdentifierPolicy.Apply(
            builder,
            "Microting.EntityFrameworkCore.MySql");

        await Assert.That(view.Metadata.GetTableName()).IsNull();
        await Assert.That(view.Metadata.GetViewName())
            .IsEqualTo("identifier_view");
    }

    [Test]
    public async Task MySqlIdentifierPolicyShortensEveryIdentifierDeterministically()
    {
        ModelBuilder builder = IdentifierModel();
        MySqlModelIdentifierPolicy.Apply(
            builder,
            "Microting.EntityFrameworkCore.MySql");

        IMutableEntityType principal =
            builder.Model.FindEntityType(typeof(IdentifierPrincipal))!;
        IMutableEntityType dependent =
            builder.Model.FindEntityType(typeof(IdentifierDependent))!;
        IMutableKey primaryKey = principal.FindPrimaryKey()!;
        IMutableKey alternateKey = principal.GetKeys()
            .Single(key => !key.IsPrimaryKey());
        IMutableIndex index = dependent.GetIndexes().Single();
        IMutableForeignKey foreignKey =
            dependent.GetForeignKeys().Single();

        await Assert.That(primaryKey.GetName())
            .IsEqualTo(ExpectedShortened(LongName("primary")));
        await Assert.That(alternateKey.GetName())
            .IsEqualTo(ExpectedShortened(LongName("alternate")));
        await Assert.That(index.GetDatabaseName())
            .IsEqualTo(ExpectedShortened(LongName("index")));
        await Assert.That(foreignKey.GetConstraintName())
            .IsEqualTo(ExpectedShortened(LongName("foreign")));
        await Assert.That(primaryKey.GetName()!.Length).IsEqualTo(64);
        await Assert.That(alternateKey.GetName()!.Length).IsEqualTo(64);
        await Assert.That(index.GetDatabaseName()!.Length).IsEqualTo(64);
        await Assert.That(foreignKey.GetConstraintName()!.Length)
            .IsEqualTo(64);
    }

    [Test]
    public async Task MySqlIdentifierPolicyUsesLongConventionBeforeShortExplicitName()
    {
        ModelBuilder builder = new(new ConventionSet());
        var entity = builder.Entity<LongConventionEntity>();
        const string table =
            "table_with_a_name_long_enough_to_force_identifier_shortening";
        const string principalTable =
            "principal_table_with_a_name_long_enough_to_force_shortening";
        entity.ToTable(table);
        entity.HasKey(value => value.Id).HasName("short_pk");
        entity.Property(value => value.PropertyWithAnIntentionallyLongName);
        entity.HasAlternateKey(
                value => value.PropertyWithAnIntentionallyLongName)
            .HasName("short_ak");
        entity.HasIndex(value => value.PropertyWithAnIntentionallyLongName)
            .HasDatabaseName("short_ix");
        builder.Entity<LongConventionPrincipal>()
            .ToTable(principalTable)
            .HasKey(value => value.Id);
        entity.HasOne<LongConventionPrincipal>()
            .WithMany()
            .HasForeignKey(value => value.PrincipalId)
            .HasConstraintName("short_fk");

        MySqlModelIdentifierPolicy.Apply(
            builder,
            "Microting.EntityFrameworkCore.MySql");

        const string convention =
            "pk_table_with_a_name_long_enough_to_force_identifier_shortening_Id";
        await Assert.That(entity.Metadata.FindPrimaryKey()!.GetName())
            .IsEqualTo(ExpectedShortened(convention));
        await Assert.That(entity.Metadata.GetKeys()
                .Single(key => !key.IsPrimaryKey())
                .GetName())
            .IsEqualTo(ExpectedShortened(
                $"ak_{table}_PropertyWithAnIntentionallyLongName"));
        await Assert.That(entity.Metadata.GetIndexes()
                .Single()
                .GetDatabaseName())
            .IsEqualTo(ExpectedShortened(
                $"ix_{table}_PropertyWithAnIntentionallyLongName"));
        await Assert.That(entity.Metadata.GetForeignKeys()
                .Single()
                .GetConstraintName())
            .IsEqualTo(ExpectedShortened(
                $"fk_{table}_{principalTable}_PrincipalId"));
    }

    [Test]
    public async Task MySqlIdentifierPolicyKeepsExactLimitName()
    {
        ModelBuilder builder = new(new ConventionSet());
        string exactLimitName = new('x', 64);
        var entity = builder.Entity<BoundaryIdentifierEntity>();
        entity.ToTable("boundary_identifiers");
        entity.HasKey(value => value.Id);
        entity.HasIndex(value => value.Value)
            .HasDatabaseName(exactLimitName);

        MySqlModelIdentifierPolicy.Apply(
            builder,
            "Microting.EntityFrameworkCore.MySql");

        await Assert.That(entity.Metadata.GetIndexes()
                .Single()
                .GetDatabaseName())
            .IsEqualTo(exactLimitName);
    }

    [Test]
    public async Task ConflictMessageParsersRequireExactConstraintEvidence()
    {
        string[][] expectedColumns =
        [
            ["submissions.tenant_id", "submissions.attempt_id"]
        ];

        await Assert.That(
                RegistrationUniqueConflictClassifier.MatchesSqliteColumns(
                    "UNIQUE constraint failed: submissions.tenant_id, submissions.attempt_id",
                    expectedColumns))
            .IsTrue();
        await Assert.That(
                RegistrationUniqueConflictClassifier.MatchesSqliteColumns(
                    "prefix UNIQUE constraint failed: submissions.tenant_id, submissions.attempt_id.",
                    expectedColumns))
            .IsTrue();
        await Assert.That(
                RegistrationUniqueConflictClassifier.MatchesSqliteColumns(
                    "UNIQUE constraint failed: submissions.tenant_id",
                    expectedColumns))
            .IsFalse();
        await Assert.That(
                RegistrationUniqueConflictClassifier.MatchesSqliteColumns(
                    "UNIQUE constraint failed: submissions.tenant-id, submissions.attempt_id",
                    expectedColumns))
            .IsFalse();
        await Assert.That(
                RegistrationUniqueConflictClassifier.MatchesSqliteColumns(
                    "not a unique constraint",
                    expectedColumns))
            .IsFalse();
        await Assert.That(
                RegistrationUniqueConflictClassifier.MatchesSqliteColumns(
                    "UNIQUE constraint failed: submissions.tenant_id,, submissions.attempt_id",
                    expectedColumns))
            .IsTrue();
        await Assert.That(
                RegistrationUniqueConflictClassifier.MatchesQuotedConstraint(
                    "duplicate key 'schema.expected_key'",
                    ["expected_key"]))
            .IsTrue();
        await Assert.That(
                RegistrationUniqueConflictClassifier.MatchesQuotedConstraint(
                    "duplicate key ' expected_key '",
                    ["expected_key"]))
            .IsTrue();
        await Assert.That(
                RegistrationUniqueConflictClassifier.MatchesQuotedConstraint(
                    "duplicate key `other_key`",
                    ["expected_key"]))
            .IsFalse();
        await Assert.That(
                RegistrationUniqueConflictMessageParser.MatchesConstraint(
                    "schema.expected_key",
                    ["expected_key"]))
            .IsTrue();
    }

    [Test]
    public async Task ConstraintResolverReturnsFinalizedMachineIdentifiers()
    {
        await using ExploreDbContext context =
            ExploreDbContextModelProviderTests.CreateContext("PostgreSql");
        RelationalConstraintDescriptor primaryKey =
            RelationalConstraintDescriptorResolver
                .PrimaryKey<RegistrationSubmission>(context);
        RelationalConstraintDescriptor uniqueIndex =
            RelationalConstraintDescriptorResolver
                .UniqueIndex<RegistrationSubmission>(
                    context,
                    nameof(RegistrationSubmission.TenantId),
                    nameof(RegistrationSubmission.RegistrationAttemptId),
                    nameof(RegistrationSubmission.BusinessDeduplicationKey));
        string exclusion = RelationalConstraintDescriptorResolver
            .ExclusionConstraint<EventSession>(context);

        await Assert.That(primaryKey.Name).IsEqualTo(
            context.Model.FindEntityType(typeof(RegistrationSubmission))!
                .FindPrimaryKey()!
                .GetName());
        await Assert.That(primaryKey.QualifiedColumns.Count).IsEqualTo(1);
        await Assert.That(uniqueIndex.Name).StartsWith("ix_");
        await Assert.That(uniqueIndex.QualifiedColumns.Count).IsEqualTo(3);
        await Assert.That(uniqueIndex.QualifiedColumns.All(
                column => column.StartsWith(
                    "registration_submissions.",
                    StringComparison.Ordinal)))
            .IsTrue();
        await Assert.That(exclusion)
            .IsEqualTo("ex_event_session_room_no_overlap");
    }

    [Test]
    public async Task ConstraintResolverFailsWhenRequiredMetadataIsAbsent()
    {
        await using ExploreDbContext context =
            ExploreDbContextModelProviderTests.CreateContext("Sqlite");

        await Assert.That(() =>
                RelationalConstraintDescriptorResolver
                    .UniqueIndex<RegistrationSubmission>(
                        context,
                        nameof(RegistrationSubmission.Id)))
            .Throws<InvalidOperationException>();
        await Assert.That(() =>
                RelationalConstraintDescriptorResolver
                    .ExclusionConstraint<EventSession>(context))
            .Throws<InvalidOperationException>();
    }

    private static ModelBuilder NamespaceModel()
    {
        ModelBuilder builder = new(new ConventionSet());
        builder.Entity<NamespaceEntity>().ToTable("events");
        builder.Entity<PrefixedNamespaceEntity>()
            .ToTable("ie_existing");
        return builder;
    }

    private static ModelBuilder IdentifierModel()
    {
        ModelBuilder builder = new(new ConventionSet());
        var principal = builder.Entity<IdentifierPrincipal>();
        principal.ToTable("identifier_principals");
        principal.HasKey(value => value.Id)
            .HasName(LongName("primary"));
        principal.HasAlternateKey(value => value.AlternateId)
            .HasName(LongName("alternate"));

        var dependent = builder.Entity<IdentifierDependent>();
        dependent.ToTable("identifier_dependents");
        dependent.HasKey(value => value.Id);
        dependent.HasIndex(value => value.SearchValue)
            .HasDatabaseName(LongName("index"));
        dependent.HasOne<IdentifierPrincipal>()
            .WithMany()
            .HasForeignKey(value => value.PrincipalId)
            .HasConstraintName(LongName("foreign"));
        return builder;
    }

    private static string LongName(string prefix) =>
        $"{prefix}_{new string('x', 72)}";

    private static string ExpectedShortened(string name)
    {
        string hash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(name)))[..8]
            .ToLowerInvariant();
        return name[..55] + "_" + hash;
    }

    private sealed class NamespaceEntity
    {
        public Guid Id { get; set; }
    }

    private sealed class PrefixedNamespaceEntity
    {
        public Guid Id { get; set; }
    }

    private sealed class IdentifierPrincipal
    {
        public Guid Id { get; set; }
        public Guid AlternateId { get; set; }
    }

    private sealed class IdentifierDependent
    {
        public Guid Id { get; set; }
        public Guid PrincipalId { get; set; }
        public string SearchValue { get; set; } = string.Empty;
    }

    private sealed class LongConventionEntity
    {
        public Guid Id { get; set; }
        public Guid PrincipalId { get; set; }
        public string PropertyWithAnIntentionallyLongName { get; set; } =
            string.Empty;
    }

    private sealed class LongConventionPrincipal
    {
        public Guid Id { get; set; }
    }

    private sealed class BoundaryIdentifierEntity
    {
        public Guid Id { get; set; }
        public string Value { get; set; } = string.Empty;
    }

    private sealed class ViewOnlyEntity
    {
        public Guid Id { get; set; }
    }
}
