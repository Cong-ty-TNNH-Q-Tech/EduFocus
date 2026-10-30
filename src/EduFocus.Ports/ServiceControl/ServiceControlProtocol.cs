namespace EduFocus.Ports.ServiceControl;

/// <summary>
/// Wire contract between the WPF control surface and the protection service
/// (issue #46). The pipe exists only on the local machine, accepts one request
/// per connection and carries small JSON messages with a 4-byte little-endian
/// length prefix.
/// </summary>
public static class ServiceControlProtocol
{
    public const string PipeName = "EduFocus.ServiceControl.v1";
    public const int Version = 1;

    public const int MaxRequestBytes = 16 * 1024;
    public const int MaxResponseBytes = 64 * 1024;

    public static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(15);
}

/// <summary>
/// Commands are fixed strings rather than a numeric enum so an unknown value
/// from an older or newer client is rejected explicitly.
/// </summary>
public static class ServiceControlCommand
{
    public const string Ping = "ping";
    public const string Status = "status";
    public const string Enable = "enable";
    public const string Disable = "disable";
    public const string DisableForExit = "disable-for-exit";
    public const string Reconcile = "reconcile";
    public const string EnableForSchedule = "enable-for-schedule";
    public const string DisableForSchedule = "disable-for-schedule";
    public const string ReloadRules = "reload-rules";
}

public static class ServiceControlErrorCode
{
    public const string UnsupportedProtocol = "unsupported_protocol";
    public const string UnknownCommand = "unknown_command";
    public const string Unauthorized = "unauthorized";
    public const string EngineError = "engine_error";
    public const string SettingsMissing = "settings_missing";
    public const string InternalError = "internal_error";
    public const string Timeout = "timeout";
    public const string Unavailable = "unavailable";
    public const string Rejected = "rejected";
}

/// <summary>
/// Names shared by the installer, the service host and the UI that decide
/// whether protection runs in the service or inside the app process.
/// </summary>
public static class ServiceIdentity
{
    public const string WindowsServiceName = "EduFocusProtection";
    public const string DisplayName = "EduFocus Protection Service";
    public const string Description =
        "Keeps EduFocus protection active while the EduFocus window is closed.";

    public const string ServiceExecutableName = "EduFocus.Service.exe";
    public const string UiExecutableName = "EduFocus.exe";

    public const string InstallRegistryPath = @"SOFTWARE\EduFocus";
    public const string ServiceModeValueName = "ServiceMode";
    public const int ServiceModeEnabled = 1;
}
