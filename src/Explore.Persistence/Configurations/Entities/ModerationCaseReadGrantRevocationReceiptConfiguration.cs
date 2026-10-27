using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class ModerationCaseReadGrantRevocationReceiptConfiguration : IEntityTypeConfiguration<ModerationCaseReadGrantRevocationReceipt>
{
    public void Configure(EntityTypeBuilder<ModerationCaseReadGrantRevocationReceipt> builder)
    {
        builder.ToTable("moderation_case_read_grant_revocation_receipts");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.HasIndex(row => row.GrantId).IsUnique();
        builder.HasOne<ModerationCaseReadGrant>().WithMany().HasForeignKey(row => row.GrantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(row => row.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.AdministratorUserId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table => table.HasCheckConstraint("ck_moderation_read_grant_revocation_revision", "expected_revision = 1"));
    }
}
