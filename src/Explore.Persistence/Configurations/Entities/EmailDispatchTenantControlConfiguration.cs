using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class EmailDispatchTenantControlConfiguration : IEntityTypeConfiguration<EmailDispatchTenantControl>
{
    public void Configure(EntityTypeBuilder<EmailDispatchTenantControl> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_email_dispatch_tenant_controls_smtp_rate_pair",
                "(smtp_available_tokens IS NULL) = (smtp_refill_at IS NULL)");
            table.HasCheckConstraint(
                "ck_email_dispatch_tenant_controls_smtp_tokens_nonnegative",
                "smtp_available_tokens IS NULL OR smtp_available_tokens >= 0");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(e => e.PauseReason).HasMaxLength(500);

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.TenantId)
            .IsUnique();

        builder.HasIndex(e => new { e.IsPaused, e.UpdatedAt });
    }
}
