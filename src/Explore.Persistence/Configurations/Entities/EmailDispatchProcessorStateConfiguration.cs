// ABOUTME: EF Core mapping for durable cross-replica email processor coordination state.
// ABOUTME: Enforces one state row per processor code for global controls and reminder hysteresis.

using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class EmailDispatchProcessorStateConfiguration : IEntityTypeConfiguration<EmailDispatchProcessorState>
{
    public void Configure(EntityTypeBuilder<EmailDispatchProcessorState> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_email_dispatch_processor_states_revision_nonnegative",
                "delivery_policy_revision >= 0");
            table.HasCheckConstraint(
                "ck_email_dispatch_processor_states_suppression_revision",
                "optional_suppressed_through_revision IS NULL OR optional_suppressed_through_revision BETWEEN 0 AND delivery_policy_revision");
            table.HasCheckConstraint(
                "ck_email_dispatch_processor_states_smtp_rate_pair",
                "(smtp_available_tokens IS NULL) = (smtp_refill_at IS NULL)");
            table.HasCheckConstraint(
                "ck_email_dispatch_processor_states_smtp_tokens_nonnegative",
                "smtp_available_tokens IS NULL OR smtp_available_tokens >= 0");
            table.HasCheckConstraint(
                "ck_email_dispatch_processor_states_global_rate_override",
                "global_smtp_rate_limit_per_minute_override IS NULL OR global_smtp_rate_limit_per_minute_override BETWEEN 1 AND 100000");
        });

        builder.HasKey(state => state.Id);
        builder.Property(state => state.Id).HasDefaultValueSql("uuidv7()");
        builder.Property(state => state.ProcessorCode).HasMaxLength(32).IsRequired();
        builder.Property(state => state.DeliveryPolicyRevision).HasDefaultValue(0L);
        builder.Property(state => state.PauseReason).HasMaxLength(500);

        builder.HasIndex(state => state.ProcessorCode)
            .IsUnique();
    }
}
