using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Services;
using EduFocus.Ports.ServiceControl;

namespace EduFocus.Infrastructure.ServiceControl;

/// <summary>
/// Loopback control endpoint of the protection service. The pipe ACL allows
/// only SYSTEM and Administrators, every connection is checked against the
/// installed UI image path, and each connection carries exactly one command.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ServiceControlServer : IAsyncDisposable
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    private readonly ServiceControlServerOptions _options;
    private readonly IServiceControlHandler _handler;
    private readonly IPipeClientVerifier _verifier;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private readonly SemaphoreSlim _connectionSlots;
    private readonly ConcurrentDictionary<long, Task> _connections = new();

    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;
    private long _connectionSequence;
    private volatile bool _running;

    public ServiceControlServer(
        ServiceControlServerOptions options,
        IServiceControlHandler handler,
        IPipeClientVerifier verifier,
        IAppLogger logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (string.IsNullOrWhiteSpace(options.PipeName))
            throw new ArgumentException("A pipe name is required.", nameof(options));
        if (options.MaxClients is < 1 or > 64)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxClients must be between 1 and 64.");
        if (options.RequestTimeout <= TimeSpan.Zero || options.RequestTimeout > TimeSpan.FromMinutes(2))
            throw new ArgumentOutOfRangeException(nameof(options), "RequestTimeout must be between 0 and 2 minutes.");

        _connectionSlots = new SemaphoreSlim(options.MaxClients, options.MaxClients);
    }

    public bool IsRunning => _running;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cts is not null)
                return;

            var cts = new CancellationTokenSource();
            _cts = cts;
            _running = true;
            _acceptLoop = Task.Run(() => AcceptLoopAsync(cts.Token), CancellationToken.None);

            _logger.Info(LogCategories.Protection, "Protection service control pipe started.");
        }
        finally
        {
            _stateGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task? acceptLoop;
        CancellationTokenSource? cts;

        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cts is null)
                return;

            _running = false;
            cts = _cts;
            _cts = null;
            acceptLoop = _acceptLoop;
            _acceptLoop = null;

            cts.Cancel();
        }
        finally
        {
            _stateGate.Release();
        }

        if (acceptLoop is not null)
        {
            try
            {
                await acceptLoop.WaitAsync(StopTimeout, CancellationToken.None).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _logger.Warning(LogCategories.Protection, "Service control pipe did not stop in time.");
            }
            catch (Exception ex)
            {
                _logger.Warning(
                    LogCategories.Protection,
                    $"Service control pipe stopped with an error ({ex.GetType().Name}).");
            }
        }

        var connections = _connections.Values.ToArray();
        if (connections.Length > 0)
        {
            try
            {
                await Task.WhenAll(connections).WaitAsync(StopTimeout, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _logger.Warning(LogCategories.Protection, "An in-flight service control command did not finish in time.");
            }
            catch (Exception ex)
            {
                _logger.Warning(
                    LogCategories.Protection,
                    $"An in-flight service control command failed ({ex.GetType().Name}).");
            }
        }

        _connections.Clear();
        cts.Dispose();
        _logger.Info(LogCategories.Protection, "Protection service control pipe stopped.");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _stateGate.Dispose();
        _connectionSlots.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        NamedPipeServerStream? pending = null;
        var slotAcquired = false;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await _connectionSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
                slotAcquired = true;

                try
                {
                    pending = CreateListener();
                    await pending.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (IOException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    pending?.Dispose();
                    pending = null;
                    _connectionSlots.Release();
                    slotAcquired = false;
                    _logger.Warning(
                        LogCategories.Protection,
                        $"Transient pipe listener error ({ex.GetType().Name}); retrying.");
                    await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var connected = pending;
                pending = null;
                slotAcquired = false;

                var id = Interlocked.Increment(ref _connectionSequence);
                var task = HandleConnectionWithSlotAsync(connected, cancellationToken);
                _connections[id] = task;
                _ = task.ContinueWith(
                    completedTask => RemoveConnection(id),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            _running = false;
            _logger.Error(LogCategories.Protection, "Service control pipe listener failed.", ex);
        }
        finally
        {
            if (slotAcquired)
            {
                try
                {
                    _connectionSlots.Release();
                }
                catch (ObjectDisposedException)
                {
                }
            }

            pending?.Dispose();
        }
    }

    private async Task HandleConnectionWithSlotAsync(
        NamedPipeServerStream pipe,
        CancellationToken serverToken)
    {
        try
        {
            await HandleConnectionAsync(pipe, serverToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                _connectionSlots.Release();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private async Task HandleConnectionAsync(
        NamedPipeServerStream pipe,
        CancellationToken serverToken)
    {
        using (pipe)
        {
            try
            {
                if (!_verifier.IsAllowed(pipe, out var reason))
                {
                    _logger.Warning(
                        LogCategories.Security,
                        $"Rejected a protection service control connection: {reason}.");
                    return;
                }

                using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(serverToken);
                requestTimeout.CancelAfter(_options.RequestTimeout);

                ServiceControlRequest? request;
                try
                {
                    var payload = await ServiceControlFraming
                        .ReadAsync(pipe, ServiceControlProtocol.MaxRequestBytes, requestTimeout.Token)
                        .ConfigureAwait(false);

                    if (payload is null)
                        return;

                    request = JsonSerializer.Deserialize<ServiceControlRequest>(
                        payload,
                        ServiceControlFraming.JsonOptions);
                }
                catch (InvalidDataException ex)
                {
                    _logger.Warning(LogCategories.Security, $"Rejected a malformed service control request: {ex.Message}");
                    await TryWriteAsync(
                        pipe,
                        ServiceControlResponse.Failure(
                            ServiceControlErrorCode.InternalError,
                            "Yêu cầu điều khiển không hợp lệ."),
                        serverToken).ConfigureAwait(false);
                    return;
                }
                catch (JsonException)
                {
                    _logger.Warning(LogCategories.Security, "Rejected a malformed service control request.");
                    await TryWriteAsync(
                        pipe,
                        ServiceControlResponse.Failure(
                            ServiceControlErrorCode.InternalError,
                            "Yêu cầu điều khiển không hợp lệ."),
                        serverToken).ConfigureAwait(false);
                    return;
                }

                if (request is null)
                {
                    await TryWriteAsync(
                        pipe,
                        ServiceControlResponse.Failure(
                            ServiceControlErrorCode.InternalError,
                            "Yêu cầu điều khiển không hợp lệ."),
                        serverToken).ConfigureAwait(false);
                    return;
                }

                ServiceControlResponse response;

                try
                {
                    response = await _handler.HandleAsync(request, requestTimeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!serverToken.IsCancellationRequested)
                {
                    response = ServiceControlResponse.Failure(
                        ServiceControlErrorCode.Timeout,
                        "Dịch vụ bảo vệ không phản hồi kịp. Vui lòng thử lại.");
                }
                catch (Exception ex)
                {
                    _logger.Error(
                        LogCategories.Protection,
                        $"Service control command '{request.Command}' failed.",
                        ex);
                    response = ServiceControlResponse.Failure(
                        ServiceControlErrorCode.InternalError,
                        "Dịch vụ bảo vệ gặp lỗi. Vui lòng thử lại.");
                }

                await TryWriteAsync(pipe, response, serverToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (IOException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception ex)
            {
                _logger.Warning(
                    LogCategories.Protection,
                    $"Service control connection failed ({ex.GetType().Name}).");
            }
        }
    }

    private async Task TryWriteAsync(
        NamedPipeServerStream pipe,
        ServiceControlResponse response,
        CancellationToken cancellationToken)
    {
        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(response, ServiceControlFraming.JsonOptions);
            if (payload.Length > ServiceControlProtocol.MaxResponseBytes)
                return;

            await ServiceControlFraming.WriteAsync(pipe, payload, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private NamedPipeServerStream CreateListener()
    {
        var pipeOptions = PipeOptions.Asynchronous | PipeOptions.WriteThrough;
        var inputBuffer = ServiceControlProtocol.MaxRequestBytes;
        var outputBuffer = ServiceControlProtocol.MaxResponseBytes;

        if (!OperatingSystem.IsWindows())
        {
            return new NamedPipeServerStream(
                _options.PipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                pipeOptions,
                inputBuffer,
                outputBuffer);
        }

        return NamedPipeServerStreamAcl.Create(
            _options.PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            pipeOptions,
            inputBuffer,
            outputBuffer,
            CreateSecurity());
    }

    private PipeSecurity CreateSecurity()
    {
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        AddFullControl(security, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null));
        AddFullControl(security, new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));

        if (_options.AllowCurrentUser && WindowsIdentity.GetCurrent().User is { } currentUser)
            AddFullControl(security, currentUser);

        return security;
    }

    private static void AddFullControl(PipeSecurity security, SecurityIdentifier identity) =>
        security.AddAccessRule(
            new PipeAccessRule(identity, PipeAccessRights.FullControl, AccessControlType.Allow));

    private void RemoveConnection(long id) => _connections.TryRemove(id, out _);
}
