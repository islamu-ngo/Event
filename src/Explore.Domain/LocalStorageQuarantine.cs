using System.Security.Cryptography;
using System.Text;

namespace Explore.Domain;

public enum LocalStorageQuarantineState : short
{
    Pending = 1,
    Processing = 2,
    Completed = 3,
    Blocked = 4
}

public enum LocalStorageQuarantineOutcome : short
{
    Moved = 1,
    AlreadyQuarantined = 2,
    AlreadyAbsent = 3,
    Blocked = 4
}

/// <summary>Retained physical custody, independent of tenant attribution and metadata lifetime.</summary>
public sealed class LocalStorageQuarantine
{
    private LocalStorageQuarantine() { }

    public Guid Id { get; private set; }
    public Guid ProviderBindingId { get; private set; }
    public string ObjectKey { get; private set; } = string.Empty;
    public string CanonicalPhysicalPath { get; private set; } = string.Empty;
    public string PhysicalPathHash { get; private set; } = string.Empty;
    public long ObservedSize { get; private set; }
    public DateTime ObservedLastModifiedUtc { get; private set; }
    public DateTime ModifiedBeforeUtc { get; private set; }
    public string AdmissionReason { get; private set; } = string.Empty;
    public DateTime AdmittedAtUtc { get; private set; }
    public LocalStorageQuarantineState State { get; private set; }
    public Guid ConcurrencyStamp { get; private set; }
    public DateTime? LeaseExpiresAtUtc { get; private set; }
    public DateTime? NextAttemptAtUtc { get; private set; }
    public DateTime? SettledAtUtc { get; private set; }
    public LocalStorageQuarantineOutcome? Outcome { get; private set; }

    public static LocalStorageQuarantine Create(StorageProviderBinding binding, string objectKey,
        long observedSize, DateTime observedLastModifiedUtc, DateTime modifiedBeforeUtc,
        string admissionReason, DateTime utcNow)
    {
        RequireUtc(observedLastModifiedUtc);
        RequireUtc(utcNow);
        RequireUtc(modifiedBeforeUtc);
        if (observedLastModifiedUtc > modifiedBeforeUtc || modifiedBeforeUtc > utcNow
            || observedSize < 0 || string.IsNullOrWhiteSpace(admissionReason)
            || admissionReason.Length > 128 || admissionReason.Any(char.IsControl))
            throw new ArgumentException("Admission requires bounded evidence and reason.");
        string path = ResolvePhysicalPath(binding, objectKey);
        if (!CanEstablishCaseIdentity(path))
            throw new ArgumentException("Non-ASCII case-insensitive physical identity is not established.");
        return new()
        {
            Id = Guid.CreateVersion7(),
            ProviderBindingId = binding.Id,
            ObjectKey = objectKey,
            CanonicalPhysicalPath = path,
            PhysicalPathHash = HashPhysicalPath(path),
            ObservedSize = observedSize,
            ObservedLastModifiedUtc = observedLastModifiedUtc,
            ModifiedBeforeUtc = modifiedBeforeUtc,
            AdmissionReason = admissionReason,
            AdmittedAtUtc = utcNow,
            State = LocalStorageQuarantineState.Pending,
            ConcurrencyStamp = Guid.CreateVersion7(),
            NextAttemptAtUtc = utcNow
        };
    }

    /// <summary>Lexical inventory identity only. The provider separately rejects symlinks before an effect.</summary>
    public static string ResolvePhysicalPath(StorageProviderBinding binding, string objectKey)
    {
        ArgumentNullException.ThrowIfNull(binding);
        string normalizedKey = objectKey?.Replace('\\', '/') ?? string.Empty;
        if (binding.Id == Guid.Empty || binding.Provider != StorageProviders.Local || binding.LocalRootPath is null
            || !Path.IsPathFullyQualified(binding.LocalRootPath)
            || binding.LocalRootPath.Any(char.IsControl)
            || string.IsNullOrWhiteSpace(objectKey) || objectKey.Length > 1024
            || objectKey.Any(char.IsControl) || objectKey.Contains(':')
            || normalizedKey.Split('/').Any(segment => segment is "" or "." or "..")
            || normalizedKey.Split('/').Any(segment => segment.Equals(".quarantine",
                OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
            || Path.IsPathRooted(normalizedKey))
            throw new ArgumentException("A local inventory key and retained local binding are required.");
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(binding.LocalRootPath));
        string path = Path.GetFullPath(Path.Combine(root, normalizedKey));
        string prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.Ordinal) || path.Length > 4096)
            throw new ArgumentException("The local target must remain under its captured root.");
        return CanonicalizePhysicalPath(path);
    }

    /// <summary>Non-Linux hosts conservatively collapse ASCII case aliases, even on case-sensitive volumes.</summary>
    public static string CanonicalizePhysicalPath(string path)
    {
        string normalized = OperatingSystem.IsWindows() ? path.Replace('\\', '/')
            : "/" + string.Join('/', path.Split('/', StringSplitOptions.RemoveEmptyEntries));
        if (OperatingSystem.IsWindows())
        {
            if (normalized.StartsWith("//?/UNC/", StringComparison.OrdinalIgnoreCase))
                normalized = "//" + normalized[8..];
            else if (normalized.StartsWith("//?/", StringComparison.Ordinal)
                && normalized.Length >= 7 && char.IsAsciiLetter(normalized[4]) && normalized[5..7] == ":/")
                normalized = normalized[4..];
        }
        return OperatingSystem.IsLinux() ? normalized : string.Create(normalized.Length, normalized,
            static (result, value) =>
            {
                for (int index = 0; index < value.Length; index++)
                    result[index] = value[index] is >= 'A' and <= 'Z' ? (char)(value[index] + ('a' - 'A')) : value[index];
            });
    }

    public static bool CanEstablishCaseIdentity(string path) =>
        OperatingSystem.IsLinux() || path.All(character => character <= 127)
            && (!OperatingSystem.IsWindows()
                || !path.StartsWith("//./", StringComparison.Ordinal) && !path.StartsWith("//?/", StringComparison.Ordinal)
                    && path.Split('/').All(segment => !segment.EndsWith('.') && !segment.EndsWith(' ') && !segment.Contains('~')
                        && segment.Split('.')[0].ToLowerInvariant() is not (
                            "con" or "prn" or "aux" or "nul" or "com1" or "com2" or "com3" or "com4" or "com5"
                            or "com6" or "com7" or "com8" or "com9" or "lpt1" or "lpt2" or "lpt3" or "lpt4"
                            or "lpt5" or "lpt6" or "lpt7" or "lpt8" or "lpt9")));

    public static string HashPhysicalPath(string path) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(path)));

    public bool TryClaim(Guid expectedStamp, Guid claimStamp, DateTime utcNow, DateTime leaseExpiresAtUtc)
    {
        RequireUtc(utcNow);
        RequireUtc(leaseExpiresAtUtc);
        if (claimStamp == Guid.Empty || claimStamp == expectedStamp || leaseExpiresAtUtc <= utcNow
            || leaseExpiresAtUtc > utcNow.AddHours(1))
            throw new ArgumentException("A fresh stamp and lease of at most one hour are required.");
        bool due = State == LocalStorageQuarantineState.Pending && NextAttemptAtUtc <= utcNow
            || State == LocalStorageQuarantineState.Processing && LeaseExpiresAtUtc <= utcNow;
        if (ConcurrencyStamp != expectedStamp || !due) return false;
        State = LocalStorageQuarantineState.Processing;
        ConcurrencyStamp = claimStamp;
        LeaseExpiresAtUtc = leaseExpiresAtUtc;
        NextAttemptAtUtc = null;
        SettledAtUtc = null;
        return true;
    }

    public bool TryComplete(Guid claimStamp, LocalStorageQuarantineOutcome outcome, DateTime utcNow)
    {
        RequireUtc(utcNow);
        if (outcome is not (LocalStorageQuarantineOutcome.Moved or LocalStorageQuarantineOutcome.AlreadyQuarantined
            or LocalStorageQuarantineOutcome.AlreadyAbsent))
            throw new ArgumentException("Completion requires a settled physical outcome.");
        if (!OwnsClaim(claimStamp, utcNow)) return false;
        Settle(LocalStorageQuarantineState.Completed, outcome, null, utcNow);
        return true;
    }

    public bool TryBlock(Guid claimStamp, DateTime utcNow)
    {
        RequireUtc(utcNow);
        if (!OwnsClaim(claimStamp, utcNow)) return false;
        Settle(LocalStorageQuarantineState.Blocked, LocalStorageQuarantineOutcome.Blocked, null, utcNow);
        return true;
    }

    public bool TryRetry(Guid claimStamp, DateTime utcNow, DateTime nextAttemptAtUtc)
    {
        RequireUtc(utcNow);
        RequireUtc(nextAttemptAtUtc);
        if (nextAttemptAtUtc <= utcNow) throw new ArgumentException("Retry must be in the future.");
        if (!OwnsClaim(claimStamp, utcNow)) return false;
        Settle(LocalStorageQuarantineState.Pending, null, nextAttemptAtUtc, utcNow);
        return true;
    }

    private bool OwnsClaim(Guid stamp, DateTime utcNow) =>
        stamp != Guid.Empty && State == LocalStorageQuarantineState.Processing
        && ConcurrencyStamp == stamp && LeaseExpiresAtUtc > utcNow;

    private void Settle(LocalStorageQuarantineState state, LocalStorageQuarantineOutcome? outcome,
        DateTime? retry, DateTime utcNow)
    {
        State = state;
        Outcome = outcome;
        ConcurrencyStamp = Guid.CreateVersion7();
        NextAttemptAtUtc = retry;
        LeaseExpiresAtUtc = null;
        SettledAtUtc = state is LocalStorageQuarantineState.Completed or LocalStorageQuarantineState.Blocked ? utcNow : null;
    }

    private static void RequireUtc(DateTime value)
    {
        if (value == default || value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("A non-default UTC timestamp is required.");
    }

    public override string ToString() => nameof(LocalStorageQuarantine);
}
