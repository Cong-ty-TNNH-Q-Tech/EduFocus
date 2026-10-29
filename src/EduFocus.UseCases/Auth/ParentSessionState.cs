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
    private DateTime? _lastActivityAtUtc;

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
                ExpireIfIdle(Now());
                return _isAuthenticated;
            }
        }
    }

    public void Authenticate()
    {
        lock (_gate)
        {
            _isAuthenticated = true;
            _lastActivityAtUtc = Now();
        }
    }

    public void EnsureAuthorized(string operation)
    {
        lock (_gate)
        {
            var now = Now();
            ExpireIfIdle(now);

            if (!_isAuthenticated)
                throw new ParentAuthorizationException(operation);

            _lastActivityAtUtc = now;
        }
    }

    public void RecordActivity()
    {
        lock (_gate)
        {
            var now = Now();
            ExpireIfIdle(now);

            if (_isAuthenticated)
                _lastActivityAtUtc = now;
        }
    }

    public void Lock()
    {
        lock (_gate)
        {
            _isAuthenticated = false;
            _lastActivityAtUtc = null;
        }
    }

    private void ExpireIfIdle(DateTime now)
    {
        if (!_isAuthenticated || _lastActivityAtUtc is not { } lastActivity)
            return;

        if (now - lastActivity >= IdleTimeout)
        {
            _isAuthenticated = false;
            _lastActivityAtUtc = null;
        }
    }

    private DateTime Now() => _timeProvider.GetUtcNow().UtcDateTime;
}
