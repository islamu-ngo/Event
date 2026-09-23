using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class EventResourceProviderActivationConfiguration : IEntityTypeConfiguration<EventResourceProviderActivation>
{
    public void Configure(EntityTypeBuilder<EventResourceProviderActivation> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_event_resource_provider_activation_state", "state BETWEEN 1 AND 3");
            table.HasCheckConstraint("ck_event_resource_provider_activation_epoch", "epoch >= 0");
            table.HasCheckConstraint("ck_event_resource_provider_activation_owner",
                "(epoch = 0 AND state = 3 AND current_operation_id = '00000000-0000-0000-0000-000000000000') OR " +
                "(epoch > 0 AND current_operation_id <> '00000000-0000-0000-0000-000000000000')");
        });
        builder.HasKey(activation => activation.Id);
        builder.Property(activation => activation.Id).ValueGeneratedNever();
        builder.Property(activation => activation.ConcurrencyStamp).IsConcurrencyToken();
        builder.Property(activation => activation.State).HasConversion<int>();
        builder.Property(activation => activation.CreatedAt)
            .HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        builder.Property(activation => activation.UpdatedAt)
            .HasConversion(value => value,
                value => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null);
    }
}
