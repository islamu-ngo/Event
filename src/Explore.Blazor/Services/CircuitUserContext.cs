namespace Explore.Blazor.Services;

/// <summary>
/// Provides access to the current circuit's user identity across async context boundaries.
/// Uses AsyncLocal so the value flows through Blazor circuit-dispatched events even when
/// IHttpContextAccessor.HttpContext is null.
/// </summary>
public interface ICircuitUserContext
{
    string? UserId { get; }
    string? SessionId { get; }
    void SetUserId(string? userId);
    void SetSessionId(string? sessionId);
    void Clear();
    IDisposable BeginActivityScope();
}

public sealed class CircuitUserContext : ICircuitUserContext
{
    private static readonly AsyncLocal<UserIdHolder?> _currentUserId = new();
    private string? _userId;
    private string? _sessionId;

    public string? UserId => _currentUserId.Value?.UserId ?? _userId;

    public string? SessionId => _currentUserId.Value?.SessionId ?? _sessionId;

    public void SetUserId(string? userId)
    {
        _userId = string.IsNullOrWhiteSpace(userId) ? null : userId;
    }

    public void SetSessionId(string? sessionId)
    {
        _sessionId = string.IsNullOrWhiteSpace(sessionId) ? null : sessionId;
    }

    public void Clear()
    {
        _userId = null;
        _sessionId = null;
        _currentUserId.Value = null;
    }

    public IDisposable BeginActivityScope()
    {
        var previous = _currentUserId.Value;
        _currentUserId.Value = new UserIdHolder { UserId = _userId, SessionId = _sessionId };
        return new Scope(() => _currentUserId.Value = previous);
    }

    private sealed class UserIdHolder
    {
        public string? UserId { get; set; }

        public string? SessionId { get; set; }
    }

    private sealed class Scope : IDisposable
    {
        private readonly Action _onDispose;
        private bool _disposed;

        public Scope(Action onDispose)
        {
            _onDispose = onDispose;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _onDispose();
        }
    }
}
