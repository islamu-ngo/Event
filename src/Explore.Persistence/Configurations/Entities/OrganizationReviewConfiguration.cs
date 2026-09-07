using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class OrganizationReviewConfiguration : IEntityTypeConfiguration<OrganizationReview>
{
    public void Configure(EntityTypeBuilder<OrganizationReview> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasDefaultValueSql("uuidv7()");

        builder.Property(e => e.ReviewerName).HasMaxLength(200);
        builder.Property(e => e.Comment).HasMaxLength(2000);

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);
    }
}
