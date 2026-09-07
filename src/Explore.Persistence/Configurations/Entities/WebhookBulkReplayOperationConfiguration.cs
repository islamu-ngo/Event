using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class WebhookBulkReplayOperationConfiguration
    : IEntityTypeConfiguration<WebhookBulkReplayOperation>
{
    public void Configure(EntityTypeBuilder<WebhookBulkReplayOperation> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_webhook_bulk_replay_operations_filter_window",
                "to_utc > from_utc");
            table.HasCheckConstraint(
                "ck_webhook_bulk_replay_operations_requested_max",
                $"requested_max_items BETWEEN 1 AND {WebhookBulkReplayOperation.HardMaximumItems}");
            table.HasCheckConstraint(
                "ck_webhook_bulk_replay_operations_request_hash",
                "request_hash ~ '^sha256:[0-9a-f]{64}$'");
            table.HasCheckConstraint(
                "ck_webhook_bulk_replay_operations_concurrency_version",
                "concurrency_version > 0");
            table.HasCheckConstraint(
                "ck_webhook_bulk_replay_operations_nonnegative_counts",
                "estimated_eligible_count >= 0 AND estimated_selected_count >= 0 " +
                "AND excluded_held_count >= 0 AND excluded_payload_unavailable_count >= 0 " +
                "AND excluded_endpoint_unavailable_count >= 0 AND excluded_ineligible_local_state_count >= 0 " +
                "AND excluded_provider_conflict_count >= 0 AND excluded_provider_unknown_count >= 0 " +
                "AND excluded_provider_manual_reconciliation_count >= 0 " +
                "AND excluded_provider_ineligible_count >= 0 AND scheduled_count >= 0");
            table.HasCheckConstraint(
                "ck_webhook_bulk_replay_operations_selected_bounds",
                "estimated_selected_count <= requested_max_items AND scheduled_count <= requested_max_items");
            table.HasCheckConstraint(
                "ck_webhook_bulk_replay_operations_lifecycle",
                "(status_id = 1 AND started_at IS NULL AND completed_at IS NULL AND cancelled_at IS NULL AND failed_at IS NULL AND failure_code IS NULL) " +
                "OR (status_id = 2 AND started_at IS NOT NULL AND completed_at IS NULL AND cancelled_at IS NULL AND failed_at IS NULL AND failure_code IS NULL) " +
                "OR (status_id = 3 AND started_at IS NOT NULL AND completed_at IS NOT NULL AND cancelled_at IS NULL AND failed_at IS NULL AND failure_code IS NULL) " +
                "OR (status_id = 4 AND started_at IS NULL AND completed_at IS NULL AND cancelled_at IS NOT NULL AND failed_at IS NULL AND failure_code IS NULL) " +
                "OR (status_id = 5 AND started_at IS NOT NULL AND completed_at IS NULL AND cancelled_at IS NULL AND failed_at IS NOT NULL AND failure_code IS NOT NULL)");
        });

        builder.Property(operation => operation.Id).HasDefaultValueSql("uuidv7()");
        builder.Property(operation => operation.OperationKey).IsRequired();
        builder.Property(operation => operation.RequestHash)
            .HasMaxLength(WebhookBulkReplayOperation.RequestHashLength)
            .IsRequired();
        builder.Property(operation => operation.StatusId).IsRequired();
        builder.Property(operation => operation.EventType)
            .HasMaxLength(WebhookMessage.MaxEventTypeLength);
        builder.Property(operation => operation.ReasonCode)
            .HasMaxLength(WebhookBulkReplayOperation.MaxReasonCodeLength)
            .IsRequired();
        builder.Property(operation => operation.CancellationReasonCode)
            .HasMaxLength(WebhookBulkReplayOperation.MaxReasonCodeLength);
        builder.Property(operation => operation.FailureCode)
            .HasMaxLength(WebhookBulkReplayOperation.MaxFailureCodeLength);
        builder.Property(operation => operation.ConcurrencyVersion)
            .IsRequired()
            .IsConcurrencyToken();

        builder.Ignore(operation => operation.Status);
        builder.Ignore(operation => operation.EstimatedExcludedCount);

        builder.HasAlternateKey(operation => new { operation.TenantId, operation.Id });
        builder.HasOne(operation => operation.Tenant)
            .WithMany()
            .HasForeignKey(operation => operation.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(operation => operation.StatusLookup)
            .WithMany()
            .HasForeignKey(operation => operation.StatusId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(operation => operation.WebhookConsumer)
            .WithMany()
            .HasForeignKey(operation => operation.WebhookConsumerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(operation => operation.WebhookEndpoint)
            .WithMany()
            .HasForeignKey(operation => operation.WebhookEndpointId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(operation => new { operation.TenantId, operation.OperationKey })
            .IsUnique();
        builder.HasIndex(operation => new { operation.StatusId, operation.QueuedAt, operation.Id });
        builder.HasIndex(operation => new { operation.TenantId, operation.StatusId, operation.QueuedAt });
        builder.HasIndex(operation => new { operation.TenantId, operation.FromUtc, operation.ToUtc });
    }
}
