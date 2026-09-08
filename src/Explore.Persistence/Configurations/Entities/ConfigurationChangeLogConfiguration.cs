// Indexes on UserId, SettingKey, and Timestamp for efficient audit trail queries.

namespace Explore.Persistence.Configurations.Entities;

using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class ConfigurationChangeLogConfiguration : IEntityTypeConfiguration<ConfigurationChangeLog>
{
    public void Configure(EntityTypeBuilder<ConfigurationChangeLog> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasDefaultValueSql("uuidv7()");

        builder.Property(e => e.Timestamp)
            .IsRequired()
            .HasDefaultValueSql("NOW()");

        builder.Property(e => e.SettingKey)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(e => e.OldValue)
            .HasColumnType("text");

        builder.Property(e => e.NewValue)
            .IsRequired()
            .HasColumnType("text");

        builder.Ignore(e => e.Scope);

        builder.Property(e => e.SettingScopeId)
            .IsRequired();

        builder.HasOne(e => e.SettingScope)
            .WithMany()
            .HasForeignKey(e => e.SettingScopeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(e => e.ActionType)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("NOW()");

        // Indexes for audit trail queries
        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.SettingKey);
        builder.HasIndex(e => e.Timestamp);
        builder.HasIndex(e => new { e.SettingScopeId, e.ScopeId });
    }
}
