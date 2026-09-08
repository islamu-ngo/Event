namespace ISLAMU.ReleaseEngineering.Tests;

internal sealed class RuntimePromotionTrustRootScope : IDisposable
{
    private const string RuntimeTrustRootName = "ISLAMU.ReleaseEngineering.promotion-allowed-signers";
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly Mutex ProcessGate = new(false, "ISLAMU.ReleaseEngineering.Tests.RuntimePromotionTrustRootScope");
    private readonly byte[]? originalBytes;
    private bool processGateHeld;
    private bool disposed;

    private RuntimePromotionTrustRootScope(string sourcePath)
    {
        Gate.Wait();
        try
        {
            RuntimeTrustRootPath = GetRuntimeTrustRootPath();
            try
            {
                processGateHeld = ProcessGate.WaitOne(TimeSpan.FromSeconds(30));
            }
            catch (AbandonedMutexException)
            {
                processGateHeld = true;
            }

            if (!processGateHeld) throw new IOException("runtime_promotion_trust_root_lock_timeout");
            originalBytes = File.Exists(RuntimeTrustRootPath) ? File.ReadAllBytes(RuntimeTrustRootPath) : null;
            OriginalSha256 = Sha256(originalBytes);
            AtomicWrite(RuntimeTrustRootPath, File.ReadAllBytes(sourcePath));
        }
        catch
        {
            if (processGateHeld) ProcessGate.ReleaseMutex();
            Gate.Release();
            throw;
        }
    }

    public string RuntimeTrustRootPath { get; }
    public string OriginalSha256 { get; private set; } = string.Empty;
    public string RestoredSha256 { get; private set; } = string.Empty;

    public static RuntimePromotionTrustRootScope Use(string sourcePath) => new(sourcePath);

    public static RuntimePromotionTrustRootScope UsePackagedDefault() => new(Path.Combine(RepositoryRoot.Find(), "eng", "release", "trust", "promotion-allowed-signers"));

    public static string GetRuntimeTrustRootPath() => Path.Combine(AppContext.BaseDirectory, RuntimeTrustRootName);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            if (originalBytes is null)
            {
                if (File.Exists(RuntimeTrustRootPath)) File.Delete(RuntimeTrustRootPath);
            }
            else
            {
                AtomicWrite(RuntimeTrustRootPath, originalBytes);
            }

            RestoredSha256 = Sha256(File.Exists(RuntimeTrustRootPath) ? File.ReadAllBytes(RuntimeTrustRootPath) : null);
        }
        finally
        {
            if (processGateHeld) ProcessGate.ReleaseMutex();
            Gate.Release();
        }
    }

    private static void AtomicWrite(string destinationPath, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        string tempPath = Path.Combine(Path.GetDirectoryName(destinationPath)!, $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(tempPath, bytes);
            File.Move(tempPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    private static string Sha256(byte[]? bytes) => bytes is null
        ? "absent"
        : Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
}
