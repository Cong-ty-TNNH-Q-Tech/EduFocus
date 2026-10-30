using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using EduFocus.Infrastructure.ServiceControl;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Outbound.Services;
using EduFocus.Ports.Protection;
using EduFocus.Ports.Security;
using EduFocus.Ports.ServiceControl;
using FluentAssertions;

namespace EduFocus.Infrastructure.Tests;

public sealed class ServiceControlClientTests
{
    private static readonly DateTime SessionStartUtc =
        new(2026, 10, 29, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Status_reads_the_service_status()
    {
        var expected = new ProtectionStatus(true, SessionStartUtc, true);
        await using var server = new OneShotPipeServer(
            _ => Json(ServiceControlResponse.Success(status: expected)));

        var status = await CreateClient(server).GetStatusAsync();

        status.Should().Be(expected);
        server.LastCommand.Should().Be(ServiceControlCommand.Status);
    }

    [Fact]
    public async Task Ping_reads_the_service_identity()
    {
        var info = new ProtectionServiceInfo(
            "2.0.0",
            Environment.ProcessId,
            DateTimeOffset.UnixEpoch,
            true,
            SessionStartUtc);

        await using var server = new OneShotPipeServer(
            _ => Json(ServiceControlResponse.Success(service: info)));

        var result = await CreateClient(server).GetServiceInfoAsync();

        result.Should().Be(info);
        server.LastCommand.Should().Be(ServiceControlCommand.Ping);
    }

    [Fact]
    public async Task Enable_sends_the_enable_command_and_returns_the_operation()
    {
        var expected = new ProtectionOperationResult(
            new ProtectionStatus(true, SessionStartUtc, true),
            Changed: true,
            Warnings: []);

        await using var server = new OneShotPipeServer(
            _ => Json(ServiceControlResponse.Success(operation: expected)));

        var result = await CreateClient(server).EnableAsync();

        result.Changed.Should().BeTrue();
        result.Status.IsEnabled.Should().BeTrue();
        server.LastCommand.Should().Be(ServiceControlCommand.Enable);
    }

    [Fact]
    public async Task A_parent_command_without_parent_mode_never_reaches_the_pipe()
    {
        await using var server = new OneShotPipeServer(
            _ => Json(ServiceControlResponse.Success()));
        var authorizer = new FakeAuthorizer { IsAuthenticated = false };
        var client = CreateClient(server, authorizer: authorizer);

        var act = () => client.DisableAsync();

        await act.Should().ThrowAsync<ParentAuthorizationException>();
        authorizer.Checks.Should().Be(1);
        server.Connections.Should().Be(0);
    }

    [Fact]
    public async Task Schedule_commands_do_not_require_parent_mode()
    {
        var expected = new ProtectionOperationResult(
            new ProtectionStatus(true, SessionStartUtc, true),
            Changed: true,
            Warnings: []);

        await using var server = new OneShotPipeServer(
            _ => Json(ServiceControlResponse.Success(operation: expected)));
        var authorizer = new FakeAuthorizer { IsAuthenticated = false };
        var client = CreateClient(server, authorizer: authorizer);

        var result = await client.EnableForScheduleAsync();

        result.Changed.Should().BeTrue();
        authorizer.Checks.Should().Be(0);
        server.LastCommand.Should().Be(ServiceControlCommand.EnableForSchedule);
    }

    [Fact]
    public async Task An_unauthorized_failure_maps_to_the_parent_exception()
    {
        await using var server = new OneShotPipeServer(
            _ => Json(ServiceControlResponse.Failure(
                ServiceControlErrorCode.Unauthorized,
                "Thao tác này cần xác thực Parent Mode hợp lệ.")));

        var act = () => CreateClient(server).DisableAsync();

        await act.Should().ThrowAsync<ParentAuthorizationException>();
    }

    [Fact]
    public async Task An_engine_failure_maps_to_the_engine_exception_for_the_command_direction()
    {
        await using var server = new OneShotPipeServer(
            _ => Json(ServiceControlResponse.Failure(
                ServiceControlErrorCode.EngineError,
                "Không thể áp dụng hoặc gỡ bảo vệ.")));

        var act = () => CreateClient(server).DisableForExitAsync();

        var exception = await act.Should().ThrowAsync<ProtectionEngineException>();
        exception.Which.Operation.Should().Be("remove");
    }

    [Fact]
    public async Task An_unsupported_protocol_response_is_rejected()
    {
        await using var server = new OneShotPipeServer(
            _ => Json(new ServiceControlResponse(
                ServiceControlProtocol.Version + 1,
                true,
                null,
                null,
                null,
                null,
                null)));

        var act = () => CreateClient(server).GetStatusAsync();

        var exception = await act.Should().ThrowAsync<ServiceControlException>();
        exception.Which.Code.Should().Be(ServiceControlErrorCode.UnsupportedProtocol);
    }

    [Fact]
    public async Task A_malformed_response_maps_to_an_internal_error()
    {
        await using var server = new OneShotPipeServer(
            _ => Encoding.UTF8.GetBytes("this is not json"));

        var act = () => CreateClient(server).GetStatusAsync();

        var exception = await act.Should().ThrowAsync<ServiceControlException>();
        exception.Which.Code.Should().Be(ServiceControlErrorCode.InternalError);
    }

    [Fact]
    public async Task A_response_without_the_expected_payload_is_a_protocol_error()
    {
        await using var server = new OneShotPipeServer(
            _ => Json(ServiceControlResponse.Success()));

        var act = () => CreateClient(server).GetStatusAsync();

        var exception = await act.Should().ThrowAsync<ServiceControlException>();
        exception.Which.Code.Should().Be(ServiceControlErrorCode.InternalError);
    }

    [Fact]
    public async Task An_unavailable_service_is_reported_without_falling_back_locally()
    {
        var client = new ServiceControlClient(new ServiceControlClientOptions
        {
            PipeName = UniquePipeName(),
            ConnectTimeout = TimeSpan.FromMilliseconds(300),
            RequireParentAuthorization = false
        });

        var act = () => client.GetStatusAsync();

        var exception = await act.Should().ThrowAsync<ServiceControlException>();
        exception.Which.Code.Should().Be(ServiceControlErrorCode.Unavailable);
    }

    [Fact]
    public async Task A_pipe_server_that_is_not_the_installed_service_is_rejected()
    {
        await using var server = new OneShotPipeServer(
            _ => Json(ServiceControlResponse.Success(
                status: new ProtectionStatus(true, null, true))));

        var client = new ServiceControlClient(new ServiceControlClientOptions
        {
            PipeName = server.PipeName,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            RequestTimeout = TimeSpan.FromSeconds(5),
            RequireParentAuthorization = false,
            ExpectedServerImagePath = Path.Combine(
                Path.GetTempPath(),
                "not-the-edufocus-service.exe")
        });

        var act = () => client.GetStatusAsync();

        var exception = await act.Should().ThrowAsync<ServiceControlException>();
        exception.Which.Code.Should().Be(ServiceControlErrorCode.Rejected);
        server.LastCommand.Should().BeNull();
    }

    [Fact]
    public void A_client_without_an_authorizer_is_rejected_when_authorization_is_required()
    {
        var act = () => new ServiceControlClient(new ServiceControlClientOptions());

        act.Should().Throw<ArgumentException>();
    }

    private static string UniquePipeName() => $"EduFocus.Test.{Guid.NewGuid():N}";

    private static ServiceControlClient CreateClient(
        OneShotPipeServer server,
        FakeAuthorizer? authorizer = null) =>
        new(
            new ServiceControlClientOptions
            {
                PipeName = server.PipeName,
                ConnectTimeout = TimeSpan.FromSeconds(5),
                RequestTimeout = TimeSpan.FromSeconds(5),
                RequireParentAuthorization = authorizer is not null
            },
            authorizer);

    private static byte[] Json(ServiceControlResponse response) =>
        JsonSerializer.SerializeToUtf8Bytes(response, ServiceControlFraming.JsonOptions);

    private sealed class FakeAuthorizer : IParentAuthorizer
    {
        public bool IsAuthenticated { get; set; } = true;

        public int Checks { get; private set; }

        public void EnsureAuthorized(string operation)
        {
            Checks++;

            if (!IsAuthenticated)
                throw new ParentAuthorizationException(operation);
        }

        public void RecordActivity()
        {
        }

        public void Lock() => IsAuthenticated = false;
    }

    internal sealed class OneShotPipeServer : IAsyncDisposable
    {
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loop;
        private int _connections;
        private string? _lastCommand;

        public OneShotPipeServer(Func<ServiceControlRequest?, byte[]?> respond)
        {
            PipeName = UniquePipeName();

            _loop = Task.Run(async () =>
            {
                try
                {
                    await using var pipe = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.InOut,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await pipe.WaitForConnectionAsync(_cts.Token);
                    Interlocked.Increment(ref _connections);

                    var payload = await ServiceControlFraming.ReadAsync(
                        pipe,
                        ServiceControlProtocol.MaxRequestBytes,
                        _cts.Token);

                    var request = payload is null
                        ? null
                        : JsonSerializer.Deserialize<ServiceControlRequest>(
                            payload,
                            ServiceControlFraming.JsonOptions);

                    _lastCommand = request?.Command;

                    var response = respond(request);
                    if (response is not null)
                        await ServiceControlFraming.WriteAsync(pipe, response, _cts.Token);
                }
                catch (OperationCanceledException)
                {
                }
                catch (IOException)
                {
                }
            });
        }

        public string PipeName { get; }

        public int Connections => Volatile.Read(ref _connections);

        public string? LastCommand => _lastCommand;

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();

            var completed = false;

            try
            {
                await _loop.WaitAsync(TimeSpan.FromSeconds(5));
                completed = true;
            }
            catch (TimeoutException)
            {
            }

            if (completed)
                _cts.Dispose();
        }
    }
}
