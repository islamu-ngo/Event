namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Single-attempt physical settlement, not a business-rejection classifier or an evidence-write grant.
/// The callback proposes commit or whole rollback. Only this boundary can acknowledge settlement,
/// after disposal of the original physical owner. Arbitrary exceptions never produce a witness.
/// </summary>
public interface IOriginalOwnerSettlementBoundary
{
    Task<IOriginalOwnerSettlement<T>> ExecuteAsync<T>(object invocation,
        Func<CancellationToken, Task<OriginalOwnerDecision<T>>> operation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Consumes a direct returned witness once, outside transactions, on the same owner instance,
    /// invocation and context lease, before that owner starts another attempt. This proves only the
    /// physical disposition. It does not establish trusted business classification, original human/
    /// action custody, fresh source admission or permission to append failure history.
    /// </summary>
    T Consume<T>(object invocation, IOriginalOwnerSettlement<T> settlement, OriginalOwnerDisposition expectedDisposition);
}

public enum OriginalOwnerDisposition
{
    Committed,
    WholeTransactionRolledBack
}

/// <summary>
/// Opaque owner-issued witness. Implementing this interface or copying its disposition creates no
/// proof: Consume accepts only the issuing owner's private, invocation-bound implementation.
/// </summary>
public interface IOriginalOwnerSettlement<T>
{
    OriginalOwnerDisposition Disposition { get; }
}

/// <summary>
/// A requested physical disposition, never a settlement receipt or source authority. The trusted
/// family coordinator retains immutable observations in Value and separately classifies eligible
/// business refusals. A rollback proposal alone cannot authorize any subsequent evidence write.
/// </summary>
public sealed record OriginalOwnerDecision<T>
{
    private OriginalOwnerDecision(T value, OriginalOwnerDisposition disposition)
    {
        Value = value;
        Disposition = disposition;
    }

    public T Value { get; }
    public OriginalOwnerDisposition Disposition { get; }
    public static OriginalOwnerDecision<T> Commit(T value) => new(value, OriginalOwnerDisposition.Committed);
    public static OriginalOwnerDecision<T> RollBack(T value) => new(value, OriginalOwnerDisposition.WholeTransactionRolledBack);
    public override string ToString() => nameof(OriginalOwnerDecision<T>);
}
