namespace Explore.Persistence.Configurations.Entities;

using Explore.Domain.Secrets;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class SecretBindingConfiguration : IEntityTypeConfiguration<SecretBinding>
{
    public void Configure(EntityTypeBuilder<SecretBinding> builder)
    {
        builder.HasKey(e => e.Id);

        // UUID v7 generation for better index performance
        builder.Property(e => e.Id)
            .HasValueGenerator<GuidVersion7ValueGenerator>();

        builder.Property(e => e.SettingKey)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(e => e.Qualifier)
            .IsRequired()
            .HasMaxLength(128)
            .HasDefaultValue(string.Empty);

        builder.Ignore(e => e.Scope);

        builder.Property(e => e.SettingScopeId)
            .IsRequired();

        builder.HasOne(e => e.SettingScope)
            .WithMany()
            .HasForeignKey(e => e.SettingScopeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(e => e.ScopeId);

        builder.Ignore(e => e.SourceType);

        builder.Property(e => e.SecretSourceTypeId)
            .IsRequired();

        builder.HasOne(e => e.SecretSourceType)
            .WithMany()
            .HasForeignKey(e => e.SecretSourceTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        // Normalized metadata columns. Exactly one group is populated per row (enforced by CHECK below).
        builder.Property(e => e.InfisicalEnvironment).HasMaxLength(64);
        builder.Property(e => e.InfisicalPath).HasMaxLength(512);
        builder.Property(e => e.InfisicalKey).HasMaxLength(256);
        builder.Property(e => e.EnvironmentVariableName).HasMaxLength(256);

        builder.Property(e => e.IsLocked)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Ignore(e => e.LastValidationResult);

        builder.Property(e => e.SecretValidationStatusId)
            .IsRequired()
            .HasDefaultValue((int)Explore.Domain.Enums.SecretValidationResult.NotValidated);

        builder.HasOne(e => e.SecretValidationStatus)
            .WithMany()
            .HasForeignKey(e => e.SecretValidationStatusId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(e => e.LastValidationError)
            .HasMaxLength(1000);

        builder.Property(e => e.LastValidatedAt);
        builder.Property(e => e.LastValidatedBy);

        builder.Property(e => e.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("NOW()");

        builder.Property(e => e.CreatedBy);
        builder.Property(e => e.UpdatedAt);
        builder.Property(e => e.UpdatedBy);

        // Filtered unique indexes for Postgres NULL semantics (Oracle-mandated for correct uniqueness on nullable ScopeId):
        // - Instance rows: one binding per (SettingKey) where scope_id IS NULL
        builder.HasIndex(e => new { e.SettingKey, e.Qualifier })
            .IsUnique()
            .HasFilter("scope_id IS NULL")
            .HasDatabaseName("ix_secret_bindings_setting_key_instance_unique");

        // - Tenant rows: one binding per (SettingKey, ScopeId) where scope_id IS NOT NULL
        builder.HasIndex(e => new { e.SettingKey, e.ScopeId, e.Qualifier })
            .IsUnique()
            .HasFilter("scope_id IS NOT NULL")
            .HasDatabaseName("ix_secret_bindings_setting_key_scope_id_tenant_unique");

        // Lookup index by scope for bulk listing
        builder.HasIndex(e => new { e.SettingScopeId, e.ScopeId })
            .HasDatabaseName("ix_secret_bindings_setting_scope_id_scope_id");

        builder.ToTable(t =>
        {
            // CHECK: scope/scope_id consistency (Instance=1 requires ScopeId NULL, Tenant=2 requires ScopeId NOT NULL).
            t.HasCheckConstraint(
                "ck_secret_bindings_setting_scope_scope_id",
                "(setting_scope_id = 1 AND scope_id IS NULL) OR (setting_scope_id = 2 AND scope_id IS NOT NULL)");

            // CHECK: exactly one metadata group populated per SourceType.
            //  SecretSourceType 0 (Infisical): env/path/key all NOT NULL, other groups NULL.
            //  SecretSourceType 1 (EnvironmentVariable): variable_name NOT NULL, other groups NULL.
            t.HasCheckConstraint(
                "ck_secret_bindings_source_metadata",
                "(secret_source_type_id = 0 AND infisical_environment IS NOT NULL AND infisical_path IS NOT NULL AND infisical_key IS NOT NULL " +
                "  AND environment_variable_name IS NULL) " +
                "OR (secret_source_type_id = 1 AND environment_variable_name IS NOT NULL " +
                "  AND infisical_environment IS NULL AND infisical_path IS NULL AND infisical_key IS NULL)");
        });
    }
}
