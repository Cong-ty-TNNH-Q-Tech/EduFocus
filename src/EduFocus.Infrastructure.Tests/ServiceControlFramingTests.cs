using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using EduFocus.Infrastructure.ServiceControl;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.ServiceControl;
using FluentAssertions;

namespace EduFocus.Infrastructure.Tests;

public sealed class ServiceControlFramingTests
{
    [Fact]
    public async Task Write_then_read_roundtrips_the_payload()
    {
        var payload = Encoding.UTF8.GetBytes("{\"command\":\"status\"}");
        var stream = new MemoryStream();

        await ServiceControlFraming.WriteAsync(stream, payload, CancellationToken.None);
        stream.Position = 0;

        var read = await ServiceControlFraming.ReadAsync(stream, 1024, CancellationToken.None);

        read.Should().Equal(payload);
        stream.Length.Should().Be(payload.Length + 4);
        BinaryPrimitives.ReadUInt32LittleEndian(stream.ToArray()).Should().Be((uint)payload.Length);
    }

    [Fact]
    public async Task Read_returns_null_when_the_stream_is_empty()
    {
        var read = await ServiceControlFraming.ReadAsync(
            new MemoryStream(),
            ServiceControlProtocol.MaxRequestBytes,
            CancellationToken.None);

        read.Should().BeNull();
    }

    [Fact]
    public async Task Read_returns_null_when_the_payload_is_truncated()
    {
        var payload = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var stream = new MemoryStream();
        await ServiceControlFraming.WriteAsync(stream, payload, CancellationToken.None);
        var truncated = new MemoryStream(stream.ToArray()[..^3]);

        var read = await ServiceControlFraming.ReadAsync(truncated, 1024, CancellationToken.None);

        read.Should().BeNull();
    }

    [Fact]
    public async Task Read_rejects_a_zero_length_message()
    {
        var stream = new MemoryStream(new byte[4]);

        var act = () => ServiceControlFraming.ReadAsync(stream, 1024, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task Read_rejects_a_length_above_the_limit()
    {
        var header = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(header, 17);

        var act = () => ServiceControlFraming.ReadAsync(
            new MemoryStream(header),
            16,
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task Read_accepts_a_payload_at_the_limit()
    {
        var payload = new byte[16];
        var stream = new MemoryStream();
        await ServiceControlFraming.WriteAsync(stream, payload, CancellationToken.None);
        stream.Position = 0;

        var read = await ServiceControlFraming.ReadAsync(stream, 16, CancellationToken.None);

        read.Should().Equal(payload);
    }

    [Fact]
    public void Protocol_identity_matches_the_installer_contract()
    {
        ServiceControlProtocol.PipeName.Should().Be("EduFocus.ServiceControl.v1");
        ServiceControlProtocol.Version.Should().Be(1);
        ServiceIdentity.WindowsServiceName.Should().Be("EduFocusProtection");
        ServiceIdentity.ServiceExecutableName.Should().Be("EduFocus.Service.exe");
        ServiceIdentity.UiExecutableName.Should().Be("EduFocus.exe");
        ServiceIdentity.InstallRegistryPath.Should().Be(@"SOFTWARE\EduFocus");
        ServiceIdentity.ServiceModeValueName.Should().Be("ServiceMode");
        ServiceIdentity.ServiceModeEnabled.Should().Be(1);
    }

    [Fact]
    public void Success_and_failure_helpers_always_carry_the_current_protocol()
    {
        var success = ServiceControlResponse.Success(
            service: new ProtectionServiceInfo("1.0.0", 1, DateTimeOffset.UnixEpoch, true, null),
            status: new ProtectionStatus(true, null, true));
        var failure = ServiceControlResponse.Failure(
            ServiceControlErrorCode.InternalError,
            "Lỗi nội bộ.");

        success.Protocol.Should().Be(ServiceControlProtocol.Version);
        success.Ok.Should().BeTrue();
        success.ErrorCode.Should().BeNull();
        success.Error.Should().BeNull();

        failure.Protocol.Should().Be(ServiceControlProtocol.Version);
        failure.Ok.Should().BeFalse();
        failure.ErrorCode.Should().Be(ServiceControlErrorCode.InternalError);
        failure.Service.Should().BeNull();
        failure.Status.Should().BeNull();
        failure.Operation.Should().BeNull();
    }

    [Fact]
    public void Responses_roundtrip_through_the_wire_serializer()
    {
        var original = ServiceControlResponse.Success(
            operation: new ProtectionOperationResult(
                new ProtectionStatus(true, new DateTime(2026, 10, 29, 8, 0, 0, DateTimeKind.Utc), true),
                Changed: true,
                Warnings: ["block page unavailable"]));

        var payload = JsonSerializer.SerializeToUtf8Bytes(original, ServiceControlFraming.JsonOptions);
        var roundtripped = JsonSerializer.Deserialize<ServiceControlResponse>(
            payload,
            ServiceControlFraming.JsonOptions);

        roundtripped.Should().NotBeNull();
        roundtripped!.Ok.Should().BeTrue();
        roundtripped.Operation!.Status.Should().Be(original.Operation!.Status);
        roundtripped.Operation.Changed.Should().BeTrue();
        roundtripped.Operation.Warnings.Should().Equal("block page unavailable");
    }
}
