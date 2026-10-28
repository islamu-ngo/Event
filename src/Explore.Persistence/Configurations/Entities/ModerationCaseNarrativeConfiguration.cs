using Explore.Domain;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class ModerationCaseNarrativeConfiguration : IEntityTypeConfiguration<ModerationCaseNarrative>
{
    public void Configure(EntityTypeBuilder<ModerationCaseNarrative> builder)
    {
        builder.ToTable("moderation_case_narratives");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Ignore(row => row.ProtectedMaterial);
        builder.Property<byte[]>("protectedMaterial").HasColumnName("protected_material").IsRequired().HasMaxLength(131072);
        builder.HasIndex(row => new { row.TenantId, row.TrustDomainId, row.CaseId, row.SourceId, row.MaterialFactId }).IsUnique();
        builder.HasIndex(row => new { row.TenantId, row.PurgeAt });
        builder.HasOne<ModerationCase>().WithMany().HasForeignKey(row => row.CaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationCaseTargetReceipt>().WithMany().HasForeignKey(row => row.ReceiptId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationCaseTargetSource>().WithMany().HasForeignKey(row => row.SourceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationCaseNarrativeAdoption>().WithMany()
            .HasForeignKey(row => new { row.AdoptionId, row.HostBindingId, row.TrustDomainId, row.TenantId, row.PolicySourceId })
            .HasPrincipalKey(row => new { row.Id, row.HostBindingId, row.TrustDomainId, row.TenantId, row.SourceId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationQualificationEpoch>().WithMany().HasForeignKey(row => row.QualificationEpochId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.ReporterUserId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_case_narrative_finite_custody", "purge_at > captured_at");
            table.HasCheckConstraint("ck_case_narrative_generations",
                "policy_writer_generation > 0 AND adoption_writer_generation > policy_writer_generation " +
                "AND qualification_revision >= adoption_writer_generation");
        });
    }
}
