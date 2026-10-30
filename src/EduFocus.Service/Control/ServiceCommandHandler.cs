using System.Reflection;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Protection;
using EduFocus.Ports.Security;
using EduFocus.Ports.ServiceControl;
using EduFocus.Service.Runtime;

namespace EduFocus.Service.Control;

/// <summary>
/// Applies authenticated control commands through the same use cases the
/// in-process app used. The pipe host has already verified the caller image;
/// this handler additionally requires the parent-authorization scope for the
/// commands that change protection.
/// </summary>
public sealed class ServiceCommandHandler : IServiceControlHandler
{
    private const string UnsupportedProtocolMessage = "Phiên bản giao thức điều khiển không tương thích.";
    private const string UnknownCommandMessage = "Lệnh điều khiển không được hỗ trợ.";
    private const string UnauthorizedMessage = "Thao tác này cần xác thực Parent Mode hợp lệ.";
    private const string EngineErrorMessage = "Không thể áp dụng hoặc gỡ bảo vệ. Vui lòng thử lại.";
    private const string SettingsMissingMessage =
        "Chưa hoàn tất thiết lập EduFocus. Hãy hoàn tất onboarding trước khi đổi trạng thái bảo vệ.";
    private const string InternalErrorMessage = "Dịch vụ bảo vệ gặp lỗi. Vui lòng thử lại.";

    private readonly IProtectionUseCase _protection;
    private readonly ServiceRequestAuthorizer _authorizer;
    private readonly ServiceRuntimeStatus _runtimeStatus;
    private readonly TimeProvider _timeProvider;
    private readonly string _version;

    public ServiceCommandHandler(
        IProtectionUseCase protection,
        ServiceRequestAuthorizer authorizer,
        ServiceRuntimeStatus runtimeStatus,
        TimeProvider timeProvider)
    {
        _protection = protection ?? throw new ArgumentNullException(nameof(protection));
        _authorizer = authorizer ?? throw new ArgumentNullException(nameof(authorizer));
        _runtimeStatus = runtimeStatus ?? throw new ArgumentNullException(nameof(runtimeStatus));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _version = typeof(ServiceCommandHandler).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            ?? typeof(ServiceCommandHandler).Assembly.GetName().Version?.ToString(3)
            ?? "0.0.0";
    }

    public async Task<ServiceControlResponse> HandleAsync(
        ServiceControlRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            return ServiceControlResponse.Failure(ServiceControlErrorCode.InternalError, InternalErrorMessage);

        if (request.Protocol != ServiceControlProtocol.Version)
            return ServiceControlResponse.Failure(
                ServiceControlErrorCode.UnsupportedProtocol,
                UnsupportedProtocolMessage);

        if (string.IsNullOrWhiteSpace(request.Command))
            return ServiceControlResponse.Failure(ServiceControlErrorCode.UnknownCommand, UnknownCommandMessage);

        try
        {
            switch (request.Command)
            {
                case ServiceControlCommand.Ping:
                    return ServiceControlResponse.Success(service: await BuildServiceInfoAsync(cancellationToken));

                case ServiceControlCommand.Status:
                    return ServiceControlResponse.Success(
                        service: await BuildServiceInfoAsync(cancellationToken),
                        status: await _protection.GetStatusAsync(cancellationToken));

                case ServiceControlCommand.Enable:
                    return Success(await RunAuthorizedAsync(
                        () => _protection.EnableAsync(cancellationToken)));

                case ServiceControlCommand.Disable:
                    return Success(await RunAuthorizedAsync(
                        () => _protection.DisableAsync(cancellationToken)));

                case ServiceControlCommand.DisableForExit:
                    return Success(await RunAuthorizedAsync(
                        () => _protection.DisableForExitAsync(cancellationToken)));

                case ServiceControlCommand.Reconcile:
                    return Success(await _protection.ReconcileAsync(cancellationToken));

                case ServiceControlCommand.EnableForSchedule:
                    return Success(await _protection.EnableForScheduleAsync(cancellationToken));

                case ServiceControlCommand.DisableForSchedule:
                    return Success(await _protection.DisableForScheduleAsync(cancellationToken));

                case ServiceControlCommand.ReloadRules:
                    return Success(await RunAuthorizedAsync(
                        () => _protection.ReloadRulesAsync(cancellationToken)));

                default:
                    return ServiceControlResponse.Failure(
                        ServiceControlErrorCode.UnknownCommand,
                        UnknownCommandMessage);
            }
        }
        catch (ParentAuthorizationException)
        {
            return ServiceControlResponse.Failure(ServiceControlErrorCode.Unauthorized, UnauthorizedMessage);
        }
        catch (ProtectionEngineException)
        {
            return ServiceControlResponse.Failure(ServiceControlErrorCode.EngineError, EngineErrorMessage);
        }
        catch (InvalidOperationException)
        {
            return ServiceControlResponse.Failure(
                ServiceControlErrorCode.SettingsMissing,
                SettingsMissingMessage);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ServiceControlResponse.Failure(ServiceControlErrorCode.InternalError, InternalErrorMessage);
        }
    }

    private async Task<ProtectionOperationResult> RunAuthorizedAsync(
        Func<Task<ProtectionOperationResult>> operation)
    {
        using var scope = _authorizer.BeginAuthorizedCommand();
        return await operation().ConfigureAwait(false);
    }

    private async Task<ProtectionServiceInfo> BuildServiceInfoAsync(CancellationToken cancellationToken)
    {
        var status = await _protection.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        var startedAtUtc = _runtimeStatus.StartedAtUtc ?? _timeProvider.GetUtcNow();

        return new ProtectionServiceInfo(
            _version,
            Environment.ProcessId,
            startedAtUtc,
            status.IsEnabled,
            status.ActiveSessionStartedAtUtc is { } started ? new DateTimeOffset(started, TimeSpan.Zero) : null);
    }

    private static ServiceControlResponse Success(ProtectionOperationResult operation) =>
        ServiceControlResponse.Success(operation: operation);
}
