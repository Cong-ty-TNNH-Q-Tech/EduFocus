using EduFocus.Ports.Outbound.Services;
using EduFocus.Ports.Security;

namespace EduFocus.UseCases.Auth;

/// <summary>
/// In-memory Parent Mode session. It never survives a process restart and
/// expires after five minutes without activity (UC02, system architecture §9.2).
/// </summary>
public sealed class ParentSessionState : IParentAuthorizer
{
    public static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromMinutes(5);

    private readonly TimeProvider _timeProvider;
    private readonly object _gate = new();
    private bool _isAuthenticated;
    private long? _lastActivityTimestamp;
    private int _activeScopeCount;
    private long _generation;

    public ParentSessionState(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public TimeSpan IdleTimeout { get; init; } = DefaultIdleTimeout;

    public bool IsAuthenticated
    {
        get
        {
            lock (_gate)
            {
                ExpireIfIdle(NowTimestamp());
                return _isAuthenticated;
            }
        }
    }

    public void Authenticate()
    {
        lock (_gate)
        {
            _generation++;
            _isAuthenticated = true;
            _lastActivityTimestamp = NowTimestamp();
        }
    }

    public void EnsureAuthorized(string operation)
    {
        lock (_gate)
        {
            var now = NowTimestamp();
            ExpireIfIdle(now);

            if (!_isAuthenticated)
                throw new ParentAuthorizationException(operation);

            _lastActivityTimestamp = now;
        }
    }

    public void RecordActivity()
    {
        lock (_gate)
        {
            var now = NowTimestamp();
            ExpireIfIdle(now);

            if (_isAuthenticated)
                _lastActivityTimestamp = now;
        }
    }

    public IDisposable? BeginActiveScope()
    {
        lock (_gate)
        {
            var now = NowTimestamp();
            ExpireIfIdle(now);

            if (!_isAuthenticated)
                return null;

            _lastActivityTimestamp = now;
            _activeScopeCount++;
            return new ActiveScope(this, _generation);
        }
    }

    public void Lock()
    {
        lock (_gate)
        {
            _generation++;
            _isAuthenticated = false;
            _lastActivityTimestamp = null;
            _activeScopeCount = 0;
        }
    }

    private void EndScope(long generation)
    {
        lock (_gate)
        {
            if (generation != _generation)
                return;

            if (_activeScopeCount > 0)
                _activeScopeCount--;

            if (_isAuthenticated)
                _lastActivityTimestamp = NowTimestamp();
        }
    }

    private void ExpireIfIdle(long nowTimestamp)
    {
        if (!_isAuthenticated || _lastActivityTimestamp is not { } lastActivity)
            return;

        if (_activeScopeCount > 0)
        {
            _lastActivityTimestamp = nowTimestamp;
            return;
        }

        if (_timeProvider.GetElapsedTime(lastActivity, nowTimestamp) >= IdleTimeout)
        {
            _isAuthenticated = false;
            _lastActivityTimestamp = null;
        }
    }

    private long NowTimestamp() => _timeProvider.GetTimestamp();

    private sealed class ActiveScope : IDisposable
    {
        private readonly ParentSessionState _parent;
        private readonly long _generation;
        private int _disposed;

        public ActiveScope(ParentSessionState parent, long generation)
        {
            _parent = parent;
            _generation = generation;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _parent.EndScope(_generation);
            }
        }
    }
}
