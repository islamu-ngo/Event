using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class SupportAccessSessionConfiguration : IEntityTypeConfiguration<SupportAccessSession>
{
    public void Configure(EntityTypeBuilder<SupportAccessSession> builder)
    {
        builder.Property(e => e.Id).HasValueGenerator<GuidVersion7ValueGenerator>();

        builder.Property(e => e.ReasonCode)
            .HasMaxLength(SupportAccessSession.MaxReasonCodeLength)
            .IsRequired();

        builder.Property(e => e.ReasonText)
            .HasMaxLength(SupportAccessSession.MaxReasonTextLength)
            .IsRequired();

        builder.Property(e => e.TicketReference)
            .HasMaxLength(SupportAccessSession.MaxTicketReferenceLength)
            .IsRequired();

        builder.Property(e => e.EndReasonText)
            .HasMaxLength(SupportAccessSession.MaxEndReasonTextLength);

        builder.Property(e => e.StartedAtUtc).IsRequired();
        builder.Property(e => e.ExpiresAtUtc).IsRequired();
        builder.Property(e => e.ConcurrencyStamp).IsConcurrencyToken();

        builder.HasOne(e => e.ActorUser)
            .WithMany()
            .HasForeignKey(e => e.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.HasOne(e => e.TargetTenant)
            .WithMany()
            .HasForeignKey(e => e.TargetTenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.TargetTenantUser)
            .WithMany()
            .HasForeignKey(e => e.TargetTenantUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.HasOne(e => e.Status)
            .WithMany()
            .HasForeignKey(e => e.StatusId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Mode)
            .WithMany()
            .HasForeignKey(e => e.ModeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ApprovedByUser)
            .WithMany()
            .HasForeignKey(e => e.ApprovedByUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.HasOne(e => e.EndReason)
            .WithMany()
            .HasForeignKey(e => e.EndReasonId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.HasIndex(e => new { e.ActorUserId, e.StatusId, e.ExpiresAtUtc })
            .HasDatabaseName("ix_support_access_sessions_actor_status_expires")
            .IsDescending(false, false, true);

        builder.HasIndex(e => new { e.Id, e.ActorUserId, e.StatusId })
            .HasDatabaseName("ix_support_access_sessions_id_actor_status");

        builder.HasIndex(e => new { e.TargetTenantId, e.StartedAtUtc })
            .HasDatabaseName("ix_support_access_sessions_target_tenant_started")
            .IsDescending(false, true);

        builder.HasIndex(e => e.ActorUserId)
            .HasDatabaseName("ux_support_access_sessions_active_actor")
            .IsUnique()
            .HasFilter($"status_id = {(int)SupportAccessSessionStatusEnum.Active} AND ended_at_utc IS NULL");

        builder.ToTable(t =>
        {
            t.HasCheckConstraint(
                "ck_support_access_sessions_timebox",
                "expires_at_utc > started_at_utc");
            t.HasCheckConstraint(
                "ck_support_access_sessions_end_after_start",
                "ended_at_utc IS NULL OR ended_at_utc >= started_at_utc");
            t.HasCheckConstraint(
                "ck_support_access_sessions_terminal_reason",
                "(end_reason_id IS NULL) = (ended_at_utc IS NULL)");
        });
    }
}
