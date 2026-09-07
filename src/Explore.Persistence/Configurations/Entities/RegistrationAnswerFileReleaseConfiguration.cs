using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class RegistrationAnswerFileReleaseConfiguration : IEntityTypeConfiguration<RegistrationAnswerFileRelease>
{
    public void Configure(EntityTypeBuilder<RegistrationAnswerFileRelease> builder)
    {
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_registration_answer_file_releases_transition",
            "previous_quarantine_state = 'quarantined' AND new_quarantine_state = 'released'"));
        builder.Property(release => release.Id).ValueGeneratedNever();
        builder.Property(release => release.Reason).HasMaxLength(500).IsRequired();
        builder.Property(release => release.PreviousQuarantineState).HasMaxLength(20).IsRequired();
        builder.Property(release => release.NewQuarantineState).HasMaxLength(20).IsRequired();
        builder.Property(release => release.ReleasedAt).IsRequired();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(release => release.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationAnswerFile>().WithOne()
            .HasForeignKey<RegistrationAnswerFileRelease>(release => new
            {
                release.TenantId,
                Id = release.RegistrationAnswerFileId
            })
            .HasPrincipalKey<RegistrationAnswerFile>(file => new { file.TenantId, file.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(release => new { release.TenantId, release.RegistrationAnswerFileId }).IsUnique();
    }
}
