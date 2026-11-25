using EduFocus.Cloud.Api.Push;

namespace EduFocus.Cloud.Tests;

public sealed class PushEndpointValidatorTests
{
    private const string Endpoint = "https://push.example.com/sub/device-1";
    private const string P256dh = "BAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string Auth = "AAAAAAAAAAAAAAAAAAAAAA";
    private static readonly string[] AllowedHosts = ["push.example.com"];

    [Fact]
    public void Accepts_valid_registration_for_allowed_host()
    {
        var valid = PushEndpointValidator.TryValidateRegistration(
            Endpoint,
            P256dh,
            Auth,
            AllowedHosts,
            enforceAllowedHosts: true,
            out var endpoint,
            out var p256dh,
            out var auth);

        Assert.True(valid);
        Assert.Equal(Endpoint, endpoint);
        Assert.Equal(P256dh, p256dh);
        Assert.Equal(Auth, auth);
    }

    [Theory]
    [InlineData("http://push.example.com/sub/device-1")]
    [InlineData("https://user:pass@push.example.com/sub/device-1")]
    [InlineData("https:///sub/device-1")]
    [InlineData("not-a-url")]
    [InlineData("https://push.example.com/sub\nInjected: value")]
    public void Rejects_insecure_or_malformed_endpoint(string endpoint)
    {
        Assert.False(TryValidate(endpoint, P256dh, Auth));
    }

    [Fact]
    public void Rejects_endpoint_longer_than_limit()
    {
        var endpoint = "https://push.example.com/" + new string('a', PushEndpointValidator.MaxEndpointLength);

        Assert.False(TryValidate(endpoint, P256dh, Auth));
    }

    [Fact]
    public void Rejects_host_outside_allowlist()
    {
        Assert.False(PushEndpointValidator.TryValidateRegistration(
            "https://attacker.example/sub/device-1",
            P256dh,
            Auth,
            AllowedHosts,
            enforceAllowedHosts: true,
            out _,
            out _,
            out _));
    }

    [Fact]
    public void Allowlist_is_skipped_when_push_is_disabled()
    {
        Assert.True(TryValidate("https://attacker.example/sub/device-1", P256dh, Auth));
    }

    [Fact]
    public void Rejects_malformed_p256dh()
    {
        var wrongLength = Convert.ToBase64String(new byte[64])
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        var wrongPrefix = "A" + P256dh[1..];

        Assert.False(TryValidate(Endpoint, wrongLength, Auth));
        Assert.False(TryValidate(Endpoint, wrongPrefix, Auth));
    }

    [Fact]
    public void Rejects_malformed_auth_secret()
    {
        var wrongLength = Convert.ToBase64String(new byte[15])
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        Assert.False(TryValidate(Endpoint, P256dh, wrongLength));
        Assert.False(TryValidate(Endpoint, P256dh, "not base64!"));
    }

    [Theory]
    [InlineData("push.example.com", true)]
    [InlineData("push.example.com:8443", true)]
    [InlineData("localhost:8080", true)]
    [InlineData("", false)]
    [InlineData("https://push.example.com", false)]
    [InlineData("push.example.com/path", false)]
    [InlineData("push.example.com?x=1", false)]
    [InlineData("push.example.com#fragment", false)]
    [InlineData("push.example.com:notaport", false)]
    public void Validates_allowed_host_entries(string value, bool expected)
    {
        Assert.Equal(expected, PushEndpointValidator.IsValidAllowedHost(value));
    }

    private static bool TryValidate(string endpoint, string p256dh, string auth) =>
        PushEndpointValidator.TryValidateRegistration(
            endpoint,
            p256dh,
            auth,
            AllowedHosts,
            enforceAllowedHosts: false,
            out _,
            out _,
            out _);
}
