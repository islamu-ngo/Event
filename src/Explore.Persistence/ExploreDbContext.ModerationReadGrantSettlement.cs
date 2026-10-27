using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence;

public partial class ExploreDbContext
{
    private ModerationReadGrantSettlement? _moderationReadGrantSettlement;

    internal IDisposable BindModerationReadGrantSettlement(ModerationCaseReadGrant grant,
        ModerationCaseReadGrantRevocationReceipt? receipt) => BindModerationReadGrantSettlement(
            receipt is null ? [Entry(grant)] : [Entry(grant), Entry(receipt)]);

    internal IDisposable BindModerationReadGrantSettlement(ModerationAdjudicatedCaseReadGrant grant,
        ModerationQualificationEpoch qualification)
    {
        if (Entry(qualification).State != EntityState.Unchanged)
            throw new ModerationReadGrantSettlementException();
        return BindModerationReadGrantSettlement([Entry(grant), Entry(qualification)]);
    }

    private IDisposable BindModerationReadGrantSettlement(EntityEntry[] entries)
    {
        if (_moderationReadGrantSettlement is not null || Database.CurrentTransaction is not { } transaction)
            throw new ModerationReadGrantSettlementException();
        var settlement = new ModerationReadGrantSettlement(this, transaction, entries);
        _moderationReadGrantSettlement = settlement;
        try
        {
            ValidateModerationReadGrantSettlement();
            return settlement;
        }
        catch
        {
            settlement.Dispose();
            throw;
        }
    }

    internal void ValidateModerationReadGrantSettlement()
    {
        if (_moderationReadGrantSettlement is not { } settlement) return;
        ChangeTracker.DetectChanges();
        if (!ReferenceEquals(Database.CurrentTransaction, settlement.Transaction)
            || !settlement.Matches()
            || HasLostModerationRecoveryAdvancement(settlement.Transaction.TransactionId)
            || ChangeTracker.Entries().Any(entry => !settlement.Owns(entry.Entity)
                && !IsOwnedModerationRecoveryAdvancement(entry)))
            throw new ModerationReadGrantSettlementException();
    }

    internal sealed class ModerationReadGrantSettlementException : InvalidOperationException
    {
        public ModerationReadGrantSettlementException() : base("moderation_read_grant_settlement_changed") { }
    }

    private sealed class ModerationReadGrantSettlement : IDisposable
    {
        private readonly ExploreDbContext _context;
        private readonly (EntityEntry Entry, EntityState State, PropertyValues Values)[] _owned;
        internal IDbContextTransaction Transaction { get; }

        internal ModerationReadGrantSettlement(ExploreDbContext context, IDbContextTransaction transaction,
            EntityEntry[] entries)
        {
            _context = context;
            Transaction = transaction;
            _owned = entries.Select(entry => (entry, entry.State, entry.CurrentValues.Clone())).ToArray();
        }

        internal bool Owns(object entity) => _owned.Any(row => ReferenceEquals(row.Entry.Entity, entity));

        internal bool Matches() => _owned.All(row =>
            (row.Entry.State == row.State || row.State is EntityState.Added or EntityState.Modified
                && row.Entry.State == EntityState.Unchanged)
            && row.Entry.Properties.All(property => Equals(property.CurrentValue, row.Values[property.Metadata.Name])));

        public void Dispose()
        {
            if (ReferenceEquals(_context._moderationReadGrantSettlement, this))
                _context._moderationReadGrantSettlement = null;
        }
    }
}
