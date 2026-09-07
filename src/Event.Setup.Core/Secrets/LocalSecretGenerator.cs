namespace ISLAMU.Event.Setup.Core.Environment;

using System.Security.Cryptography;

public enum LocalSecretGenerationProfile
{
    OpaqueUrlSafe256,
}

public sealed class GeneratedDotenvValue : IDisposable
{
    private char[]? _characters;

    internal GeneratedDotenvValue(char[] characters)
    {
        _characters = characters;
    }

    public DotenvProvenance Provenance => DotenvProvenance.Generated;
    public int CharacterCount => _characters?.Length ?? 0;

    public string CopyValue()
    {
        char[] characters = _characters
            ?? throw new ObjectDisposedException(nameof(GeneratedDotenvValue));
        return new string(characters);
    }

    public byte[] CopyUtf8Bytes()
    {
        char[] characters = _characters
            ?? throw new ObjectDisposedException(nameof(GeneratedDotenvValue));
        byte[] bytes = new byte[characters.Length];
        for (int index = 0; index < characters.Length; index++)
        {
            char character = characters[index];
            if (character > 0x7f)
                throw new InvalidOperationException("generated-secret-not-url-safe-ascii");
            bytes[index] = (byte)character;
        }
        return bytes;
    }

    public void Dispose()
    {
        char[]? characters = Interlocked.Exchange(ref _characters, null);
        if (characters is not null) Array.Clear(characters);
    }

    public override string ToString() => $"{nameof(GeneratedDotenvValue)}:Redacted";
}

public sealed class LocalSecretGenerationResult : IDisposable
{
    private readonly EnvironmentDiagnostic[] _diagnostics;

    internal LocalSecretGenerationResult(
        GeneratedDotenvValue? output,
        IEnumerable<EnvironmentDiagnostic> diagnostics)
    {
        Output = output;
        _diagnostics = diagnostics.ToArray();
    }

    public GeneratedDotenvValue? Output { get; }
    public IReadOnlyList<EnvironmentDiagnostic> Diagnostics =>
        Array.AsReadOnly((EnvironmentDiagnostic[])_diagnostics.Clone());
    public bool Succeeded => Output is not null && _diagnostics.Length == 0;
    public void Dispose() => Output?.Dispose();
    public override string ToString() =>
        $"{nameof(LocalSecretGenerationResult)}:Succeeded={Succeeded}:Diagnostics={_diagnostics.Length}";
}

public sealed class LocalSecretGenerator : IDisposable
{
    private const int EntropyBytes = 32;
    private readonly RandomNumberGenerator _entropy;
    private bool _disposed;

    internal LocalSecretGenerator(RandomNumberGenerator entropy)
    {
        ArgumentNullException.ThrowIfNull(entropy);
        _entropy = entropy;
    }

    public static LocalSecretGenerator Create() =>
        new(RandomNumberGenerator.Create());

    public LocalSecretGenerationResult Generate(string key, LocalSecretGenerationProfile profile)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(key);
        if (!string.Equals(key, "SETUP_SECRET", StringComparison.Ordinal))
            return Denied("secret-generation-key-unapproved");
        if (profile != LocalSecretGenerationProfile.OpaqueUrlSafe256)
            return Denied("secret-generation-profile-unapproved");

        byte[] bytes = new byte[EntropyBytes];
        char[] encoded = new char[44];
        try
        {
            _entropy.GetBytes(bytes);
            if (!Convert.TryToBase64Chars(bytes, encoded, out int written) || written != encoded.Length)
                return Denied("secret-generation-encoding-failed");
            for (int index = 0; index < written; index++)
            {
                if (encoded[index] == '+') encoded[index] = '-';
                else if (encoded[index] == '/') encoded[index] = '_';
            }
            int length = written;
            while (length > 0 && encoded[length - 1] == '=') length--;
            var owned = new char[length];
            encoded.AsSpan(0, length).CopyTo(owned);
            return new LocalSecretGenerationResult(new GeneratedDotenvValue(owned), []);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            Array.Clear(encoded);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _entropy.Dispose();
    }

    public override string ToString() => $"{nameof(LocalSecretGenerator)}:OpaqueUrlSafe256";

    private static LocalSecretGenerationResult Denied(string code) =>
        new(null, [new EnvironmentDiagnostic(code, "$.generation", null, "secret-generation")]);
}
