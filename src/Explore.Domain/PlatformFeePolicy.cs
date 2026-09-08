using Explore.Domain.Interfaces;
using Explore.Domain.ValueObjects;

namespace Explore.Domain;

public sealed class PlatformFeePolicy : IAuditableEntity
{
    private readonly List<PlatformFeeFixedCharge> _fixedCharges = [];

    private PlatformFeePolicy()
    {
    }

    private PlatformFeePolicy(int versionNumber, bool isEnabled, int feeBasisPoints, IEnumerable<PlatformFeeFixedCharge> fixedCharges)
    {
        Id = Guid.CreateVersion7();
        VersionNumber = versionNumber;
        IsActive = true;
        IsEnabled = isEnabled;
        FeeBasisPoints = feeBasisPoints;
        _fixedCharges.AddRange(fixedCharges.Select(static fixedCharge => fixedCharge.Clone()));
    }

    public Guid Id { get; private set; }

    public int VersionNumber { get; private set; }

    public bool IsActive { get; private set; }

    public bool IsEnabled { get; private set; }

    public int FeeBasisPoints { get; private set; }

    public IReadOnlyCollection<PlatformFeeFixedCharge> FixedCharges => _fixedCharges.AsReadOnly();

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public static PlatformFeePolicy CreateDefault() => new(1, false, 0, []);

    public PlatformFeePolicy CreateRevision(bool isEnabled, int feeBasisPoints, IEnumerable<PlatformFeeFixedCharge> fixedCharges)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(fixedCharges);
        PlatformFeeFixedCharge[] materializedFixedCharges = fixedCharges.ToArray();
        Validate(feeBasisPoints, materializedFixedCharges);
        IsActive = false;
        return new PlatformFeePolicy(checked(VersionNumber + 1), isEnabled, feeBasisPoints, materializedFixedCharges);
    }

    public void Retire()
    {
        EnsureActive();
        IsActive = false;
    }

    public long GetFixedChargeMinor(string currencyCode)
    {
        CurrencyMetadata currency = CurrencyMetadata.Get(currencyCode);
        if (currency.IsNoCurrency)
        {
            throw new ArgumentException("XXX currency has no monetary platform fee.", nameof(currencyCode));
        }

        return _fixedCharges.SingleOrDefault(fixedCharge => fixedCharge.CurrencyCode == currency.Code)?.AmountMinor ?? 0;
    }

    public long CalculateFeeMinor(string currencyCode, long amountMinor)
    {
        if (!IsEnabled)
        {
            return 0;
        }

        long organizerSubtotalMinor = Math.Max(0, amountMinor);
        long feeMinor = MinorUnitMath.Add(
            MinorUnitMath.ApplyBasisPoints(organizerSubtotalMinor, FeeBasisPoints),
            GetFixedChargeMinor(currencyCode));
        return Math.Min(feeMinor, organizerSubtotalMinor);
    }

    private void EnsureActive()
    {
        if (!IsActive)
        {
            throw new InvalidOperationException("Only the active platform fee policy version can be revised or retired.");
        }
    }

    private static void Validate(int feeBasisPoints, IEnumerable<PlatformFeeFixedCharge> fixedCharges)
    {
        if (feeBasisPoints is < 0 or > 10_000)
        {
            throw new ArgumentOutOfRangeException(nameof(feeBasisPoints));
        }

        PlatformFeeFixedCharge[] materializedFixedCharges = fixedCharges.ToArray();
        if (materializedFixedCharges.Select(static fixedCharge => fixedCharge.CurrencyCode).Distinct(StringComparer.Ordinal).Count() != materializedFixedCharges.Length)
        {
            throw new ArgumentException("Fixed fee currencies must be unique.", nameof(fixedCharges));
        }
    }
}
