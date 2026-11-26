using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using EduFocus.SqliteDataAccess.Persistence;

namespace EduFocus.WPF.Services;

/// <summary>
/// Checks the GitHub Releases API for updates and applies verified MSI packages.
/// Supports pre-release builds (e.g. v0.2.0-beta).
/// </summary>
public class UpdaterService : IDisposable
{
    private const string Owner  = "Cong-ty-TNNH-Q-Tech";
    private const string Repo   = "EduFocus";
    // /releases (not /releases/latest) to include pre-releases
    private const string ApiUrl = $"https://api.github.com/repos/{Owner}/{Repo}/releases";
    private const long MaxDownloadBytes = 250L * 1024 * 1024;
    private const long MaxChecksumBytes = 1024L * 1024;
    private const int MaxRedirects = 5;

    private static readonly HashSet<string> AllowedDownloadHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "github.com",
        "api.github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com"
    };

    private readonly HttpClient _http;
    private readonly AppExitState? _exitState;
    private readonly string _stagingRootDirectory;

    public UpdaterService(
        AppExitState? exitState = null,
        string? stagingRootDirectory = null,
        HttpMessageHandler? messageHandler = null)
    {
        _exitState = exitState;
        _stagingRootDirectory = string.IsNullOrWhiteSpace(stagingRootDirectory)
            ? Path.Combine(EduFocusDatabaseOptions.DefaultDirectory, "updates")
            : Path.GetFullPath(stagingRootDirectory);

        var handler = messageHandler ?? new SocketsHttpHandler { AllowAutoRedirect = false };
        _http = new HttpClient(handler, disposeHandler: messageHandler is null)
        {
            Timeout = TimeSpan.FromMinutes(30)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"EduFocus/{CurrentVersion}");
    }

    public void Dispose() => _http.Dispose();

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

            // Require signed MSI installers rather than executing extracted scripts.
            var asset = Array.Find(release.Assets,
                a => a.Name.Contains("win-x64", StringComparison.OrdinalIgnoreCase)
                    && a.Name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase));
            if (asset == null) return null;

            var checksumAsset = Array.Find(release.Assets,
                a => a.Name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase));

            return new ReleaseInfo(
                latestDisplay,
                release.TagName,
                release.Body ?? string.Empty,
                asset.BrowserDownloadUrl,
                asset.Size,
                checksumAsset?.BrowserDownloadUrl);
        }
        catch
        {
            return null;  // graceful failure — network down, etc.
        }
    }

    /// <summary>
    /// Downloads the verified MSI package, closes protection cleanly, and launches msiexec.
    /// Progress is reported as 0→100 during download.
    /// </summary>
    public async Task DownloadAndApplyAsync(ReleaseInfo release,
                                            IProgress<int>? progress = null,
                                            Func<Task>? beforeRestartAsync = null,
                                            Action? beforeRestart = null)
    {
        ArgumentNullException.ThrowIfNull(release);

        var downloadUri = ValidateDownloadUri(release.DownloadUrl);
        var assetFileName = Path.GetFileName(downloadUri.LocalPath);
        if (!assetFileName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Chỉ hỗ trợ cài đặt tự động bằng gói MSI đã ký.");

        if (Environment.IsPrivilegedProcess)
            DataDirectorySecurity.EnsureSecure(_stagingRootDirectory);
        else
            Directory.CreateDirectory(_stagingRootDirectory);

        var safeVersion = string.Concat(release.Version.Split(Path.GetInvalidFileNameChars()));
        var workDir  = Path.Combine(_stagingRootDirectory, $"EduFocus-update-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        var tempFile = Path.Combine(workDir, $"EduFocus-{safeVersion}.msi");

        try
        {
            // ── Download with host/scheme validation and hard byte cap ──
            using (var response = await GetWithValidatedRedirectsAsync(downloadUri, CancellationToken.None))
            {
                response.EnsureSuccessStatusCode();

                if (response.Content.Headers.ContentLength is > MaxDownloadBytes)
                    throw new InvalidOperationException("Tệp cài đặt vượt quá giới hạn dung lượng cho phép.");

                var total = release.SizeBytes > 0 ? release.SizeBytes : response.Content.Headers.ContentLength;
                await using (var downloadStream = await response.Content.ReadAsStreamAsync())
                await using (var fileStream     = File.Create(tempFile))
                {
                    var buffer    = new byte[81920];
                    long received = 0;
                    int  read;
                    while ((read = await downloadStream.ReadAsync(buffer)) > 0)
                    {
                        received += read;
                        if (received > MaxDownloadBytes)
                            throw new InvalidOperationException("Tệp cài đặt vượt quá giới hạn dung lượng cho phép.");

                        await fileStream.WriteAsync(buffer.AsMemory(0, read));
                        if (total.HasValue && total.Value > 0)
                            progress?.Report((int)Math.Min(100, received * 100 / total.Value));
                    }
                }
            }
            progress?.Report(100);

            if (!string.IsNullOrWhiteSpace(release.Sha256SumsUrl))
                await VerifySha256FromManifestAsync(tempFile, assetFileName, release.Sha256SumsUrl, CancellationToken.None);

#if !DEBUG
            if (!VerifyAuthenticodeSignature(tempFile))
                throw new InvalidOperationException("Chữ ký số của gói cài đặt không hợp lệ hoặc không được tin cậy.");
#endif

            // Teardown runs and succeeds BEFORE launching msiexec so an aborted teardown never starts an install.
            await PrepareForRestartAsync(beforeRestartAsync, beforeRestart);

            var msiexecPath = Path.Combine(Environment.SystemDirectory, "msiexec.exe");
            var startInfo = new ProcessStartInfo(msiexecPath)
            {
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("/i");
            startInfo.ArgumentList.Add(tempFile);
            startInfo.ArgumentList.Add("/qb");
            startInfo.ArgumentList.Add("REBOOT=ReallySuppress");

            if (Process.Start(startInfo) is null)
                throw new InvalidOperationException("Không thể khởi chạy trình cài đặt MSI.");

            System.Windows.Application.Current?.Shutdown();
        }
        catch
        {
            TryDeleteDirectory(workDir);
            throw;
        }
    }

    internal static Uri ValidateDownloadUri(string url)
    {
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !AllowedDownloadHosts.Contains(uri.DnsSafeHost))
        {
            throw new InvalidOperationException("Nguồn tải bản cập nhật không hợp lệ hoặc không được tin cậy.");
        }

        return uri;
    }

    private async Task<HttpResponseMessage> GetWithValidatedRedirectsAsync(
        Uri initialUri,
        CancellationToken cancellationToken)
    {
        var currentUri = initialUri;
        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            var response = await _http.GetAsync(currentUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!IsRedirectStatus(response.StatusCode))
                return response;

            var location = response.Headers.Location;
            response.Dispose();
            if (location is null)
                throw new InvalidOperationException("Chuyển hướng tải bản cập nhật thiếu địa chỉ đích.");

            var nextUri = location.IsAbsoluteUri ? location : new Uri(currentUri, location);
            currentUri = ValidateDownloadUri(nextUri.ToString());
        }

        throw new InvalidOperationException("Quá nhiều chuyển hướng khi tải bản cập nhật.");
    }

    private static bool IsRedirectStatus(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Redirect
            or HttpStatusCode.RedirectMethod
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

    private async Task VerifySha256FromManifestAsync(
        string filePath,
        string assetFileName,
        string sha256SumsUrl,
        CancellationToken cancellationToken)
    {
        var sumsUri = ValidateDownloadUri(sha256SumsUrl);
        using var response = await GetWithValidatedRedirectsAsync(sumsUri, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaxChecksumBytes)
            throw new InvalidOperationException("Tệp kiểm tra SHA256SUMS.txt vượt quá giới hạn.");

        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        string? expectedHash = null;

        foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = rawLine.Trim().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
                continue;

            var name = Path.GetFileName(parts[1].Trim().TrimStart('*'));
            if (string.Equals(name, assetFileName, StringComparison.OrdinalIgnoreCase))
            {
                expectedHash = parts[0].Trim();
                break;
            }
        }

        if (string.IsNullOrEmpty(expectedHash) || expectedHash.Length != 64)
            throw new InvalidOperationException("Không tìm thấy mã kiểm tra SHA-256 hợp lệ cho gói cài đặt.");

        await using var stream = File.OpenRead(filePath);
        var actualBytes = await SHA256.HashDataAsync(stream, cancellationToken);
        var actualHex = Convert.ToHexString(actualBytes);

        if (!string.Equals(actualHex, expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Mã kiểm tra SHA-256 của gói cài đặt không khớp.");
    }

    private static bool VerifyAuthenticodeSignature(string filePath)
    {
        var actionGuid = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        var filePathPtr = Marshal.StringToCoTaskMemUni(filePath);
        var fileInfoPtr = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>());

        try
        {
            var fileInfo = new WinTrustFileInfo
            {
                cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                pcwszFilePath = filePathPtr
            };
            Marshal.StructureToPtr(fileInfo, fileInfoPtr, fDeleteOld: false);

            var data = new WinTrustData
            {
                cbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
                dwUIChoice = 2, // WTD_UI_NONE
                fdwRevocationChecks = 0, // WTD_REVOKE_NONE
                dwUnionChoice = 1, // WTD_CHOICE_FILE
                pFile = fileInfoPtr,
                dwStateAction = 0, // WTD_STATEACTION_IGNORE
                dwProvFlags = 0x10 // WTD_REVOCATION_CHECK_NONE
            };

            return WinVerifyTrust(IntPtr.Zero, ref actionGuid, ref data) == 0;
        }
        finally
        {
            Marshal.FreeCoTaskMem(fileInfoPtr);
            Marshal.FreeCoTaskMem(filePathPtr);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false)]
    private static extern int WinVerifyTrust(
        IntPtr hwnd,
        [MarshalAs(UnmanagedType.LPStruct)] ref Guid pgActionID,
        ref WinTrustData pWVTData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint cbStruct;
        public IntPtr pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    private async Task PrepareForRestartAsync(Func<Task>? beforeRestartAsync, Action? beforeRestart)
    {
        if (beforeRestartAsync != null)
            await beforeRestartAsync();
        beforeRestart?.Invoke();
        _exitState?.ApproveExit();
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
    long   SizeBytes,
    string? Sha256SumsUrl = null);
