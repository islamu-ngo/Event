using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class RegistrationTicketAssignmentConfiguration : IEntityTypeConfiguration<RegistrationTicketAssignment>
{
    public void Configure(EntityTypeBuilder<RegistrationTicketAssignment> builder)
    {
        builder.Property(assignment => assignment.Id).ValueGeneratedNever();
        builder.Property(assignment => assignment.CreatedAt).IsRequired();
        builder.Property(assignment => assignment.ConcurrencyStamp).IsConcurrencyToken();
        builder.Property<bool>("IsDeleted").HasDefaultValue(false);
        builder.HasAlternateKey(assignment => new { assignment.TenantId, assignment.Id });
        builder.HasAlternateKey(assignment => new { assignment.TenantId, assignment.RegistrationOrderId, assignment.Id });
        builder.HasAlternateKey(assignment => new
        {
            assignment.TenantId,
            assignment.RegistrationOrderId,
            assignment.Id,
            assignment.RegistrationOrderLineId
        });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(assignment => assignment.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(assignment => assignment.RegistrationOrder).WithMany()
            .HasForeignKey(assignment => new { assignment.TenantId, assignment.RegistrationOrderId })
            .HasPrincipalKey(order => new { order.TenantId, order.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(assignment => assignment.RegistrationOrderLine).WithMany(line => line.Assignments)
            .HasForeignKey(assignment => new { assignment.TenantId, assignment.RegistrationOrderId, assignment.RegistrationOrderLineId })
            .HasPrincipalKey(line => new { line.TenantId, line.RegistrationOrderId, line.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(assignment => assignment.Participant).WithMany()
            .HasForeignKey(assignment => new { assignment.TenantId, assignment.RegistrationOrderId, assignment.ParticipantId })
            .HasPrincipalKey(participant => new { participant.TenantId, participant.RegistrationOrderId, participant.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(assignment => assignment.AssignmentStatus).WithMany()
            .HasForeignKey(assignment => assignment.AssignmentStatusId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(assignment => new { assignment.TenantId, assignment.RegistrationOrderId });
        builder.HasIndex(assignment => new { assignment.TenantId, assignment.ParticipantId });
        builder.HasIndex(assignment => new { assignment.TenantId, assignment.RegistrationOrderLineId, assignment.Ordinal })
            .IsUnique();
    }
}
