using System.Buffers.Binary;
using System.Text.Json;

namespace EduFocus.Infrastructure.ServiceControl;

/// <summary>
/// Length-prefixed JSON messages shared by the service pipe server and client.
/// One request per connection keeps every command independently authenticated
/// and avoids a shared connection state machine.
/// </summary>
public static class ServiceControlFraming
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task WriteAsync(Stream stream, byte[] payload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(payload);

        var header = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)payload.Length);

        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<byte[]?> ReadAsync(
        Stream stream,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var header = new byte[4];
        if (!await FillAsync(stream, header, cancellationToken).ConfigureAwait(false))
            return null;

        var length = BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (length == 0 || length > (uint)maxBytes)
            throw new InvalidDataException("Service control message length is invalid.");

        var payload = new byte[length];
        return await FillAsync(stream, payload, cancellationToken).ConfigureAwait(false) ? payload : null;
    }

    private static async Task<bool> FillAsync(
        Stream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken)
                .ConfigureAwait(false);
            if (count == 0)
                return false;

            read += count;
        }

        return true;
    }
}
