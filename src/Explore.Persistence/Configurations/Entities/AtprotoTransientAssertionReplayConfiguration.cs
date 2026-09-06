// ABOUTME: Maps immutable instance-wide transient assertion replay claims to relational storage.
// ABOUTME: Enforces digest uniqueness and an integer expiry index across all supported providers.

using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Explore.Persistence.Configurations.Entities;

public sealed class AtprotoTransientAssertionReplayConfiguration : IEntityTypeConfiguration<AtprotoTransientAssertionReplay>
{
    public void Configure(EntityTypeBuilder<AtprotoTransientAssertionReplay> builder)
    {
        builder.HasKey(replay => replay.Id);
        builder.Property(replay => replay.Id).ValueGeneratedNever();
        builder.Property(replay => replay.AssertionDigest).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(replay => replay.ExpiresAtUnixMilliseconds).IsRequired();
        builder.HasIndex(replay => replay.AssertionDigest).IsUnique().HasDatabaseName("ux_atproto_assertion_replays_digest");
        builder.HasIndex(replay => replay.ExpiresAtUnixMilliseconds).HasDatabaseName("ix_atproto_assertion_replays_expiry");

        foreach (var property in builder.Metadata.GetProperties())
        {
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        }
    }
}
