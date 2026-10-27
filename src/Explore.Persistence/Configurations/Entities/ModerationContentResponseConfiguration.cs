using Explore.Domain;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class ModerationContentResponseConfiguration : IEntityTypeConfiguration<ModerationContentResponse>
{
    public void Configure(EntityTypeBuilder<ModerationContentResponse> builder)
    {
        builder.ToTable("moderation_content_responses");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.SnapshotHash).HasMaxLength(64).IsRequired();
        builder.Property(row => row.RequestDigest).HasMaxLength(64).IsRequired();
        builder.Ignore(row => row.ProtectedStatement);
        builder.Property<byte[]>("protectedStatement").HasColumnName("protected_statement").IsRequired().HasMaxLength(16384);
        builder.HasIndex(row => new { row.TenantId, row.EffectId });
        builder.HasIndex(row => new { row.TenantId, row.RepresentativeHumanId });
        builder.HasOne<ModerationNoticeSource>().WithMany().HasForeignKey(row => row.EffectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationNotice>().WithMany().HasForeignKey(row => row.NoticeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationCase>().WithMany().HasForeignKey(row => row.CaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.RepresentativeHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_content_response_finite_purpose", "retain_until > submitted_at");
            table.HasCheckConstraint("ck_content_response_assignment", "assignment_revision > 0");
        });
        foreach (var property in builder.Metadata.GetProperties())
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
