using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Moderation;

public sealed class ModerationConsumerMeasurementReceiptConfiguration
    : IEntityTypeConfiguration<ModerationConsumerMeasurementReceipt>
{
    public void Configure(EntityTypeBuilder<ModerationConsumerMeasurementReceipt> builder)
    {
        builder.ToTable("moderation_consumer_measurement_receipts", table =>
        {
            table.HasCheckConstraint("ck_moderation_measurement_receipt_generation",
                "writer_generation > 0 AND writer_generation < 9223372036854775807 AND revision = 1");
            table.HasCheckConstraint("ck_moderation_measurement_receipt_validity", "valid_until > accepted_at");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.SignedToken).HasMaxLength(65536).IsRequired();
        builder.Property(row => row.TokenDigest).HasMaxLength(64).IsRequired();
        builder.HasIndex(row => new { row.HostBindingId, row.TenantId, row.ChallengeId }).IsUnique();
        builder.HasIndex(row => new { row.HostBindingId, row.TenantId, row.RunId }).IsUnique();
        builder.HasIndex(row => row.TokenDigest);
        builder.HasOne<ModerationHostBinding>().WithMany().HasForeignKey(row => row.HostBindingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(row => row.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationConsumerMeasurementChallenge>().WithMany().HasForeignKey(row => row.ChallengeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationMeasurementIssuerEnrollment>().WithMany().HasForeignKey(row => row.IssuerEnrollmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.AcceptedByHumanId).OnDelete(DeleteBehavior.Restrict);
        foreach (var property in builder.Metadata.GetProperties())
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
