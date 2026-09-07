using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class RegistrationWorkflowConfiguration : IEntityTypeConfiguration<RegistrationWorkflow>
{
    public void Configure(EntityTypeBuilder<RegistrationWorkflow> builder)
    {
        builder.Property(workflow => workflow.Id).ValueGeneratedNever();
        builder.Property(workflow => workflow.Purpose).IsRequired().HasMaxLength(100);
        builder.Property(workflow => workflow.CreatedAt).IsRequired();
        builder.Property(workflow => workflow.IsDeleted).HasDefaultValue(false);
        builder.Property(workflow => workflow.ConcurrencyStamp).IsConcurrencyToken();
        builder.HasAlternateKey(workflow => new { workflow.TenantId, workflow.EventId, workflow.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(workflow => workflow.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Event>().WithMany()
            .HasForeignKey(workflow => new { workflow.TenantId, workflow.EventId })
            .HasPrincipalKey(@event => new { @event.TenantId, @event.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(workflow => new { workflow.TenantId, workflow.EventId, workflow.Purpose }).IsUnique();
    }
}
