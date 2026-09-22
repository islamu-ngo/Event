using Explore.Domain.Keycloak;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class KeycloakOperationConfiguration
    : IEntityTypeConfiguration<KeycloakOperation>
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<KeycloakOperation> builder)
    {
        builder.ToTable("KeycloakOperationReceipts", table =>
        {
            table.HasCheckConstraint(
                "CK_KeycloakOperationReceipts_SetupGeneration",
                "setup_generation > 0");
            table.HasCheckConstraint(
                "CK_KeycloakOperationReceipts_Expiry",
                "expires_at_utc > created_at_utc");
            table.HasCheckConstraint(
                "CK_KeycloakOperationReceipts_Settlement",
                "((state IN ('Previewed','Applying','OutcomeUnknown') "
                + "AND settled_at_utc IS NULL "
                + "AND settled_at_utc_ticks IS NULL) OR "
                + "(state NOT IN ('Previewed','Applying','OutcomeUnknown') "
                + "AND settled_at_utc IS NOT NULL "
                + "AND settled_at_utc_ticks IS NOT NULL))");
            table.HasCheckConstraint(
                "CK_KeycloakOperationReceipts_State",
                "state IN ('Previewed','Applying','Verified','PartiallyApplied',"
                + "'OutcomeUnknown','Conflict','FailedBeforeWrite',"
                + "'Cancelled','Expired')");
        });

        builder.HasKey(operation => operation.Id);
        builder.Property(operation => operation.Id).ValueGeneratedNever();
        builder.Property(operation => operation.Actor)
            .HasMaxLength(256)
            .IsRequired();
        builder.Property(operation => operation.Digest)
            .HasMaxLength(128)
            .IsRequired();
        builder.Property(operation => operation.SetupGeneration).IsRequired();
        builder.Property(operation => operation.CreatedAtUtc).IsRequired();
        builder.Property(operation => operation.ExpiresAtUtc).IsRequired();
        builder.Property(operation => operation.State)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(operation => operation.ConcurrencyStamp)
            .IsConcurrencyToken()
            .ValueGeneratedNever();

        PropertyBuilder<KeycloakChangeSet> changeSet =
            builder.Property(operation => operation.ChangeSet)
                .HasConversion(
                    value => JsonSerializer.Serialize(
                        value.Steps,
                        JsonOptions),
                    value => DeserializeChangeSet(value))
                .HasMaxLength(4096)
                .IsRequired();
        changeSet.Metadata.SetValueComparer(
            new ValueComparer<KeycloakChangeSet>(
                (left, right) =>
                    left != null
                    && right != null
                    && left.Steps.SequenceEqual(right.Steps),
                value => value.Steps.Aggregate(
                    0,
                    (hash, step) => HashCode.Combine(hash, step)),
                value => new KeycloakChangeSet(value.Steps)));

        PropertyBuilder<KeycloakStepOutcomeSet> outcomes =
            builder.Property(operation => operation.StepOutcomes)
                .HasConversion(
                    value => JsonSerializer.Serialize(
                        value.Items,
                        JsonOptions),
                    value => DeserializeOutcomes(value))
                .HasMaxLength(8192)
                .IsRequired();
        outcomes.Metadata.SetValueComparer(
            new ValueComparer<KeycloakStepOutcomeSet>(
                (left, right) =>
                    left != null
                    && right != null
                    && left.Items.SequenceEqual(right.Items),
                value => value.Items.Aggregate(
                    0,
                    (hash, outcome) => HashCode.Combine(hash, outcome)),
                value => new KeycloakStepOutcomeSet(value.Items)));

        builder.OwnsOne(operation => operation.Target, target =>
        {
            target.Property(value => value.InstanceId).IsRequired();
            target.Property(value => value.Authority)
                .HasMaxLength(2048)
                .IsRequired();
            target.Property(value => value.AuthorityKey)
                .HasMaxLength(64)
                .IsRequired();
            target.Property(value => value.Realm)
                .HasMaxLength(256)
                .IsRequired();
            target.Property(value => value.Client)
                .HasMaxLength(256)
                .IsRequired();
            target.HasIndex(value => new
            {
                value.InstanceId,
                value.AuthorityKey,
                value.Realm
            });
        });

        builder.HasIndex(operation => operation.SettledAtUtcTicks);
        builder.HasIndex(operation => operation.State);
    }

    private static KeycloakChangeSet DeserializeChangeSet(string value) =>
        new(
            JsonSerializer.Deserialize<KeycloakChangeStep[]>(
                value,
                JsonOptions)
            ?? throw new InvalidOperationException(
                "The Keycloak change set is invalid."));

    private static KeycloakStepOutcomeSet DeserializeOutcomes(string value) =>
        new(
            JsonSerializer.Deserialize<KeycloakStepOutcome[]>(
                value,
                JsonOptions)
            ?? throw new InvalidOperationException(
                "The Keycloak step outcomes are invalid."));
}
