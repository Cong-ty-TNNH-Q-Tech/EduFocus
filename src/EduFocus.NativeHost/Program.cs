using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.NativeHost;

/// <summary>
/// Chrome/Edge native messaging relay. Chrome runs this process with the
/// extension origin as its only argument and speaks 4-byte little-endian
/// length-prefixed JSON over stdin/stdout. The relay forwards frames verbatim
/// to the EduFocus named pipe and keeps retrying while the pipe is absent, so
/// a browser that is already open picks up protection within seconds. When the
/// pipe is unavailable it sends <c>{"type":"unavailable"}</c> so the extension
/// fails open (issue #45).
/// </summary>
internal static class Program
{
    private const string ExpectedExtensionOrigin =
        "chrome-extension://" + NativeMessagingRegistration.ExtensionId + "/";

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    private static readonly byte[] UnavailableMessage = Encoding.UTF8.GetBytes(
        """{"type":"unavailable"}""");

    private static async Task<int> Main(string[] args)
    {
        NativeHostOptions options;
        try
        {
            options = NativeHostOptions.Parse(args, AppContext.BaseDirectory);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }

        if (options.Mode != NativeHostMode.Relay)
            return RunRegistration(options);

        if (!IsAllowedOrigin(args))
            return 2;

        using var stdin = Console.OpenStandardInput();
        using var stdout = Console.OpenStandardOutput();
        using var cts = new CancellationTokenSource();
        using var relay = new Relay(stdin, stdout);

        var browserTask = relay.ReadBrowserAsync(cts.Token);
        var eduFocusTask = relay.PumpEduFocusAsync(cts.Token);

        await Task.WhenAny(browserTask, eduFocusTask).ConfigureAwait(false);
        await cts.CancelAsync().ConfigureAwait(false);

        await IgnoreAsync(browserTask).ConfigureAwait(false);
        await IgnoreAsync(eduFocusTask).ConfigureAwait(false);
        return 0;
    }

    private static int RunRegistration(NativeHostOptions options)
    {
        try
        {
            var hostPath = Environment.ProcessPath
                ?? Path.Combine(AppContext.BaseDirectory, "EduFocus.NativeHost.exe");

            if (options.Mode == NativeHostMode.Register)
            {
                NativeMessagingRegistration.Register(
                    hostPath,
                    options.ManifestDirectory,
                    options.MachineScope);
            }
            else
            {
                NativeMessagingRegistration.Unregister(
                    options.ManifestDirectory,
                    options.MachineScope);
            }

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"EduFocus native messaging registration failed: {exception.Message}");
            return 1;
        }
    }

    private static bool IsAllowedOrigin(string[] args)
    {
        // Chrome passes the calling origin; Edge uses the same scheme. Manual
        // runs without an origin are allowed for diagnostics.
        if (args.Length == 0)
            return true;

        var origin = args[0];
        if (!origin.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase))
            return true;

        return origin.TrimEnd('/').Equals(
            ExpectedExtensionOrigin.TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> FillAsync(
        Stream source,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await source
                .ReadAsync(buffer.AsMemory(read), cancellationToken)
                .ConfigureAwait(false);
            if (count == 0)
                return false;

            read += count;
        }

        return true;
    }

    private static async Task IgnoreAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private static async Task<byte[]?> ReadFrameAsync(
        Stream source,
        int maxFrameBytes,
        CancellationToken cancellationToken)
    {
        var header = new byte[4];
        if (!await FillAsync(source, header, cancellationToken).ConfigureAwait(false))
            return null;

        var length = BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (length == 0 || length > (uint)maxFrameBytes)
            throw new InvalidDataException("Native message exceeds the allowed frame size.");

        var payload = new byte[length];
        return await FillAsync(source, payload, cancellationToken).ConfigureAwait(false)
            ? payload
            : null;
    }

    private static async Task WriteFrameAsync(
        Stream destination,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        var header = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)payload.Length);

        await destination.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await destination.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed class Relay(Stream inbox, Stream outbox) : IDisposable
    {
        private readonly Stream _inbox = inbox;
        private readonly Stream _outbox = outbox;
        private NamedPipeClientStream? _pipe;
        private bool _unavailableSent;

        public async Task ReadBrowserAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var payload = await ReadFrameAsync(
                        _inbox,
                        BrowserBridgeProtocol.MaxIncomingMessageBytes,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (payload is null)
                    return;

                var pipe = Volatile.Read(ref _pipe);
                if (pipe is null)
                    continue;

                try
                {
                    await WriteFrameAsync(pipe, payload, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception)
                {
                    // The pump task notices the broken pipe and reconnects.
                }
            }
        }

        public async Task PumpEduFocusAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var pipe = new NamedPipeClientStream(
                    ".",
                    BrowserBridgeProtocol.PipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous);

                if (!await TryConnectAsync(pipe, cancellationToken).ConfigureAwait(false))
                {
                    pipe.Dispose();
                    await SendUnavailableAsync(cancellationToken).ConfigureAwait(false);
                    await DelayAsync(cancellationToken).ConfigureAwait(false);
                    continue;
                }

                Volatile.Write(ref _pipe, pipe);
                _unavailableSent = false;
                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var payload = await ReadFrameAsync(
                                pipe,
                                BrowserBridgeProtocol.MaxMessageBytes,
                                cancellationToken)
                            .ConfigureAwait(false);
                        if (payload is null)
                            break;

                        await WriteFrameAsync(_outbox, payload, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception)
                {
                }
                finally
                {
                    Volatile.Write(ref _pipe, null);
                    pipe.Dispose();
                }

                if (cancellationToken.IsCancellationRequested)
                    return;

                await SendUnavailableAsync(cancellationToken).ConfigureAwait(false);
                await DelayAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            var pipe = Interlocked.Exchange(ref _pipe, null);
            pipe?.Dispose();
        }

        private async Task SendUnavailableAsync(CancellationToken cancellationToken)
        {
            if (_unavailableSent)
                return;

            _unavailableSent = true;
            try
            {
                await WriteFrameAsync(_outbox, UnavailableMessage, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception)
            {
                // The browser side is gone; Main will observe the stdin close.
            }
        }

        private static Task DelayAsync(CancellationToken cancellationToken) =>
            Task.Delay(RetryDelay, cancellationToken);

        private static async Task<bool> TryConnectAsync(
            NamedPipeClientStream pipe,
            CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(400));

            try
            {
                await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}
