
using Explore.Domain;
using Explore.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

internal sealed class AnonymousChallengeTenantQuotaConfiguration : IEntityTypeConfiguration<AnonymousChallengeTenantQuota>
{
    public void Configure(EntityTypeBuilder<AnonymousChallengeTenantQuota> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_anon_challenge_tenant_count", "issued >= 0 AND issued <= 10000");
            table.HasCheckConstraint("ck_anon_challenge_tenant_minute", "window_minute >= 0");
        });
        builder.HasKey(row => row.TenantId);
        builder.Property(row => row.TenantId).ValueGeneratedNever();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(row => row.TenantId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AnonymousChallengeEventQuotaConfiguration : IEntityTypeConfiguration<AnonymousChallengeEventQuota>
{
    public void Configure(EntityTypeBuilder<AnonymousChallengeEventQuota> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_anon_challenge_event_count", "issued >= 0 AND issued <= 10000");
            table.HasCheckConstraint("ck_anon_challenge_event_minute", "window_minute >= 0");
        });
        builder.HasKey(row => new { row.TenantId, row.EventId });
        builder.Property(row => row.TenantId).ValueGeneratedNever();
        builder.Property(row => row.EventId).ValueGeneratedNever();
        builder.HasOne<Explore.Domain.Event>().WithMany()
            .HasForeignKey(row => new { row.TenantId, row.EventId })
            .HasPrincipalKey(entity => new { entity.TenantId, entity.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
