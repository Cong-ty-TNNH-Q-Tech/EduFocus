using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Principal;
using HostsBlockingSpike.BlockPage;
using HostsBlockingSpike.Hosts;
using HostsBlockingSpike.Probes;
using HostsBlockingSpike.Reporting;

namespace HostsBlockingSpike;

public static class SpikeRunner
{
    public static async Task<int> RunAsync(CliOptions options)
    {
        var report = new SpikeReport
        {
            CommandLine = string.Join(' ', Environment.GetCommandLineArgs()),
            ApplyHostsRequested = options.ApplyHosts,
            IsElevated = IsElevated(),
            HostsPath = options.HostsPath,
            BackupDir = options.BackupDir
        };

        var exitCode = 0;
        var outputDir = Path.GetFullPath(options.OutputDir);
        Directory.CreateDirectory(outputDir);

        Log(report, $"OS: {report.OsDescription}; runtime: {report.FrameworkDescription}; elevated={report.IsElevated}");
        report.Proxy = NetworkProbe.DetectProxy();
        Log(report, $"System proxy: {report.Proxy.EffectiveProxy}; usesProxy={report.Proxy.UsesProxy}");

        report.Browsers.AddRange(BrowserProbe.Discover());
        Log(report, report.Browsers.Count == 0
            ? "Browsers: none found in standard locations."
            : "Browsers: " + string.Join("; ", report.Browsers.Select(browser => $"{browser.Name} {browser.Version ?? "?"}")));

        if (!options.SkipSelfTests)
        {
            var workRoot = Path.Combine(Path.GetTempPath(), "edufocus-hosts-spike");
            Directory.CreateDirectory(workRoot);
            var tests = HostsSelfTests.RunAll(workRoot);
            report.SelfTests.AddRange(tests);
            foreach (var test in tests)
                Log(report, $"Self-test '{test.Name}': {(test.Passed ? "PASS" : "FAIL")} - {test.Detail}");
            if (tests.Any(test => !test.Passed))
                exitCode = 1;
        }

        await RunLoopbackBlockPageChecksAsync(report);

        if (!options.ApplyHosts)
        {
            report.Note = "Chỉ chạy self-test sandbox và block page loopback. Dùng --apply-hosts (elevated) để kiểm chứng hosts thật và trình duyệt.";
            AddStaticLimitations(report);
            return Finish(report, outputDir, exitCode);
        }

        if (!report.IsElevated)
        {
            report.Note = "--apply-hosts yêu cầu quyền administrator; phần hosts thật và trình duyệt bị bỏ qua.";
            Log(report, report.Note);
            AddStaticLimitations(report);
            return Finish(report, outputDir, 2);
        }

        await RunRealHostsChecksAsync(report, options, outputDir);

        AddStaticLimitations(report);
        return Finish(report, outputDir, report.FatalError is null ? exitCode : 1);
    }

    private static async Task RunLoopbackBlockPageChecksAsync(SpikeReport report)
    {
        var port = GetFreePort();
        using var server = new BlockPageServer(["example.com", "www.example.com"], port);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            server.Start();
            stopwatch.Stop();
            Log(report, $"Loopback block page listening on 127.0.0.1:{port} (start {stopwatch.ElapsedMilliseconds} ms)");
        }
        catch (SocketException ex)
        {
            Log(report, $"Could not start loopback block page server: {ex.Message}");
            return;
        }

        using var client = CreateDirectClient();

        await CheckAsync("blocked host", "example.com", "/");
        await CheckAsync("blocked host with port", "example.com:1234", "/");
        await CheckAsync("blocked host trailing dot", "example.com.", "/");
        await CheckAsync("blocked host with path/query", "example.com", "/some/path?q=1");
        await CheckAsync("www variant", "www.example.com", "/");
        await CheckAsync("unblocked host", "not-blocked.test", "/");

        var escapedPage = BlockPageServer.BuildBlockPage("<script>alert(1)</script>");
        var escapingWorks = escapedPage.Contains("&lt;script&gt;", StringComparison.Ordinal) &&
                            !escapedPage.Contains("<script>", StringComparison.Ordinal);
        report.BlockPageChecks.Add(new BlockPageCheck(
            "HTML escape",
            "<script>alert(1)</script>",
            "-",
            0,
            escapingWorks,
            !HasExternalResourceReference(escapedPage),
            0));

        server.Stop();
        Log(report, $"Loopback block page checks: {report.BlockPageChecks.Count(check => check.Name != "HTML escape")} HTTP checks + escape check.");

        async Task CheckAsync(string name, string hostHeader, string path)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}{path}");
                request.Headers.Host = hostHeader;
                var requestStopwatch = Stopwatch.StartNew();
                using var response = await client.SendAsync(request).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                requestStopwatch.Stop();
                report.BlockPageChecks.Add(new BlockPageCheck(
                    name,
                    hostHeader,
                    path,
                    (int)response.StatusCode,
                    body.Contains("EDUFOCUS_BLOCK_PAGE", StringComparison.Ordinal),
                    !HasExternalResourceReference(body),
                    requestStopwatch.ElapsedMilliseconds));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                report.BlockPageChecks.Add(new BlockPageCheck(name, hostHeader, path, 0, false, false, 0));
                Log(report, $"Loopback check '{name}' failed: {ex.Message}");
            }
        }
    }

    private static async Task RunRealHostsChecksAsync(SpikeReport report, CliOptions options, string outputDir)
    {
        var manager = new HostsFileManager(options.HostsPath, options.BackupDir);
        var domains = HostsFileManager.NormalizeDomains(options.Domains);
        var primaryDomain = domains[0];

        Console.CancelKeyPress += (_, _) => TryRestoreSilently(manager);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => TryRestoreSilently(manager);

        try
        {
            report.Recovery = manager.RecoverIfNeeded();
            Log(report, $"Pre-run recovery: performed={report.Recovery.Performed}, fromJournal={report.Recovery.FromJournal}, detail={report.Recovery.Detail}");
            report.HostsSha256Before = Sha256File(options.HostsPath);

            report.HostsApply = manager.Apply(domains);
            Log(report,
                $"Hosts applied: domains={domains.Count}, blockLines={report.HostsApply.BlockLineCount}, " +
                $"changed={report.HostsApply.Changed}, backup={report.HostsApply.BackupPath}");

            report.DnsFlush = NetworkProbe.FlushDns();
            Log(report, $"DNS flush after apply: success={report.DnsFlush.Success}, exit={report.DnsFlush.ExitCode}, {report.DnsFlush.DurationMs} ms");

            foreach (var domain in domains)
            {
                var dns = await NetworkProbe.ResolveAsync(domain, CancellationToken.None).ConfigureAwait(false);
                report.DnsResults.Add(dns);
                Log(report, $"DNS {domain}: resolved={dns.Resolved}, addresses={string.Join(", ", dns.Addresses)}");
            }

            await TestPortConflictAsync(report, domains, options.BlockPagePort).ConfigureAwait(false);

            using var server = new BlockPageServer(domains, options.BlockPagePort);
            try
            {
                server.Start();
                Log(report, $"Block page server listening on 127.0.0.1:{options.BlockPagePort}");
            }
            catch (SocketException ex)
            {
                report.FatalError = $"Không bind được block page server cổng {options.BlockPagePort}: {ex.Message}";
                Log(report, report.FatalError);
            }

            if (server.IsRunning)
            {
                var directHttp = await NetworkProbe.HttpGetAsync($"http://{primaryDomain}/", CancellationToken.None).ConfigureAwait(false);
                report.HttpResults.Add(directHttp);
                Log(report, $"HTTP {directHttp.Url}: status={directHttp.StatusCode}, blockPage={directHttp.BlockPageMarker}, {directHttp.DurationMs} ms");

                var pathHttp = await NetworkProbe.HttpGetAsync($"http://{primaryDomain}/some/path?q=1", CancellationToken.None).ConfigureAwait(false);
                report.HttpResults.Add(pathHttp);
                Log(report, $"HTTP path probe: status={pathHttp.StatusCode}, blockPage={pathHttp.BlockPageMarker} (path không được enforce riêng)");

                var https = await NetworkProbe.HttpsGetAsync($"https://{primaryDomain}/", CancellationToken.None).ConfigureAwait(false);
                report.HttpResults.Add(https);
                Log(report, $"HTTPS probe: success={https.Success}, error={https.ErrorType ?? "(none)"}, blockPage={https.BlockPageMarker}");
            }

            if (server.IsRunning && !options.SkipBrowsers)
                await RunBrowserMatrixAsync(report, options, outputDir, server, primaryDomain).ConfigureAwait(false);
            else if (options.SkipBrowsers)
                Log(report, "Browser probes skipped by flag.");

            report.ServerRequests.AddRange(server.Requests);
            server.Stop();
        }
        catch (Exception ex)
        {
            report.FatalError = $"{ex.GetType().Name}: {ex.Message}";
            Log(report, "Real hosts test failed: " + report.FatalError);
        }
        finally
        {
            try
            {
                report.HostsRestore = manager.Restore();
                report.HostsSha256After = Sha256File(options.HostsPath);
                var flush = NetworkProbe.FlushDns();
                Log(report,
                    $"Hosts restored: blockRemoved={report.HostsRestore.BlockRemoved}, exact={report.HostsRestore.RestoredExact}, " +
                    $"sha={ShortHash(report.HostsSha256After)}; DNS flush success={flush.Success}");

                var afterRestore = await NetworkProbe.HttpGetAsync($"http://{primaryDomain}/", CancellationToken.None).ConfigureAwait(false);
                report.HttpResults.Add(afterRestore with { Mode = "GET-after-restore" });
                Log(report, $"HTTP after restore: success={afterRestore.Success}, status={afterRestore.StatusCode}, blockPage={afterRestore.BlockPageMarker}");
            }
            catch (Exception ex)
            {
                report.FatalError = (report.FatalError is null ? string.Empty : report.FatalError + "; ") +
                                    $"Restore failed: {ex.GetType().Name}: {ex.Message}";
                Log(report, "RESTORE FAILED: " + ex);
            }
        }
    }

    private static Task TestPortConflictAsync(SpikeReport report, IReadOnlyList<string> domains, int port)
    {
        var stopwatch = Stopwatch.StartNew();
        var dummy = new TcpListener(IPAddress.Loopback, port);
        var occupied = false;

        try
        {
            dummy.Start();
            occupied = true;

            using var probe = new BlockPageServer(domains, port);
            try
            {
                probe.Start();
                probe.Stop();
                report.PortConflict = new PortConflictResult(
                    false,
                    string.Empty,
                    "Server started while the port was occupied (no conflict detected).",
                    true,
                    stopwatch.ElapsedMilliseconds);
                Log(report, $"Port {port}: unexpected success while occupied.");
            }
            catch (SocketException ex)
            {
                report.PortConflict = new PortConflictResult(
                    true,
                    ex.GetType().Name,
                    ex.Message,
                    false,
                    stopwatch.ElapsedMilliseconds);
                Log(report, $"Port {port} conflict detected: {ex.Message}");
            }
        }
        catch (SocketException ex)
        {
            report.PortConflict = new PortConflictResult(
                false,
                ex.GetType().Name,
                "Không chiếm được cổng để kiểm tra: " + ex.Message,
                false,
                stopwatch.ElapsedMilliseconds);
            Log(report, $"Could not occupy port {port} for the conflict test: {ex.Message}");
        }
        finally
        {
            if (occupied)
                dummy.Stop();
        }

        if (report.PortConflict is { ConflictDetected: true })
        {
            using var server = new BlockPageServer(domains, port);
            try
            {
                server.Start();
                server.Stop();
                report.PortConflict = report.PortConflict with
                {
                    SucceededAfterRelease = true,
                    DurationMs = stopwatch.ElapsedMilliseconds
                };
                Log(report, $"Port {port} usable after release.");
            }
            catch (SocketException ex)
            {
                Log(report, $"Port {port} still unusable after release: {ex.Message}");
            }
        }

        return Task.CompletedTask;
    }

    private static async Task RunBrowserMatrixAsync(
        SpikeReport report,
        CliOptions options,
        string outputDir,
        BlockPageServer server,
        string primaryDomain)
    {
        if (report.Browsers.Count == 0)
        {
            Log(report, "No Chrome/Edge/Firefox installation found; browser matrix skipped.");
            return;
        }

        foreach (var browser in report.Browsers)
        {
            foreach (var (mode, url) in new[]
                     {
                         ("http", $"http://{primaryDomain}/"),
                         ("https", $"https://{primaryDomain}/")
                     })
            {
                var requestsBefore = server.Requests.Count;
                var timeoutSeconds = browser.Name == "Firefox" && mode == "https"
                    ? Math.Min(options.BrowserTimeoutSeconds, 20)
                    : options.BrowserTimeoutSeconds;
                try
                {
                    var run = await BrowserProbe
                        .RunAsync(browser, url, mode, outputDir, timeoutSeconds, CancellationToken.None)
                        .ConfigureAwait(false);

                    await Task.Delay(TimeSpan.FromSeconds(2.5), CancellationToken.None).ConfigureAwait(false);
                    var matchingRequest = server.Requests
                        .Skip(requestsBefore)
                        .FirstOrDefault(request => UserAgentMatches(browser.Name, request.UserAgent));

                    run = run with
                    {
                        BlockPageServerHit = matchingRequest is not null,
                        ServerHitUserAgent = matchingRequest?.UserAgent
                    };
                    report.BrowserRuns.Add(run);
                    Log(report,
                        $"{browser.Name} {mode}: exit={run.ExitCode}, timeout={run.TimedOut}, " +
                        $"domMarker={run.BlockPageInDom?.ToString() ?? "n/a"}, serverHit={run.BlockPageServerHit}, " +
                        $"matchedUa={run.ServerHitUserAgent ?? "(none)"}, {run.DurationMs} ms");
                }
                catch (Exception ex)
                {
                    report.BrowserRuns.Add(new BrowserRunResult(
                        browser.Name,
                        url,
                        mode,
                        false,
                        false,
                        null,
                        0,
                        null,
                        false,
                        null,
                        null,
                        string.Empty,
                        $"{ex.GetType().Name}: {ex.Message}"));
                    Log(report, $"Browser probe failed for {browser.Name} {mode}: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
    }

    private static bool UserAgentMatches(string browserName, string? userAgent)
    {
        if (string.IsNullOrEmpty(userAgent))
            return false;

        return browserName switch
        {
            "Chrome" => userAgent.Contains("Chrome/", StringComparison.Ordinal) &&
                        !userAgent.Contains("Edg/", StringComparison.Ordinal),
            "Edge" => userAgent.Contains("Edg/", StringComparison.Ordinal),
            "Firefox" => userAgent.Contains("Firefox/", StringComparison.Ordinal),
            _ => false
        };
    }

    private static void AddStaticLimitations(SpikeReport report)
    {
        report.Limitations.Add("Hosts chỉ chặn domain exact; không phân biệt URL/path, nên block page áp dụng cho mọi path của domain.");
        report.Limitations.Add("Block page chỉ phục vụ HTTP; truy cập HTTPS thường báo lỗi kết nối/SSL thay vì hiển thị trang chặn.");
        report.Limitations.Add("Hosts không tạo log truy cập HTTPS; server chỉ ghi nhận request HTTP thực tế đến cổng block page.");
        report.Limitations.Add("DoH/DoT, VPN, proxy hoặc browser extension có thể bypass hosts.");
        if (report.Proxy?.UsesProxy == true)
            report.Limitations.Add($"Máy có proxy hệ thống {report.Proxy.EffectiveProxy}; proxy có thể resolve DNS phía server và bypass hosts.");
        report.Limitations.Add("Học sinh có quyền admin có thể sửa hosts hoặc dừng tiến trình; self-heal/journal chỉ giảm thiểu, không chống admin.");
        if (report.Browsers.Count == 0)
            report.Limitations.Add("Không tìm thấy Chrome/Edge/Firefox trong đường dẫn chuẩn; chưa kiểm chứng được hành vi trình duyệt.");
    }

    private static int Finish(SpikeReport report, string outputDir, int exitCode)
    {
        report.FinishedAt = DateTimeOffset.UtcNow;
        var paths = ReportWriter.Write(report, outputDir);
        Console.WriteLine();
        Console.WriteLine($"Report: {paths.MarkdownPath}");
        Console.WriteLine($"JSON:   {paths.JsonPath}");
        Console.WriteLine($"Log:    {paths.LogPath}");
        return exitCode;
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static HttpClient CreateDirectClient() => new(new SocketsHttpHandler
    {
        UseProxy = false,
        AllowAutoRedirect = false
    })
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    private static bool HasExternalResourceReference(string html) =>
        html.Contains("http://", StringComparison.OrdinalIgnoreCase) ||
        html.Contains("https://", StringComparison.OrdinalIgnoreCase);

    private static string Sha256File(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static string ShortHash(string hash) =>
        string.IsNullOrEmpty(hash) ? "-" : (hash.Length <= 16 ? hash : hash[..16] + "...");

    private static void TryRestoreSilently(HostsFileManager manager)
    {
        try { manager.Restore(); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void Log(SpikeReport report, string message)
    {
        var line = $"[{DateTimeOffset.Now:HH:mm:ss}] {message}";
        report.Log.Add(line);
        Console.WriteLine(line);
    }
}
