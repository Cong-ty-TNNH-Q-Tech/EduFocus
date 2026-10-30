using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace EduFocus.WPF.Services;

/// <summary>
/// Checks the GitHub Releases API for updates and applies them.
/// Supports pre-release builds (e.g. v0.2.0-beta).
/// </summary>
public class UpdaterService
{
    private const string Owner  = "Cong-ty-TNNH-Q-Tech";
    private const string Repo   = "EduFocus";
    // /releases (not /releases/latest) to include pre-releases
    private const string ApiUrl = $"https://api.github.com/repos/{Owner}/{Repo}/releases";

    private readonly HttpClient _http;
    private readonly AppExitState? _exitState;

    public UpdaterService(AppExitState? exitState = null)
    {
        _exitState = exitState;
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"EduFocus/{CurrentVersion}");
    }

    /// <summary>Current assembly version (e.g. "0.1.0" or "0.2.0-beta.1").</summary>
    public static string CurrentVersion
    {
        get
        {
            var assembly = Assembly.GetEntryAssembly() ?? typeof(UpdaterService).Assembly;
            var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info))
            {
                var plus = info.IndexOf('+');
                return (plus >= 0 ? info[..plus] : info).TrimStart('v');
            }

            var v = assembly.GetName().Version ?? new Version(0, 1, 0);
            return $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    /// <summary>
    /// Strips pre-release suffix from a version string so Version.TryParse works.
    /// "0.2.0-beta" → "0.2.0"
    /// </summary>
    private static string StripPreRelease(string tag, out string preRelease)
    {
        tag = tag.Trim().TrimStart('v');
        var plus = tag.IndexOf('+');
        if (plus >= 0)
            tag = tag[..plus];

        var dash = tag.IndexOf('-');
        if (dash >= 0)
        {
            preRelease = tag[(dash + 1)..];
            return tag[..dash];
        }

        preRelease = string.Empty;
        return tag;
    }

    public static bool IsNewerVersion(string candidateTag, string currentVersionText, bool candidateIsPreRelease = false)
    {
        var candidateNumericText = StripPreRelease(candidateTag, out var candidatePre);
        var currentNumericText = StripPreRelease(currentVersionText, out var currentPre);

        if (!Version.TryParse(candidateNumericText, out var candidate) ||
            !Version.TryParse(currentNumericText, out var current))
        {
            return false;
        }

        var candidateHasPre = candidateIsPreRelease || candidatePre.Length > 0;
        var currentHasPre = currentPre.Length > 0;

        // If current build is stable, ignore pre-release candidates unless explicitly on a pre-release channel
        if (!currentHasPre && candidateHasPre)
            return false;

        var cmp = candidate.CompareTo(current);
        if (cmp != 0)
            return cmp > 0;

        // Same numeric version: stable (no pre-release) is newer than pre-release (e.g. 1.0.0 > 1.0.0-rc.1)
        if (currentHasPre && !candidateHasPre)
            return true;

        if (currentHasPre && candidateHasPre)
            return string.Compare(candidatePre, currentPre, StringComparison.OrdinalIgnoreCase) > 0;

        return false;
    }

    /// <summary>
    /// Checks GitHub for the most recent release.
    /// Returns null if already up to date or the check fails.
    /// </summary>
    public async Task<ReleaseInfo?> CheckForUpdateAsync()
    {
        try
        {
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(15));
            var releases = await _http.GetFromJsonAsync<GitHubRelease[]>(ApiUrl, cts.Token);
            if (releases == null || releases.Length == 0) return null;

            var currentText = CurrentVersion;
            var release = releases.FirstOrDefault(r =>
                !r.Draft && IsNewerVersion(r.TagName, currentText, r.Prerelease));
            if (release == null) return null;

            var latestDisplay = release.TagName.TrimStart('v');

            // Prefer MSI, fall back to portable zip
            var asset = Array.Find(release.Assets,
                            a => a.Name.Contains("win-x64") && a.Name.EndsWith(".msi"))
                      ?? Array.Find(release.Assets,
                            a => a.Name.Contains("win-x64") && a.Name.EndsWith(".zip"));
            if (asset == null) return null;

            return new ReleaseInfo(latestDisplay, release.TagName, release.Body ?? string.Empty,
                                   asset.BrowserDownloadUrl, asset.Size);
        }
        catch
        {
            return null;  // graceful failure — network down, etc.
        }
    }

    /// <summary>
    /// Downloads the asset (MSI preferred, portable zip fallback), installs, and restarts.
    /// Progress is reported as 0→100 during download.
    /// </summary>
    public async Task DownloadAndApplyAsync(ReleaseInfo release,
                                            IProgress<int>? progress = null,
                                            Func<Task>? beforeRestartAsync = null,
                                            Action? beforeRestart = null)
    {
        var isMsi    = release.DownloadUrl.EndsWith(".msi", StringComparison.OrdinalIgnoreCase);
        var ext      = isMsi ? ".msi" : ".zip";
        var safeVersion = string.Concat(release.Version.Split(Path.GetInvalidFileNameChars()));
        var workDir  = Path.Combine(Path.GetTempPath(), $"EduFocus-update-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        var tempFile = Path.Combine(workDir, $"EduFocus-{safeVersion}{ext}");

        // ── Download ──────────────────────────────────────────────
        using var response = await _http.GetAsync(release.DownloadUrl,
                                                  HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var total = release.SizeBytes > 0 ? release.SizeBytes : (long?)null;
        await using (var downloadStream = await response.Content.ReadAsStreamAsync())
        await using (var fileStream     = File.Create(tempFile))
        {
            var buffer    = new byte[81920];
            long received = 0;
            int  read;
            while ((read = await downloadStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read));
                received += read;
                if (total.HasValue)
                    progress?.Report((int)(received * 100 / total.Value));
            }
        }
        progress?.Report(100);

        if (beforeRestartAsync != null)
            await beforeRestartAsync();
        beforeRestart?.Invoke();
        _exitState?.ApproveExit();

        if (isMsi)
        {
            // ── MSI path: msiexec handles install + shortcuts + uninstall ──
            // /qb = basic UI (progress bar); REBOOT=ReallySuppress prevents forced restart
            Process.Start(new ProcessStartInfo(
                "msiexec.exe",
                $"/i \"{tempFile}\" /qb REBOOT=ReallySuppress")
            {
                UseShellExecute = true   // required for UAC elevation prompt
            });
            System.Windows.Application.Current?.Shutdown();
        }
        else
        {
            // ── ZIP path: extract + cmd auto-restart script ───────────────
            var tempDir = Path.Combine(workDir, "extracted");
            var exeDir  = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
            ZipFile.ExtractToDirectory(tempFile, tempDir);

            var pid     = Environment.ProcessId;
            var exeName = Path.GetFileName(Environment.ProcessPath ?? "EduFocus.exe");
            var script  = Path.Combine(workDir, "edufocus_update.cmd");

            await File.WriteAllTextAsync(script,
                $"""
                @echo off
                timeout /t 2 /nobreak >nul
                taskkill /PID {pid} /F >nul 2>&1
                timeout /t 1 /nobreak >nul
                xcopy /E /Y /I "{tempDir}" "{exeDir}"
                start "" "{Path.Combine(exeDir, exeName)}"
                del "%~f0"
                """);

            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"")
            {
                CreateNoWindow  = true,
                UseShellExecute = false,
                WindowStyle     = ProcessWindowStyle.Hidden
            });
            System.Windows.Application.Current?.Shutdown();
        }
    }

    // ── GitHub API models ─────────────────────────────────────────
    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")]   string TagName,
        [property: JsonPropertyName("body")]       string? Body,
        [property: JsonPropertyName("draft")]      bool Draft,
        [property: JsonPropertyName("prerelease")] bool Prerelease,
        [property: JsonPropertyName("assets")]     GitHubAsset[] Assets);

    private sealed record GitHubAsset(
        [property: JsonPropertyName("name")]                  string Name,
        [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl,
        [property: JsonPropertyName("size")]                  long Size);
}

/// <summary>Information about an available update.</summary>
public record ReleaseInfo(
    string Version,
    string TagName,
    string ReleaseNotes,
    string DownloadUrl,
    long   SizeBytes);
