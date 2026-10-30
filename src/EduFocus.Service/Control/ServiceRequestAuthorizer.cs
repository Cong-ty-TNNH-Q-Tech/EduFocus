using EduFocus.Ports.Outbound.Services;
using EduFocus.Ports.Security;

namespace EduFocus.Service.Control;

/// <summary>
/// The service never receives a PIN: the UI authenticates the parent before it
/// sends a command. This authorizer opens a short-lived, async-local scope
/// around the three parent commands so no other service code path can enable
/// or disable protection by accident.
/// </summary>
public sealed class ServiceRequestAuthorizer : IParentAuthorizer
{
    private readonly AsyncLocal<int> _scopeDepth = new();

    public bool IsAuthenticated => _scopeDepth.Value > 0;

    public void EnsureAuthorized(string operation)
    {
        if (_scopeDepth.Value == 0)
            throw new ParentAuthorizationException(operation);
    }

    public void RecordActivity()
    {
    }

    public void Lock()
    {
    }

    public IDisposable BeginAuthorizedCommand() => new Scope(this);

    private sealed class Scope : IDisposable
    {
        private readonly ServiceRequestAuthorizer _owner;
        private bool _disposed;

        public Scope(ServiceRequestAuthorizer owner)
        {
            _owner = owner;
            _owner._scopeDepth.Value++;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _owner._scopeDepth.Value--;
        }
    }
}
