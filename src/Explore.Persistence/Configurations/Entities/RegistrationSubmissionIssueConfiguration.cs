using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class RegistrationSubmissionIssueConfiguration : IEntityTypeConfiguration<RegistrationSubmissionIssue>
{
    public void Configure(EntityTypeBuilder<RegistrationSubmissionIssue> builder)
    {
        builder.Property(issue => issue.Id).ValueGeneratedNever();
        builder.Property(issue => issue.Code).HasMaxLength(100).IsRequired();
        builder.Property(issue => issue.CreatedAt).IsRequired();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(issue => issue.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationSubmission>().WithMany()
            .HasForeignKey(issue => new { issue.TenantId, issue.EventId, Id = issue.RegistrationSubmissionId })
            .HasPrincipalKey(submission => new { submission.TenantId, submission.EventId, submission.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(issue => new { issue.TenantId, issue.RegistrationSubmissionId });
    }
}
