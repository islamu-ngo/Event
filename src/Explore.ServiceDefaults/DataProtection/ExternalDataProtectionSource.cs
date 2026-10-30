using System.Globalization;
using System.Text;
using Explore.ServiceDefaults.Secrets;

namespace Explore.ServiceDefaults.DataProtection;

public interface IExternalDataProtectionSource
{
    ValueTask PrepareAsync(CancellationToken cancellationToken = default);
    ExternalDataProtectionInput Read();
    ValueTask<ExternalDataProtectionInput> ReadFreshAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Prepares the selected process owner's exact two-value group before provider resolution.
/// Synchronous consumers use only prepared memory; readiness explicitly reacquires authority.
/// Deployment pin and floor never come from the candidate authority or application configuration.
/// </summary>
public sealed class ExternalDataProtectionSource(
    SecretAuthorityAcquisition owner, ExternalDataProtectionPartition partition,
    TimeProvider? timeProvider = null) : IExternalDataProtectionSource
{
    private ExternalDataProtectionInput? prepared;

    public async ValueTask PrepareAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref prepared) is not null)
            return;
        var candidate = await ReadFreshAsync(cancellationToken).ConfigureAwait(false);
        _ = ExternalDataProtectionRingAdmission.Validate(candidate, partition.Name,
            (timeProvider ?? TimeProvider.System).GetUtcNow());
        cancellationToken.ThrowIfCancellationRequested();
        var existing = Interlocked.CompareExchange(ref prepared, candidate, null);
        if (existing is not null && existing != candidate)
            throw ExternalDataProtectionRingAdmission.Invalid();
    }

    public ExternalDataProtectionInput Read() =>
        Volatile.Read(ref prepared) ?? throw new InvalidOperationException("data_protection_not_prepared");

    public async ValueTask<ExternalDataProtectionInput> ReadFreshAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            string prefix = ExternalDataProtectionRegistration.EnvironmentPrefix(partition.Name);
            var expectations = ReadExpectations(prefix);
            using var material = await owner.PrepareDataProtectionAsync(partition.Name, cancellationToken)
                .ConfigureAwait(false);
            using var ring = material.ReadRing();
            using var admission = material.ReadAdmission();
            string ringText = Required(ring.Material.Reveal());
            string admissionText = Required(admission.Material.Reveal());
            if (ReadExpectations(prefix) != expectations)
                throw ExternalDataProtectionRingAdmission.Invalid();
            cancellationToken.ThrowIfCancellationRequested();
            return new(ringText, admissionText, expectations.Pin, expectations.Floor);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Never attach upstream exceptions: they may contain protected material or coordinates.
            throw new InvalidOperationException("data_protection_authority_unavailable");
        }
    }

    private static (string Pin, long Floor) ReadExpectations(string prefix)
    {
        string pin = Required(Environment.GetEnvironmentVariable(prefix + "_ADMISSION_SHA256"));
        if (!long.TryParse(Environment.GetEnvironmentVariable(prefix + "_MINIMUM_GENERATION"),
                NumberStyles.None, CultureInfo.InvariantCulture, out long floor) || floor < 1)
            throw ExternalDataProtectionRingAdmission.Invalid();
        return (pin, floor);
    }

    private static string Required(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || Encoding.UTF8.GetByteCount(value) > 32 * 1024
            || value.Contains("${", StringComparison.Ordinal))
            throw ExternalDataProtectionRingAdmission.Invalid();
        return value;
    }
}
