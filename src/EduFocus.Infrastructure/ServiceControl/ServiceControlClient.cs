using System.IO.Pipes;
using System.Text.Json;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Services;
using EduFocus.Ports.Protection;
using EduFocus.Ports.Security;
using EduFocus.Ports.ServiceControl;

namespace EduFocus.Infrastructure.ServiceControl;

/// <summary>
/// Authenticated control client used by the WPF app in service mode. It sends
/// every command over a fresh pipe connection and never falls back to a local
/// engine, so two protection owners can never exist at the same time.
/// </summary>
public sealed class ServiceControlClient : IProtectionUseCase, IServiceControlClient
{
    private readonly ServiceControlClientOptions _options;
    private readonly IParentAuthorizer? _authorizer;

    public ServiceControlClient(
        ServiceControlClientOptions options,
        IParentAuthorizer? authorizer = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _authorizer = authorizer;

        if (options.RequireParentAuthorization && authorizer is null)
            throw new ArgumentException(
                "A parent authorizer is required when parent authorization is enabled.",
                nameof(authorizer));
        if (options.ConnectTimeout <= TimeSpan.Zero || options.ConnectTimeout > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(options), "ConnectTimeout must be between 0 and 1 minute.");
        if (options.RequestTimeout <= TimeSpan.Zero || options.RequestTimeout > TimeSpan.FromMinutes(2))
            throw new ArgumentOutOfRangeException(nameof(options), "RequestTimeout must be between 0 and 2 minutes.");
    }

    public async Task<ProtectionServiceInfo> GetServiceInfoAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(ServiceControlCommand.Ping, cancellationToken).ConfigureAwait(false);
        return response.Service
            ?? throw ProtocolError("Dịch vụ bảo vệ không trả về thông tin phiên bản.");
    }

    public async Task<ProtectionStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(ServiceControlCommand.Status, cancellationToken).ConfigureAwait(false);
        return response.Status
            ?? throw ProtocolError("Dịch vụ bảo vệ không trả về trạng thái.");
    }

    public async Task<ProtectionOperationResult> EnableAsync(CancellationToken cancellationToken = default)
    {
        _authorizer?.EnsureAuthorized("enable protection");
        return await SendOperationAsync(ServiceControlCommand.Enable, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ProtectionOperationResult> DisableAsync(CancellationToken cancellationToken = default)
    {
        _authorizer?.EnsureAuthorized("disable protection");
        return await SendOperationAsync(ServiceControlCommand.Disable, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ProtectionOperationResult> DisableForExitAsync(CancellationToken cancellationToken = default)
    {
        _authorizer?.EnsureAuthorized("exit EduFocus");
        return await SendOperationAsync(ServiceControlCommand.DisableForExit, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<ProtectionOperationResult> ReconcileAsync(CancellationToken cancellationToken = default) =>
        SendOperationAsync(ServiceControlCommand.Reconcile, cancellationToken);

    public Task<ProtectionOperationResult> EnableForScheduleAsync(CancellationToken cancellationToken = default) =>
        SendOperationAsync(ServiceControlCommand.EnableForSchedule, cancellationToken);

    public Task<ProtectionOperationResult> DisableForScheduleAsync(CancellationToken cancellationToken = default) =>
        SendOperationAsync(ServiceControlCommand.DisableForSchedule, cancellationToken);

    public async Task<ProtectionOperationResult> ReloadRulesAsync(CancellationToken cancellationToken = default)
    {
        _authorizer?.EnsureAuthorized("reload block rules");
        return await SendOperationAsync(ServiceControlCommand.ReloadRules, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ProtectionOperationResult> SendOperationAsync(
        string command,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync(command, cancellationToken).ConfigureAwait(false);
        return response.Operation
            ?? throw ProtocolError("Dịch vụ bảo vệ không trả về kết quả thao tác.");
    }

    private async Task<ServiceControlResponse> SendAsync(
        string command,
        CancellationToken cancellationToken)
    {
        using var pipe = new NamedPipeClientStream(
            ".",
            _options.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            connectTimeout.CancelAfter(_options.ConnectTimeout);

            try
            {
                await pipe.ConnectAsync(connectTimeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new ServiceControlException(
                    ServiceControlErrorCode.Unavailable,
                    "Không thể kết nối tới dịch vụ bảo vệ. Hãy kiểm tra dịch vụ EduFocus.",
                    ex);
            }
            catch (Exception ex) when (ex is IOException or TimeoutException)
            {
                throw new ServiceControlException(
                    ServiceControlErrorCode.Unavailable,
                    "Không thể kết nối tới dịch vụ bảo vệ. Hãy kiểm tra dịch vụ EduFocus.",
                    ex);
            }
        }

        VerifyServer(pipe);

        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestTimeout.CancelAfter(_options.RequestTimeout);

        try
        {
            var request = new ServiceControlRequest(ServiceControlProtocol.Version, command);
            var payload = JsonSerializer.SerializeToUtf8Bytes(request, ServiceControlFraming.JsonOptions);
            await ServiceControlFraming.WriteAsync(pipe, payload, requestTimeout.Token).ConfigureAwait(false);

            var responsePayload = await ServiceControlFraming
                .ReadAsync(pipe, ServiceControlProtocol.MaxResponseBytes, requestTimeout.Token)
                .ConfigureAwait(false);

            if (responsePayload is null)
                throw ProtocolError("Dịch vụ bảo vệ đóng kết nối trước khi trả lời.");

            var response = JsonSerializer.Deserialize<ServiceControlResponse>(
                responsePayload,
                ServiceControlFraming.JsonOptions)
                ?? throw ProtocolError("Dịch vụ bảo vệ trả về phản hồi rỗng.");

            if (response.Protocol != ServiceControlProtocol.Version)
                throw new ServiceControlException(
                    ServiceControlErrorCode.UnsupportedProtocol,
                    "Phiên bản giao thức của dịch vụ bảo vệ không tương thích.");

            if (!response.Ok)
                throw MapFailure(response, command);

            return response;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ServiceControlException(
                ServiceControlErrorCode.Timeout,
                "Dịch vụ bảo vệ không phản hồi kịp. Vui lòng thử lại.",
                ex);
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
        {
            throw new ServiceControlException(
                ServiceControlErrorCode.InternalError,
                "Không thể đọc phản hồi từ dịch vụ bảo vệ.",
                ex);
        }
    }

    private void VerifyServer(NamedPipeClientStream pipe)
    {
        if (string.IsNullOrWhiteSpace(_options.ExpectedServerImagePath))
            return;

        if (!OperatingSystem.IsWindows())
            throw new ServiceControlException(
                ServiceControlErrorCode.Rejected,
                "Không thể xác thực dịch vụ bảo vệ trên hệ điều hành này.");

        var processId = WindowsProcessIdentity.GetServerProcessId(pipe)
            ?? throw new ServiceControlException(
                ServiceControlErrorCode.Rejected,
                "Không thể xác thực tiến trình dịch vụ bảo vệ.");

        var imagePath = WindowsProcessIdentity.TryGetImagePath(processId)
            ?? throw new ServiceControlException(
                ServiceControlErrorCode.Rejected,
                "Không thể xác thực tiến trình dịch vụ bảo vệ.");

        if (!WindowsProcessIdentity.PathsEqual(imagePath, _options.ExpectedServerImagePath))
            throw new ServiceControlException(
                ServiceControlErrorCode.Rejected,
                "Tiến trình dịch vụ bảo vệ không đúng. Đã từ chối gửi lệnh.");
    }

    private static Exception MapFailure(ServiceControlResponse response, string command)
    {
        var message = response.Error ?? "Dịch vụ bảo vệ từ chối yêu cầu.";

        return response.ErrorCode switch
        {
            ServiceControlErrorCode.EngineError => new ProtectionEngineException(
                command is ServiceControlCommand.Disable
                    or ServiceControlCommand.DisableForExit
                    or ServiceControlCommand.DisableForSchedule
                    ? "remove"
                    : "apply",
                message),
            ServiceControlErrorCode.Unauthorized => new ParentAuthorizationException(command),
            _ => new ServiceControlException(response.ErrorCode ?? ServiceControlErrorCode.InternalError, message)
        };
    }

    private static ServiceControlException ProtocolError(string message) =>
        new(ServiceControlErrorCode.InternalError, message);
}
