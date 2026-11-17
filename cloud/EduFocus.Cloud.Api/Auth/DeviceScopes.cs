namespace EduFocus.Cloud.Api.Auth;

public static class DeviceScopes
{
    public const string Sync = "sync";
    public const string Read = "read";
    public const string Preferences = "prefs";
    public const string FamilyAdmin = "family:admin";

    public const string ClaimType = "scope";

    public const string Desktop = Sync + "," + FamilyAdmin;
    public const string Mobile = Read + "," + Preferences + "," + FamilyAdmin;
}
