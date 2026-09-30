namespace ISLAMU.Event.SetupAssistant.Terminal;

using System.Text;
using ISLAMU.Event.Setup.Core.Environment;

internal sealed class SetupTerminalSecretBuffer : IDisposable
{
    private readonly char[] _characters;
    private readonly int _maximumUtf8Bytes;
    private readonly bool _urlSafeOnly;
    private readonly object _gate = new();
    private int _count;
    private bool _disposed;

    internal SetupTerminalSecretBuffer(
        int maximumUtf8Bytes = DotenvCodec.MaximumValueUtf8Bytes,
        bool urlSafeOnly = true)
    {
        _maximumUtf8Bytes = maximumUtf8Bytes;
        _urlSafeOnly = urlSafeOnly;
        _characters = new char[maximumUtf8Bytes];
    }

    internal int Count
    {
        get
        {
            lock (_gate)
                return _count;
        }
    }

    internal bool TryReplace(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (value.Length == 0
                || value.Length > _characters.Length
                || Encoding.UTF8.GetByteCount(value) > _maximumUtf8Bytes
                || (_urlSafeOnly && value.Any(character => !IsUrlSafe(character))))
                return false;

            _characters.AsSpan().Clear();
            value.AsSpan().CopyTo(_characters);
            _count = value.Length;
            return true;
        }
    }

    internal bool TryAppend(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (value.Length == 0
                || _count + value.Length > _characters.Length
                || Encoding.UTF8.GetByteCount(_characters.AsSpan(0, _count))
                    + Encoding.UTF8.GetByteCount(value) > _maximumUtf8Bytes
                || (_urlSafeOnly && value.Any(character => !IsUrlSafe(character))))
                return false;
            value.AsSpan().CopyTo(_characters.AsSpan(_count));
            _count += value.Length;
            return true;
        }
    }

    internal void RemoveLast()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_count == 0)
                return;
            int removed = _count > 1 && char.IsLowSurrogate(_characters[_count - 1])
                && char.IsHighSurrogate(_characters[_count - 2]) ? 2 : 1;
            _characters.AsSpan(_count - removed, removed).Clear();
            _count -= removed;
        }
    }

    internal byte[] CopyUtf8Bytes()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_count == 0)
                throw new InvalidOperationException("terminal-secret-empty");
            ReadOnlySpan<char> characters = _characters.AsSpan(0, _count);
            byte[] bytes = new byte[Encoding.UTF8.GetByteCount(characters)];
            Encoding.UTF8.GetBytes(characters, bytes);
            return bytes;
        }
    }

    internal void Clear()
    {
        lock (_gate)
        {
            _characters.AsSpan().Clear();
            _count = 0;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _characters.AsSpan().Clear();
            _count = 0;
            _disposed = true;
        }
    }

    public override string ToString() => $"{nameof(SetupTerminalSecretBuffer)}:Redacted:Count={Count}";

    private static bool IsUrlSafe(char value) => value is >= 'a' and <= 'z'
        or >= 'A' and <= 'Z'
        or >= '0' and <= '9'
        or '-' or '_';
}
