namespace ISLAMU.Event.SetupAssistant.Terminal;

using System.Text;
using ISLAMU.Event.Setup.Core.Environment;

internal sealed class SetupTerminalSecretBuffer : IDisposable
{
    private readonly char[] _characters = new char[DotenvCodec.MaximumValueUtf8Bytes];
    private readonly object _gate = new();
    private int _count;
    private bool _disposed;

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
                || Encoding.UTF8.GetByteCount(value) > DotenvCodec.MaximumValueUtf8Bytes
                || value.Any(character => !IsUrlSafe(character)))
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
                    + Encoding.UTF8.GetByteCount(value) > DotenvCodec.MaximumValueUtf8Bytes
                || value.Any(character => !IsUrlSafe(character)))
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
            _characters[--_count] = '\0';
        }
    }

    internal byte[] CopyUtf8Bytes()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_count == 0)
                throw new InvalidOperationException("terminal-secret-empty");
            byte[] bytes = new byte[_count];
            for (int index = 0; index < _count; index++)
                bytes[index] = (byte)_characters[index];
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
