using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class NotificationFanoutProcessorStateConfiguration
    : IEntityTypeConfiguration<NotificationFanoutProcessorState>
{
    public void Configure(EntityTypeBuilder<NotificationFanoutProcessorState> builder)
    {
        builder.HasKey(state => state.Id);
        builder.Property(state => state.Id).HasDefaultValueSql("uuidv7()");
        builder.Property(state => state.ProcessorCode).HasMaxLength(32).IsRequired();
        builder.HasIndex(state => state.ProcessorCode)
            .IsUnique();
    }
}
