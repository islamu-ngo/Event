using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class RegistrationSubmissionRevisionConfiguration : IEntityTypeConfiguration<RegistrationSubmissionRevision>
{
    public void Configure(EntityTypeBuilder<RegistrationSubmissionRevision> builder)
    {
        builder.ToTable(table =>
            table.HasCheckConstraint("ck_registration_submission_revisions_number", "revision_number > 0"));
        builder.Property(revision => revision.Id).ValueGeneratedNever();
        builder.Property(revision => revision.ReceivedEvidenceHash)
            .HasConversion(hash => hash.Value, value => RegistrationEvidenceHash.Create(value)).HasMaxLength(44).IsRequired();
        builder.Property(revision => revision.ProviderRevisionId).HasMaxLength(200);
        builder.Property(revision => revision.ReceivedAt).IsRequired();
        builder.Property(revision => revision.CreatedAt).IsRequired();
        builder.Property(revision => revision.IsDeleted).HasDefaultValue(false);
        builder.Property(revision => revision.ConcurrencyStamp).IsConcurrencyToken();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(revision => revision.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(revision => new { revision.TenantId, revision.RegistrationSubmissionId, revision.RevisionNumber })
            .IsUnique();
    }
}
