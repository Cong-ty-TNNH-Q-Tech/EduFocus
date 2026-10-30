using System.Globalization;
using System.Runtime.Versioning;
using EduFocus.Ports.Outbound.Services;
using Microsoft.Win32;

namespace EduFocus.Infrastructure.Protection;

/// <summary>
/// Writes <c>DnsOverHttpsMode=off</c> to the Chrome/Edge machine policies while
/// protection is ON. Secure DNS is what lets a browser bypass the Windows hosts
/// file, so this turns the hosts block back into the effective layer.
/// The previous policy value (or its absence) is stored under
/// <c>HKLM\SOFTWARE\EduFocus\BrowserDnsPolicy</c> before the policy changes, so
/// restore is exact even after a crash between the two steps. Browsers read the
/// policy at startup and when it changes; a running browser may keep DoH until
/// it is restarted. Failure is a warning, never a reason to leave protection
/// OFF, because hosts and the other adapters still apply.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class BrowserDnsPolicy : IBrowserDnsPolicyPort
{
    public const string ApplyWarning =
        "Không thể tắt Secure DNS của Chrome/Edge; trình duyệt có thể bỏ qua hosts. " +
        "Hãy chạy EduFocus với quyền quản trị và khởi động lại trình duyệt.";

    public const string RestoreWarning =
        "Không thể khôi phục policy Secure DNS trước đó của trình duyệt; " +
        "policy sẽ được thử lại ở lần khởi động sau.";

    private const string BackupExistedValue = "Existed";
    private const string BackupKindValue = "Kind";
    private const string BackupDataValue = "Value";

    private static readonly RegistryView[] Views =
    [
        RegistryView.Registry64,
        RegistryView.Registry32
    ];

    private readonly BrowserDnsPolicyOptions _options;
    private readonly IAppLogger _logger;
    private readonly object _gate = new();

    public BrowserDnsPolicy(BrowserDnsPolicyOptions options, IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (string.IsNullOrWhiteSpace(options.BackupRootPath))
            throw new ArgumentException("A backup registry path is required.", nameof(options));
        if (options.Targets is null || options.Targets.Count == 0)
            throw new ArgumentException("At least one browser policy target is required.", nameof(options));

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in options.Targets)
        {
            if (target is null)
                throw new ArgumentException("Browser policy targets cannot be null.", nameof(options));
            if (string.IsNullOrWhiteSpace(target.Name) || target.Name.Contains('\\', StringComparison.Ordinal))
                throw new ArgumentException("Browser policy target names must be simple identifiers.", nameof(options));
            if (string.IsNullOrWhiteSpace(target.PolicyKeyPath))
                throw new ArgumentException("Browser policy target keys are required.", nameof(options));
            if (!names.Add(target.Name))
                throw new ArgumentException($"Duplicate browser policy target '{target.Name}'.", nameof(options));
        }

        _options = options;
    }

    public Task<IReadOnlyList<string>> ApplyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var failed = false;
            var pending = new List<(BrowserDnsPolicyTarget Target, RegistryView View)>();

            // Back up every view before changing any policy. Some Windows
            // builds expose the same physical key through both views; writing
            // first would make the second backup capture our own "off" value.
            foreach (var target in _options.Targets)
            {
                foreach (var view in Views)
                {
                    try
                    {
                        using var root = RegistryKey.OpenBaseKey(_options.Hive, view);
                        EnsureBackup(root, target, view);
                        pending.Add((target, view));
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        failed = true;
                        LogFailure("back up", target, view, ex);
                    }
                }
            }

            var applied = 0;
            foreach (var (target, view) in pending)
            {
                try
                {
                    using var root = RegistryKey.OpenBaseKey(_options.Hive, view);
                    WriteDisabledPolicy(root, target);
                    applied++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failed = true;
                    LogFailure("disable", target, view, ex);
                }
            }

            if (applied > 0)
            {
                _logger.Info(
                    LogCategories.Protection,
                    $"Browser Secure DNS policy set to '{BrowserDnsPolicyOptions.DisabledValue}' " +
                    $"for {applied} policy target(s).");
            }

            return Task.FromResult<IReadOnlyList<string>>(failed ? [ApplyWarning] : []);
        }
    }

    public Task<IReadOnlyList<string>> RestoreAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var restored = 0;
            var failed = false;

            foreach (var target in _options.Targets)
            {
                foreach (var view in Views)
                {
                    try
                    {
                        if (RestoreCore(target, view))
                            restored++;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        failed = true;
                        LogFailure("restore", target, view, ex);
                    }
                }
            }

            if (restored > 0)
            {
                _logger.Info(
                    LogCategories.Protection,
                    $"Browser Secure DNS policy restored for {restored} policy target(s).");
            }

            return Task.FromResult<IReadOnlyList<string>>(failed ? [RestoreWarning] : []);
        }
    }

    private static void WriteDisabledPolicy(RegistryKey root, BrowserDnsPolicyTarget target)
    {
        using var policy = root.CreateSubKey(target.PolicyKeyPath, writable: true)
            ?? throw new InvalidOperationException(
                $"Cannot create the browser policy key '{target.PolicyKeyPath}'.");
        policy.SetValue(
            BrowserDnsPolicyOptions.PolicyValueName,
            BrowserDnsPolicyOptions.DisabledValue,
            RegistryValueKind.String);
    }

    private void LogFailure(
        string action,
        BrowserDnsPolicyTarget target,
        RegistryView view,
        Exception exception) =>
        _logger.Warning(
            LogCategories.Protection,
            $"Could not {action} the Secure DNS policy for {target.Name} ({view}): " +
            $"{exception.GetType().Name}.");

    private void EnsureBackup(
        RegistryKey root,
        BrowserDnsPolicyTarget target,
        RegistryView view)
    {
        var backupPath = GetBackupPath(target, view);
        using (var existing = root.OpenSubKey(backupPath, writable: false))
        {
            // The original value is already recorded. Re-applying must not
            // overwrite that backup with our own "off" value.
            if (existing is not null)
                return;
        }

        var exists = false;
        var kind = RegistryValueKind.String;
        string? previous = null;

        using (var policy = root.OpenSubKey(target.PolicyKeyPath, writable: false))
        {
            if (policy is not null)
            {
                var value = policy.GetValue(
                    BrowserDnsPolicyOptions.PolicyValueName,
                    defaultValue: null,
                    RegistryValueOptions.DoNotExpandEnvironmentNames);

                if (value is not null)
                {
                    kind = policy.GetValueKind(BrowserDnsPolicyOptions.PolicyValueName);
                    if (kind is not (RegistryValueKind.String or RegistryValueKind.ExpandString))
                    {
                        // A value we cannot reproduce exactly is left untouched
                        // so restore never destroys an administrator's policy.
                        throw new InvalidOperationException(
                            $"The existing Secure DNS policy for {target.Name} has type {kind}.");
                    }

                    previous = (string)value;
                    exists = true;
                }
            }
        }

        using var backup = root.CreateSubKey(backupPath, writable: true)
            ?? throw new InvalidOperationException($"Cannot create the policy backup '{backupPath}'.");
        backup.SetValue(BackupExistedValue, exists ? 1 : 0, RegistryValueKind.DWord);

        if (!exists)
            return;

        backup.SetValue(BackupKindValue, (int)kind, RegistryValueKind.DWord);
        backup.SetValue(BackupDataValue, previous!, RegistryValueKind.String);
    }

    private bool RestoreCore(BrowserDnsPolicyTarget target, RegistryView view)
    {
        using var root = RegistryKey.OpenBaseKey(_options.Hive, view);
        var backupPath = GetBackupPath(target, view);

        bool existed;
        RegistryValueKind kind = RegistryValueKind.String;
        string? previous = null;

        using (var backup = root.OpenSubKey(backupPath, writable: false))
        {
            if (backup is null)
                return false;

            existed = Convert.ToInt32(
                backup.GetValue(BackupExistedValue, 0),
                CultureInfo.InvariantCulture) == 1;

            if (existed)
            {
                kind = (RegistryValueKind)Convert.ToInt32(
                    backup.GetValue(BackupKindValue, (int)RegistryValueKind.String),
                    CultureInfo.InvariantCulture);
                previous = backup.GetValue(BackupDataValue) as string
                    ?? throw new InvalidOperationException(
                        "The browser Secure DNS policy backup has no value.");
            }
        }

        if (existed)
        {
            using var policy = root.CreateSubKey(target.PolicyKeyPath, writable: true)
                ?? throw new InvalidOperationException(
                    $"Cannot create the browser policy key '{target.PolicyKeyPath}'.");
            policy.SetValue(BrowserDnsPolicyOptions.PolicyValueName, previous!, kind);
        }
        else
        {
            using var policy = root.OpenSubKey(target.PolicyKeyPath, writable: true);
            policy?.DeleteValue(BrowserDnsPolicyOptions.PolicyValueName, throwOnMissingValue: false);
        }

        root.DeleteSubKeyTree(backupPath, throwOnMissingSubKey: false);
        return true;
    }

    private string GetBackupPath(BrowserDnsPolicyTarget target, RegistryView view) =>
        $@"{_options.BackupRootPath}\{target.Name}-{ViewSuffix(view)}";

    private static string ViewSuffix(RegistryView view) =>
        view == RegistryView.Registry64 ? "64" : "32";
}
