using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class CustomPropertyProjectionStatusConfiguration : IEntityTypeConfiguration<CustomPropertyProjectionStatus>
{
    public void Configure(EntityTypeBuilder<CustomPropertyProjectionStatus> builder)
    {

        builder.HasKey(e => new { e.ProjectionName, e.ProjectionVersion, e.TenantId });

        builder.Property(e => e.ProjectionName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.State)
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(e => e.LastCheckpoint)
            .HasMaxLength(200);

        builder.Property(e => e.LastErrorMessage)
            .HasMaxLength(2000);

        builder.Property(e => e.ConcurrencyStamp)
            .IsConcurrencyToken();

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
