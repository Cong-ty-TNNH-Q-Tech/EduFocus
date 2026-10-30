using System.Buffers.Binary;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using EduFocus.Infrastructure.ServiceControl;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Services;
using EduFocus.Ports.Protection;
using EduFocus.Ports.Security;
using EduFocus.Ports.ServiceControl;
using FluentAssertions;

namespace EduFocus.Infrastructure.Tests;

[SupportedOSPlatform("windows")]
public sealed class ServiceControlServerTests
{
    private static readonly DateTime SessionStartUtc =
        new(2026, 10, 29, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Start_is_idempotent_and_stop_is_safe()
    {
        await using var harness = new ServerHarness();

        await harness.Server.StartAsync();
        await harness.Server.StartAsync();

        harness.Server.IsRunning.Should().BeTrue();

        await harness.Server.StopAsync();
        await harness.Server.StopAsync();

        harness.Server.IsRunning.Should().BeFalse();
        harness.Handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Enter_commands_are_answered_for_the_verified_client()
    {
        await using var harness = new ServerHarness();
        await harness.Server.StartAsync();

        var client = CreateClient(harness.PipeName);
        var info = await client.GetServiceInfoAsync();
        var status = await client.GetStatusAsync();

        info.Version.Should().NotBeNullOrWhiteSpace();
        status.IsEnabled.Should().BeTrue();
        harness.Handler.Requests.Select(request => request.Command).Should().Equal(
            ServiceControlCommand.Ping,
            ServiceControlCommand.Status);
        harness.Handler.Requests.Should().OnlyContain(
            request => request.Protocol == ServiceControlProtocol.Version);
    }

    [Fact]
    public async Task A_rejected_client_is_logged_and_no_command_runs()
    {
        await using var harness = new ServerHarness(allowAll: false);
        await harness.Server.StartAsync();

        var act = () => CreateClient(harness.PipeName).GetServiceInfoAsync();

        await act.Should().ThrowAsync<ServiceControlException>();
        harness.Handler.Requests.Should().BeEmpty();
        harness.Logger.Warnings.Should().Contain(
            warning => warning.Contains("Rejected a protection service control connection"));
    }

    [Fact]
    public async Task An_oversized_request_is_rejected_without_dispatch()
    {
        await using var harness = new ServerHarness();
        await harness.Server.StartAsync();

        var response = await SendRawAsync(
            harness.PipeName,
            header => BinaryPrimitives.WriteUInt32LittleEndian(
                header,
                (uint)ServiceControlProtocol.MaxRequestBytes + 1),
            payload: []);

        response.Ok.Should().BeFalse();
        response.ErrorCode.Should().Be(ServiceControlErrorCode.InternalError);
        harness.Handler.Requests.Should().BeEmpty();
        harness.Logger.Warnings.Should().Contain(
            warning => warning.Contains("malformed service control request"));
    }

    [Fact]
    public async Task A_malformed_json_request_is_rejected_without_dispatch()
    {
        await using var harness = new ServerHarness();
        await harness.Server.StartAsync();

        var response = await SendRawAsync(
            harness.PipeName,
            header => BinaryPrimitives.WriteUInt32LittleEndian(header, 5),
            payload: Encoding.UTF8.GetBytes("{{{{{"));

        response.Ok.Should().BeFalse();
        response.ErrorCode.Should().Be(ServiceControlErrorCode.InternalError);
        harness.Handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_handler_error_becomes_a_sanitized_internal_error()
    {
        await using var harness = new ServerHarness();
        harness.Handler.Responder = _ => throw new InvalidOperationException("secret database path");
        await harness.Server.StartAsync();

        var act = () => CreateClient(harness.PipeName).GetStatusAsync();

        var exception = await act.Should().ThrowAsync<ServiceControlException>();
        exception.Which.Code.Should().Be(ServiceControlErrorCode.InternalError);
        exception.Which.Message.Should().NotContain("secret database path");
        harness.Handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task A_handler_failure_response_is_forwarded()
    {
        await using var harness = new ServerHarness();
        harness.Handler.Responder = _ => ServiceControlResponse.Failure(
            ServiceControlErrorCode.Unauthorized,
            "Thao tác này cần xác thực Parent Mode hợp lệ.");
        await harness.Server.StartAsync();

        var act = () => CreateClient(harness.PipeName).DisableAsync();

        await act.Should().ThrowAsync<ParentAuthorizationException>();
    }

    [Fact]
    public async Task A_command_that_exceeds_the_server_timeout_is_reported()
    {
        await using var harness = new ServerHarness(requestTimeout: TimeSpan.FromMilliseconds(150));
        harness.Handler.AsyncResponder = async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return ServiceControlResponse.Success();
        };
        await harness.Server.StartAsync();

        var act = () => CreateClient(harness.PipeName).EnableAsync();

        var exception = await act.Should().ThrowAsync<ServiceControlException>();
        exception.Which.Code.Should().Be(ServiceControlErrorCode.Timeout);
    }

    [Fact]
    public async Task The_server_accepts_sequential_connections()
    {
        await using var harness = new ServerHarness();
        await harness.Server.StartAsync();

        await CreateClient(harness.PipeName).GetStatusAsync();
        await CreateClient(harness.PipeName).GetStatusAsync();
        await CreateClient(harness.PipeName).GetServiceInfoAsync();

        harness.Handler.Requests.Should().HaveCount(3);
    }

    private static ServiceControlClient CreateClient(string pipeName) =>
        new(new ServiceControlClientOptions
        {
            PipeName = pipeName,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            RequestTimeout = TimeSpan.FromSeconds(5),
            RequireParentAuthorization = false
        });

    private static async Task<ServiceControlResponse> SendRawAsync(
        string pipeName,
        Action<byte[]> writeHeader,
        byte[] payload)
    {
        await using var pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        pipe.Connect(5000);
        var header = new byte[4];
        writeHeader(header);
        await pipe.WriteAsync(header);
        await pipe.WriteAsync(payload);
        await pipe.FlushAsync();

        var responsePayload = await ServiceControlFraming.ReadAsync(
            pipe,
            ServiceControlProtocol.MaxResponseBytes,
            CancellationToken.None);

        responsePayload.Should().NotBeNull();
        return JsonSerializer.Deserialize<ServiceControlResponse>(
            responsePayload!,
            ServiceControlFraming.JsonOptions)!;
    }

    private sealed class ServerHarness : IAsyncDisposable
    {
        public ServerHarness(
            bool allowAll = true,
            TimeSpan? requestTimeout = null)
        {
            PipeName = $"EduFocus.Test.{Guid.NewGuid():N}";

            var options = new ServiceControlServerOptions
            {
                PipeName = PipeName,
                AllowCurrentUser = true,
                RequestTimeout = requestTimeout ?? TimeSpan.FromSeconds(5)
            };

            Server = new ServiceControlServer(
                options,
                Handler,
                allowAll ? new AllowAllVerifier() : new DenyAllVerifier(),
                Logger);
        }

        public string PipeName { get; }

        public RecordingHandler Handler { get; } = new();

        public RecordingLogger Logger { get; } = new();

        public ServiceControlServer Server { get; }

        public ValueTask DisposeAsync() => Server.DisposeAsync();
    }

    private sealed class AllowAllVerifier : IPipeClientVerifier
    {
        public bool IsAllowed(NamedPipeServerStream pipe, out string reason)
        {
            reason = string.Empty;
            return true;
        }
    }

    private sealed class DenyAllVerifier : IPipeClientVerifier
    {
        public bool IsAllowed(NamedPipeServerStream pipe, out string reason)
        {
            reason = "not the EduFocus UI";
            return false;
        }
    }

    private sealed class RecordingHandler : IServiceControlHandler
    {
        public List<ServiceControlRequest> Requests { get; } = [];

        public Func<ServiceControlRequest, ServiceControlResponse>? Responder { get; set; }

        public Func<ServiceControlRequest, CancellationToken, Task<ServiceControlResponse>>?
            AsyncResponder { get; set; }

        public Task<ServiceControlResponse> HandleAsync(
            ServiceControlRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);

            if (AsyncResponder is not null)
                return AsyncResponder(request, cancellationToken);

            var response = Responder?.Invoke(request)
                ?? ServiceControlResponse.Success(
                    service: new ProtectionServiceInfo(
                        "2.0.0",
                        Environment.ProcessId,
                        DateTimeOffset.UnixEpoch,
                        true,
                        SessionStartUtc),
                    status: new ProtectionStatus(true, SessionStartUtc, true),
                    operation: new ProtectionOperationResult(
                        new ProtectionStatus(true, SessionStartUtc, true),
                        Changed: true,
                        Warnings: []));

            return Task.FromResult(response);
        }
    }

    private sealed class RecordingLogger : IAppLogger
    {
        public List<string> Infos { get; } = [];

        public List<string> Warnings { get; } = [];

        public List<string> Errors { get; } = [];

        public void Info(string category, string message) => Infos.Add(message);

        public void Warning(string category, string message) => Warnings.Add(message);

        public void Error(string category, string message, Exception? exception = null) =>
            Errors.Add(message);
    }
}
