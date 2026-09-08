using Explore.Domain;
using Explore.Persistence.Schema;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class InstanceBootstrapStateConfiguration : IEntityTypeConfiguration<InstanceBootstrapState>
{
    public void Configure(EntityTypeBuilder<InstanceBootstrapState> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_instance_bootstrap_states_status",
                "status BETWEEN 1 AND 3");
            table.HasCheckConstraint(
                "ck_instance_bootstrap_states_mode",
                "mode BETWEEN 1 AND 2");
            table.HasCheckConstraint(
                "ck_instance_bootstrap_states_provider_kind",
                "provider_kind IS NULL OR provider_kind IN (1, 2, 4)");
            table.HasCheckConstraint(
                "ck_instance_bootstrap_states_deployment_mode",
                "deployment_mode BETWEEN 1 AND 2");
            table.HasCheckConstraint(
                "ck_instance_bootstrap_states_generation",
                "generation > 0");
            table.HasCheckConstraint(
                "ck_instance_bootstrap_states_mode_evidence",
                "(mode = 1 AND provider_kind IS NULL " +
                "AND configuration_fingerprint IS NULL AND selector_fingerprint IS NULL) OR " +
                "(mode = 2 AND provider_kind IS NOT NULL " +
                "AND configuration_fingerprint IS NOT NULL AND selector_fingerprint IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_instance_bootstrap_states_lifecycle",
                "(status = 1 AND superseded_at IS NULL AND completed_at IS NULL " +
                "AND completed_by_user_id IS NULL AND completed_identity_fingerprint IS NULL) OR " +
                "(status = 2 AND mode = 2 AND superseded_at IS NOT NULL " +
                "AND completed_at IS NULL AND completed_by_user_id IS NULL " +
                "AND completed_identity_fingerprint IS NULL) OR " +
                "(status = 3 AND superseded_at IS NULL AND completed_at IS NOT NULL " +
                "AND completed_by_user_id IS NOT NULL AND " +
                "((mode = 1 AND completed_identity_fingerprint IS NULL) OR " +
                "(mode = 2 AND completed_identity_fingerprint IS NOT NULL " +
                "AND completed_identity_fingerprint = selector_fingerprint)))");
            table.HasCheckConstraint(
                "ck_instance_bootstrap_states_terminal_timestamps",
                "(superseded_at IS NULL OR superseded_at >= created_at) AND " +
                "(completed_at IS NULL OR completed_at >= created_at)");
        });

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasValueGenerator<GuidVersion7ValueGenerator>();

        builder.Property(e => e.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(e => e.Mode)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(e => e.ProviderKind)
            .HasConversion<int?>();

        builder.Property(e => e.DeploymentMode)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(e => e.Generation)
            .IsRequired();

        Fingerprint(builder.Property(e => e.ConfigurationFingerprint));
        Fingerprint(builder.Property(e => e.SelectorFingerprint));
        Fingerprint(builder.Property(e => e.CompletedIdentityFingerprint));

        Utc(builder.Property(e => e.CreatedAt));
        Utc(builder.Property(e => e.SupersededAt));
        Utc(builder.Property(e => e.CompletedAt));

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.CompletedByUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.HasIndex(e => e.Generation)
            .IsUnique()
            .IsDescending();

        builder.HasIndex(e => new { e.Status, e.Generation })
            .IsDescending(false, true);
    }

    private static void Fingerprint(PropertyBuilder<string?> property) =>
        property.HasMaxLength(64).IsFixedLength().UsePortableOrdinalAscii();

    private static void Utc(PropertyBuilder<DateTime> property) =>
        property.HasConversion(
            value => value,
            value => DateTime.SpecifyKind(value, DateTimeKind.Utc))
            .IsRequired();

    private static void Utc(PropertyBuilder<DateTime?> property) =>
        property.HasConversion(
            value => value,
            value => value.HasValue
                ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
                : null);
}
