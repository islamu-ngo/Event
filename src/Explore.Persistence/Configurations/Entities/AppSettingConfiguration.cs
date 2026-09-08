// check constraint preventing high-value secrets, and concurrency control via RowVersion.

namespace Explore.Persistence.Configurations.Entities;

using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> builder)
    {
        // Primary key on Key column (not GUID - settings are identified by key)
        builder.HasKey(e => e.ConfigKey);

        builder.Property(e => e.ConfigKey)
            .IsRequired()
            .HasMaxLength(256);

        // Encrypted value - no max length as encrypted data can be variable size
        builder.Property(e => e.EncryptedValue)
            .IsRequired();

        builder.Property(e => e.KeyVersion)
            .IsRequired();

        builder.Property(e => e.EncryptedAt)
            .IsRequired();

        builder.Property(e => e.IsSensitive)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.Description)
            .HasMaxLength(1000);

        builder.Property(e => e.Category)
            .HasMaxLength(100);

        builder.Property(e => e.ValueType)
            .IsRequired()
            .HasConversion<int>();

        // Audit fields
        builder.Property(e => e.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("NOW()");

        // Concurrency token for optimistic locking
        builder.Property(e => e.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        // Indexes for efficient querying
        builder.HasIndex(e => e.Category);
        builder.HasIndex(e => e.KeyVersion);
        builder.HasIndex(e => e.IsSensitive);

        // Check constraint: prevent storing high-value secrets that should use secret manager
        // Database connection strings and master encryption keys must NEVER be stored here
        // Note: Uses snake_case column name 'key' to match UseSnakeCaseNamingConvention()
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_AppSettings_NoHighValueSecrets",
            "config_key NOT LIKE 'Database:%' AND config_key NOT LIKE 'Security:MasterKey%' AND config_key NOT LIKE 'ConnectionStrings:%'"));
    }
}
