using System.Runtime.Versioning;
using EduFocus.Infrastructure.Protection;
using FluentAssertions;
using Microsoft.Win32;

namespace EduFocus.Infrastructure.Tests;

[SupportedOSPlatform("windows")]
public sealed class BrowserDnsPolicyTests : IDisposable
{
    private readonly string _rootPath;
    private readonly string _chromePath;
    private readonly string _edgePath;
    private readonly BrowserDnsPolicyOptions _options;
    private readonly TestLogger _logger = new();

    public BrowserDnsPolicyTests()
    {
        _rootPath = @"SOFTWARE\EduFocusTests\BrowserDnsPolicy\" + Guid.NewGuid().ToString("N");
        _chromePath = _rootPath + @"\Policies\Chrome";
        _edgePath = _rootPath + @"\Policies\Edge";
        _options = new BrowserDnsPolicyOptions
        {
            Hive = RegistryHive.CurrentUser,
            BackupRootPath = _rootPath + @"\Backup",
            Targets =
            [
                new("Chrome", _chromePath),
                new("Edge", _edgePath)
            ]
        };
    }

    [Fact]
    public async Task Apply_sets_off_for_every_target_and_view()
    {
        WriteString(_chromePath, RegistryView.Registry64, "automatic");
        WriteString(_edgePath, RegistryView.Registry32, "secure");
        var policy = new BrowserDnsPolicy(_options, _logger);

        var warnings = await policy.ApplyAsync();

        warnings.Should().BeEmpty();
        ReadString(_chromePath, RegistryView.Registry64).Should().Be("off");
        ReadString(_chromePath, RegistryView.Registry32).Should().Be("off");
        ReadString(_edgePath, RegistryView.Registry64).Should().Be("off");
        ReadString(_edgePath, RegistryView.Registry32).Should().Be("off");
        _logger.Infos.Should().Contain(message => message.Contains("Secure DNS policy set"));
    }

    [Fact]
    public async Task Restore_puts_back_the_previous_values_and_removes_added_ones()
    {
        WriteString(_chromePath, RegistryView.Registry64, "automatic");
        WriteValue(_edgePath, RegistryView.Registry32, "DnsOverHttpsTemplates", @"https://dns.example/dns-query");
        var policy = new BrowserDnsPolicy(_options, _logger);
        await policy.ApplyAsync();

        var warnings = await policy.RestoreAsync();

        warnings.Should().BeEmpty();
        ReadString(_chromePath, RegistryView.Registry64).Should().Be("automatic");
        ReadString(_edgePath, RegistryView.Registry64).Should().BeNull();
        ReadString(_edgePath, RegistryView.Registry32).Should().BeNull();
        ReadValue(_edgePath, RegistryView.Registry32, "DnsOverHttpsTemplates")
            .Should().Be(@"https://dns.example/dns-query");
        BackupExists("Chrome", "64").Should().BeFalse();
        BackupExists("Edge", "32").Should().BeFalse();
    }

    [Fact]
    public async Task Apply_is_idempotent_and_keeps_the_first_backup()
    {
        WriteString(_chromePath, RegistryView.Registry64, "secure");
        var policy = new BrowserDnsPolicy(_options, _logger);

        await policy.ApplyAsync();
        await policy.ApplyAsync();
        await policy.RestoreAsync();

        ReadString(_chromePath, RegistryView.Registry64).Should().Be("secure");
    }

    [Fact]
    public async Task Restore_before_apply_is_a_no_op()
    {
        var policy = new BrowserDnsPolicy(_options, _logger);

        var warnings = await policy.RestoreAsync();

        warnings.Should().BeEmpty();
        KeyExists(_rootPath, RegistryView.Registry64).Should().BeFalse();
        KeyExists(_rootPath, RegistryView.Registry32).Should().BeFalse();
    }

    [Fact]
    public async Task Apply_reports_a_warning_but_still_covers_other_targets()
    {
        var options = _options with
        {
            Targets =
            [
                new("Chrome", _chromePath),
                new("Broken", new string('x', 300))
            ]
        };
        var policy = new BrowserDnsPolicy(options, _logger);

        var warnings = await policy.ApplyAsync();

        warnings.Should().ContainSingle().Which.Should().Be(BrowserDnsPolicy.ApplyWarning);
        ReadString(_chromePath, RegistryView.Registry64).Should().Be("off");
        _logger.Warnings.Should().Contain(message => message.Contains("Could not back up the Secure DNS policy"));

        await policy.RestoreAsync();
        ReadString(_chromePath, RegistryView.Registry64).Should().BeNull();
    }

    [Fact]
    public async Task Apply_leaves_a_policy_of_an_unexpected_type_untouched()
    {
        WriteValue(
            _chromePath,
            RegistryView.Registry64,
            BrowserDnsPolicyOptions.PolicyValueName,
            1,
            RegistryValueKind.DWord);
        var policy = new BrowserDnsPolicy(_options, _logger);

        var warnings = await policy.ApplyAsync();

        warnings.Should().ContainSingle().Which.Should().Be(BrowserDnsPolicy.ApplyWarning);
        ReadValue(_chromePath, RegistryView.Registry64, BrowserDnsPolicyOptions.PolicyValueName)
            .Should().Be(1);
        ReadString(_edgePath, RegistryView.Registry64).Should().Be("off");
    }

    [Fact]
    public void Invalid_targets_are_rejected()
    {
        var duplicateNames = _options with
        {
            Targets =
            [
                new("Chrome", _chromePath),
                new("chrome", _edgePath)
            ]
        };
        var emptyTargets = _options with { Targets = [] };
        var nullTarget = _options with { Targets = [null!] };

        var duplicate = () => new BrowserDnsPolicy(duplicateNames, _logger);
        var empty = () => new BrowserDnsPolicy(emptyTargets, _logger);
        var nullEntry = () => new BrowserDnsPolicy(nullTarget, _logger);

        duplicate.Should().Throw<ArgumentException>();
        empty.Should().Throw<ArgumentException>();
        nullEntry.Should().Throw<ArgumentException>();
    }

    public void Dispose()
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);
            root.DeleteSubKeyTree(_rootPath, throwOnMissingSubKey: false);
        }
    }

    private bool BackupExists(string target, string viewSuffix)
    {
        var view = viewSuffix == "64" ? RegistryView.Registry64 : RegistryView.Registry32;
        using var root = RegistryKey.OpenBaseKey(_options.Hive, view);
        using var key = root.OpenSubKey($@"{_options.BackupRootPath}\{target}-{viewSuffix}");
        return key is not null;
    }

    private static bool KeyExists(string path, RegistryView view)
    {
        using var root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);
        using var key = root.OpenSubKey(path);
        return key is not null;
    }

    private static string? ReadString(string keyPath, RegistryView view) =>
        ReadValue(keyPath, view, BrowserDnsPolicyOptions.PolicyValueName) as string;

    private static object? ReadValue(string keyPath, RegistryView view, string name)
    {
        using var root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);
        using var key = root.OpenSubKey(keyPath);
        return key?.GetValue(name);
    }

    private static void WriteString(string keyPath, RegistryView view, string value) =>
        WriteValue(
            keyPath,
            view,
            BrowserDnsPolicyOptions.PolicyValueName,
            value,
            RegistryValueKind.String);

    private static void WriteValue(
        string keyPath,
        RegistryView view,
        string name,
        object value,
        RegistryValueKind kind = RegistryValueKind.String)
    {
        using var root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);
        using var key = root.CreateSubKey(keyPath, writable: true)
            ?? throw new InvalidOperationException($"Cannot create test key '{keyPath}'.");
        key.SetValue(name, value, kind);
    }
}
