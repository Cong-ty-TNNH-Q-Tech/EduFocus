using EduFocus.Domain.Entities;
using FluentAssertions;

namespace EduFocus.Domain.Tests.Domain;

public sealed class CloudLinkTests
{
    private static readonly DateTime Now = new(2026, 11, 17, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid SessionId = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
    private static readonly Guid FamilyId = Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7");
    private static readonly Guid DeviceId = Guid.Parse("3f2504e0-4f89-41d3-9a0c-0305e82c3301");
    private const string Token = "efd_desktop_token_must_never_leak";

    [Fact]
    public void CreateDefault_is_disabled_without_consent_or_tokens()
    {
        var link = CloudLink.CreateDefault(Now);

        link.IsEnabled.Should().BeFalse();
        link.HasConsent.Should().BeFalse();
        link.IsLinked.Should().BeFalse();
        link.DesktopToken.Should().BeNull();
        link.CloudBaseUrl.Should().BeNull();
        link.ConsentAcceptedAtUtc.Should().BeNull();
        link.CreatedAtUtc.Should().Be(Now);
        link.UpdatedAtUtc.Should().Be(Now);
    }

    [Fact]
    public void BeginPairing_requires_consent()
    {
        var link = CloudLink.CreateDefault(Now);

        var act = () => link.BeginPairing(
            "https://cloud.example.com",
            SessionId,
            "ABCD2345",
            Token,
            Now.AddMinutes(10),
            Now);

        act.Should().Throw<InvalidOperationException>();
        link.DesktopToken.Should().BeNull();
    }

    [Fact]
    public void BeginPairing_stores_the_token_and_blocks_a_second_active_session()
    {
        var link = Consented();

        link.BeginPairing("https://cloud.example.com/", SessionId, "abcd2345", Token, Now.AddMinutes(10), Now);

        link.IsEnabled.Should().BeTrue();
        link.CloudBaseUrl.Should().Be("https://cloud.example.com");
        link.DesktopToken.Should().Be(Token);
        link.PairingCode.Should().Be("ABCD2345");
        link.PairingSessionId.Should().Be(SessionId);
        link.HasActivePairing(Now.AddMinutes(5)).Should().BeTrue();

        var act = () => link.BeginPairing(
            "https://cloud.example.com",
            Guid.NewGuid(),
            "WXYZ2345",
            "efd_other",
            Now.AddMinutes(10),
            Now.AddMinutes(1));

        act.Should().Throw<InvalidOperationException>();
        link.DesktopToken.Should().Be(Token);
    }

    [Fact]
    public void BeginPairing_accepts_a_new_session_after_the_previous_one_expires()
    {
        var link = Consented();
        link.BeginPairing("https://cloud.example.com", SessionId, "ABCD2345", Token, Now.AddMinutes(10), Now);

        var later = Now.AddMinutes(11);
        var newSessionId = Guid.NewGuid();
        link.BeginPairing("https://cloud.example.com", newSessionId, "WXYZ2345", "efd_new", later.AddMinutes(10), later);

        link.PairingSessionId.Should().Be(newSessionId);
        link.DesktopToken.Should().Be("efd_new");
    }

    [Fact]
    public void CompletePairing_drops_the_pairing_code_and_marks_the_link()
    {
        var link = Consented();
        link.BeginPairing("https://cloud.example.com", SessionId, "ABCD2345", Token, Now.AddMinutes(10), Now);

        link.CompletePairing(FamilyId, DeviceId, Now.AddSeconds(20), Now.AddSeconds(20));

        link.IsLinked.Should().BeTrue();
        link.FamilyId.Should().Be(FamilyId);
        link.DesktopDeviceId.Should().Be(DeviceId);
        link.LinkedAtUtc.Should().Be(Now.AddSeconds(20));
        link.PairingSessionId.Should().BeNull();
        link.PairingCode.Should().BeNull();
        link.PairingExpiresAtUtc.Should().BeNull();
    }

    [Fact]
    public void Unlink_clears_every_identifier_but_keeps_consent()
    {
        var link = Linked();

        link.Unlink(Now.AddMinutes(1));

        link.IsEnabled.Should().BeFalse();
        link.IsLinked.Should().BeFalse();
        link.DesktopToken.Should().BeNull();
        link.FamilyId.Should().BeNull();
        link.DesktopDeviceId.Should().BeNull();
        link.LinkedAtUtc.Should().BeNull();
        link.LastSyncAtUtc.Should().BeNull();
        link.HasConsent.Should().BeTrue();
        link.ConsentAcceptedAtUtc.Should().Be(Now);
    }

    [Fact]
    public void RevokeConsent_requires_a_disabled_link_and_clears_consent()
    {
        var link = Linked();

        var whileEnabled = () => link.RevokeConsent(Now.AddMinutes(1));
        whileEnabled.Should().Throw<InvalidOperationException>();

        link.Unlink(Now.AddMinutes(1));
        link.RevokeConsent(Now.AddMinutes(2));

        link.HasConsent.Should().BeFalse();
        link.ConsentAcceptedAtUtc.Should().BeNull();
    }

    [Fact]
    public void RecordError_limits_length_and_strips_control_characters()
    {
        var link = Consented();
        var noisy = "lỗi\nmạng\t" + new string('x', 800);

        link.RecordError(noisy, Now.AddMinutes(1));

        link.LastError.Should().NotBeNull();
        link.LastError!.Length.Should().BeLessThanOrEqualTo(CloudLink.MaxErrorLength);
        link.LastError.Should().NotContain("\n");
        link.LastError.Should().NotContain("\t");
    }

    [Fact]
    public void Timestamps_cannot_move_backwards()
    {
        var link = Consented();

        var act = () => link.RecordConsent(Now.AddMinutes(-1));

        act.Should().Throw<ArgumentException>();
    }

    private static CloudLink Consented()
    {
        var link = CloudLink.CreateDefault(Now);
        link.RecordConsent(Now);
        return link;
    }

    private static CloudLink Linked()
    {
        var link = Consented();
        link.BeginPairing("https://cloud.example.com", SessionId, "ABCD2345", Token, Now.AddMinutes(10), Now);
        link.CompletePairing(FamilyId, DeviceId, Now.AddSeconds(20), Now.AddSeconds(20));
        link.RecordSync(Now.AddSeconds(30), Now.AddSeconds(30));
        return link;
    }
}
