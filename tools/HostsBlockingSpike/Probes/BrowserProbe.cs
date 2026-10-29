using System.Diagnostics;
using System.Text;

namespace HostsBlockingSpike.Probes;

public sealed record BrowserInfo(string Name, string ExecutablePath, string? Version);

public sealed record BrowserRunResult(
    string Browser,
    string Url,
    string Mode,
    bool ProcessStarted,
    bool TimedOut,
    int? ExitCode,
    long DurationMs,
    bool? BlockPageInDom,
    bool BlockPageServerHit,
    string? ServerHitUserAgent,
    string? ScreenshotPath,
    string StandardOutputSnippet,
    string StandardErrorSnippet);

public static class BrowserProbe
{
    private const string BlockPageMarker = "EDUFOCUS_BLOCK_PAGE";

    public static IReadOnlyList<BrowserInfo> Discover()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var candidates = new List<(string Name, string Path)>
        {
            ("Chrome", Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe")),
            ("Chrome", Path.Combine(programFilesX86, "Google", "Chrome", "Application", "chrome.exe")),
            ("Chrome", Path.Combine(localAppData, "Google", "Chrome", "Application", "chrome.exe")),
            ("Edge", Path.Combine(programFilesX86, "Microsoft", "Edge", "Application", "msedge.exe")),
            ("Edge", Path.Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe")),
            ("Firefox", Path.Combine(programFiles, "Mozilla Firefox", "firefox.exe")),
            ("Firefox", Path.Combine(programFilesX86, "Mozilla Firefox", "firefox.exe"))
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var browsers = new List<BrowserInfo>();
        foreach (var (name, path) in candidates)
        {
            if (!File.Exists(path) || !seen.Add(path))
                continue;

            browsers.Add(new BrowserInfo(name, path, ReadVersion(path)));
        }

        return browsers;
    }

    public static async Task<BrowserRunResult> RunAsync(
        BrowserInfo browser,
        string url,
        string mode,
        string screenshotDir,
        int timeoutSeconds,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var isFirefox = string.Equals(browser.Name, "Firefox", StringComparison.OrdinalIgnoreCase);
        var profileDir = Path.Combine(Path.GetTempPath(), "edufocus-hosts-spike", $"profile-{Guid.NewGuid():N}");
        Directory.CreateDirectory(profileDir);

        string? screenshotPath = null;
        var startInfo = new ProcessStartInfo
        {
            FileName = browser.ExecutablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (isFirefox)
        {
            Directory.CreateDirectory(screenshotDir);
            screenshotPath = Path.Combine(screenshotDir, $"firefox-{mode}-{Guid.NewGuid():N}.png");
            startInfo.ArgumentList.Add("-headless");
            startInfo.ArgumentList.Add("-no-remote");
            startInfo.ArgumentList.Add("-profile");
            startInfo.ArgumentList.Add(profileDir);
            startInfo.ArgumentList.Add("--screenshot");
            startInfo.ArgumentList.Add(screenshotPath);
            startInfo.ArgumentList.Add("--window-size=1280,720");
            startInfo.ArgumentList.Add(url);
        }
        else
        {
            startInfo.ArgumentList.Add("--headless=new");
            startInfo.ArgumentList.Add("--no-sandbox");
            startInfo.ArgumentList.Add("--disable-gpu");
            startInfo.ArgumentList.Add("--no-first-run");
            startInfo.ArgumentList.Add("--no-default-browser-check");
            startInfo.ArgumentList.Add("--disable-extensions");
            startInfo.ArgumentList.Add("--disable-sync");
            startInfo.ArgumentList.Add("--metrics-recording-only");
            startInfo.ArgumentList.Add("--user-data-dir=" + profileDir);
            startInfo.ArgumentList.Add("--virtual-time-budget=8000");
            startInfo.ArgumentList.Add("--dump-dom");
            startInfo.ArgumentList.Add(url);
        }

        try
        {
            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                return new BrowserRunResult(
                    browser.Name, url, mode, false, false, null,
                    stopwatch.ElapsedMilliseconds, null, false, null, null, string.Empty, "Process did not start.");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            var timedOut = false;

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                timedOut = true;
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }

                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }

            var stdout = await stdoutTask.WaitAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
            var stderr = await stderrTask.WaitAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);

            // firefox.exe trả về ngay trong khi tiến trình nền vẫn tải trang và ghi ảnh;
            // chờ file xuất hiện để request của lần chạy này không bị quy nhầm sang lần sau.
            if (isFirefox && !timedOut && screenshotPath is not null)
                await WaitForScreenshotAsync(screenshotPath, TimeSpan.FromSeconds(Math.Min(timeoutSeconds, 15)), ct)
                    .ConfigureAwait(false);

            stopwatch.Stop();
            var screenshotExists = screenshotPath is not null && File.Exists(screenshotPath);

            return new BrowserRunResult(
                browser.Name,
                url,
                mode,
                true,
                timedOut,
                process.HasExited ? process.ExitCode : null,
                stopwatch.ElapsedMilliseconds,
                isFirefox ? null : stdout.Contains(BlockPageMarker, StringComparison.Ordinal),
                false,
                null,
                screenshotExists ? screenshotPath : null,
                Snippet(stdout),
                Snippet(stderr));
        }
        finally
        {
            try
            {
                if (Directory.Exists(profileDir))
                    Directory.Delete(profileDir, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task WaitForScreenshotAsync(string path, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path) && new FileInfo(path).Length > 0)
                return;
            await Task.Delay(200, ct).ConfigureAwait(false);
        }
    }

    private static string? ReadVersion(string executablePath)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(executablePath);
            var version = info.ProductVersion ?? info.FileVersion;
            return string.IsNullOrWhiteSpace(version) ? null : version.Trim();
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    private static string Snippet(string value)
    {
        var normalized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= 600 ? normalized : normalized[..600] + "...";
    }
}
