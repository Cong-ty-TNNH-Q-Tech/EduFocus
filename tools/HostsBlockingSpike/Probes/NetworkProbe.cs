using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace HostsBlockingSpike.Probes;

public sealed record FlushDnsResult(bool Success, int ExitCode, long DurationMs, string Output);

public sealed record DnsProbeResult(
    string Host,
    bool Resolved,
    IReadOnlyList<string> Addresses,
    long DurationMs);

public sealed record HttpProbeResult(
    string Url,
    string Mode,
    bool Success,
    int? StatusCode,
    bool BlockPageMarker,
    long DurationMs,
    string? ErrorType,
    string? ErrorMessage,
    string BodySnippet);

public sealed record ProxyProbeResult(string EffectiveProxy, bool UsesProxy);

public static class NetworkProbe
{
    private const string BlockPageMarker = "EDUFOCUS_BLOCK_PAGE";

    public static FlushDnsResult FlushDns()
    {
        var stopwatch = Stopwatch.StartNew();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("ipconfig", "/flushdns")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        if (!process.Start())
            return new FlushDnsResult(false, -1, stopwatch.ElapsedMilliseconds, "Could not start ipconfig.");

        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit(15000);
        stopwatch.Stop();
        return new FlushDnsResult(
            process.HasExited && process.ExitCode == 0,
            process.HasExited ? process.ExitCode : -1,
            stopwatch.ElapsedMilliseconds,
            output.Trim());
    }

    public static async Task<DnsProbeResult> ResolveAsync(string host, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
            stopwatch.Stop();
            return new DnsProbeResult(
                host,
                addresses.Length > 0,
                addresses.Select(address => address.ToString()).Distinct().Order().ToList(),
                stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException)
        {
            stopwatch.Stop();
            return new DnsProbeResult(host, false, [$"error: {ex.Message}"], stopwatch.ElapsedMilliseconds);
        }
    }

    public static Task<HttpProbeResult> HttpGetAsync(string url, CancellationToken ct) =>
        SendAsync(url, "GET-direct", ct);

    public static Task<HttpProbeResult> HttpsGetAsync(string url, CancellationToken ct) =>
        SendAsync(url, "GET-https", ct);

    public static ProxyProbeResult DetectProxy()
    {
        var testUri = new Uri("http://example.com/");
        try
        {
            var proxy = WebRequest.GetSystemWebProxy();
            var effective = proxy.GetProxy(testUri);
            var usesProxy = effective is not null && effective != testUri;
            return new ProxyProbeResult(effective?.ToString() ?? "(none)", usesProxy);
        }
        catch (Exception ex) when (ex is NotSupportedException or UriFormatException)
        {
            return new ProxyProbeResult($"error: {ex.Message}", false);
        }
    }

    private static async Task<HttpProbeResult> SendAsync(string url, string mode, CancellationToken ct)
    {
        using var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(5)
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            stopwatch.Stop();
            return new HttpProbeResult(
                url,
                mode,
                true,
                (int)response.StatusCode,
                body.Contains(BlockPageMarker, StringComparison.Ordinal),
                stopwatch.ElapsedMilliseconds,
                null,
                null,
                Snippet(body));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            stopwatch.Stop();
            return new HttpProbeResult(
                url,
                mode,
                false,
                null,
                false,
                stopwatch.ElapsedMilliseconds,
                ex.GetType().Name,
                ex.Message,
                string.Empty);
        }
    }

    private static string Snippet(string body)
    {
        var normalized = body.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= 300 ? normalized : normalized[..300] + "...";
    }
}
