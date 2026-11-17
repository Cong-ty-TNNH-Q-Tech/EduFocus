using EduFocus.Domain.Entities;
using EduFocus.SqliteDataAccess.Persistence;
using EduFocus.SqliteDataAccess.Repositories;
using EduFocus.SqliteDataAccess.Tests.TestSupport;
using FluentAssertions;
using Microsoft.Data.Sqlite;

namespace EduFocus.SqliteDataAccess.Tests;

public sealed class CloudLinkRepositoryTests
{
    private static readonly DateTime Now = new(2026, 11, 17, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid SessionId = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
    private static readonly Guid FamilyId = Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7");
    private static readonly Guid DeviceId = Guid.Parse("3f2504e0-4f89-41d3-9a0c-0305e82c3301");
    private const string Token = "efd_desktop_token_must_never_leak";
    private const string CloudBaseUrl = "https://cloud.example.com";

    [Fact]
    public async Task Get_returns_null_before_the_link_is_created()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var repository = new SqliteCloudLinkRepository(db.ContextFactory);

        (await repository.GetAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Pairing_state_round_trips_through_a_new_repository_instance()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var repository = new SqliteCloudLinkRepository(db.ContextFactory);
        var link = CloudLink.CreateDefault(Now);
        link.RecordConsent(Now);
        link.BeginPairing(CloudBaseUrl, SessionId, "ABCD2345", Token, Now.AddMinutes(10), Now);
        await repository.AddAsync(link);

        var reopened = new SqliteCloudLinkRepository(
            new SqliteDbContextFactory(db.Options, db.KeyStore));
        var stored = await reopened.GetAsync();

        stored.Should().NotBeNull();
        stored!.IsEnabled.Should().BeTrue();
        stored.HasConsent.Should().BeTrue();
        stored.ConsentAcceptedAtUtc.Should().Be(Now);
        stored.CloudBaseUrl.Should().Be(CloudBaseUrl);
        stored.PairingSessionId.Should().Be(SessionId);
        stored.PairingCode.Should().Be("ABCD2345");
        stored.PairingExpiresAtUtc.Should().Be(Now.AddMinutes(10));
        stored.DesktopToken.Should().Be(Token);
    }

    [Fact]
    public async Task Linked_state_round_trips_through_a_new_repository_instance()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var repository = new SqliteCloudLinkRepository(db.ContextFactory);
        var link = CloudLink.CreateDefault(Now);
        link.RecordConsent(Now);
        link.BeginPairing(CloudBaseUrl, SessionId, "ABCD2345", Token, Now.AddMinutes(10), Now);
        await repository.AddAsync(link);

        link.CompletePairing(FamilyId, DeviceId, Now.AddSeconds(20), Now.AddSeconds(20));
        link.RecordSync(Now.AddSeconds(30), Now.AddSeconds(30));
        await repository.UpdateAsync(link);

        var reopened = new SqliteCloudLinkRepository(
            new SqliteDbContextFactory(db.Options, db.KeyStore));
        var stored = await reopened.GetAsync();

        stored.Should().NotBeNull();
        stored!.IsLinked.Should().BeTrue();
        stored.FamilyId.Should().Be(FamilyId);
        stored.DesktopDeviceId.Should().Be(DeviceId);
        stored.LinkedAtUtc.Should().Be(Now.AddSeconds(20));
        stored.LastSyncAtUtc.Should().Be(Now.AddSeconds(30));
        stored.DesktopToken.Should().Be(Token);
        stored.PairingCode.Should().BeNull();
    }

    [Fact]
    public async Task Unlink_clears_the_token_in_the_database_and_keeps_consent()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var repository = new SqliteCloudLinkRepository(db.ContextFactory);
        var link = CloudLink.CreateDefault(Now);
        link.RecordConsent(Now);
        link.BeginPairing(CloudBaseUrl, SessionId, "ABCD2345", Token, Now.AddMinutes(10), Now);
        link.CompletePairing(FamilyId, DeviceId, Now.AddSeconds(20), Now.AddSeconds(20));
        await repository.AddAsync(link);

        link.Unlink(Now.AddMinutes(1));
        await repository.UpdateAsync(link);

        var reopened = new SqliteCloudLinkRepository(
            new SqliteDbContextFactory(db.Options, db.KeyStore));
        var stored = await reopened.GetAsync();

        stored.Should().NotBeNull();
        stored!.IsEnabled.Should().BeFalse();
        stored.DesktopToken.Should().BeNull();
        stored.FamilyId.Should().BeNull();
        stored.DesktopDeviceId.Should().BeNull();
        stored.HasConsent.Should().BeTrue();
    }

    [Fact]
    public async Task Database_rejects_an_enabled_link_without_consent()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        using var connection = db.OpenConnection();

        var insert = () => TempDatabase.ExecuteNonQuery(
            connection,
            """
            INSERT INTO CloudLink (Id, IsEnabled, CreatedAtUtc, UpdatedAtUtc)
            VALUES ('4d1f5f8a-5c5c-4f3e-9d3a-0a2c5b7e8f10', 1,
                    '2026-11-17T09:00:00.0000000Z', '2026-11-17T09:00:00.0000000Z');
            """);

        insert.Should().Throw<SqliteException>();
    }

    [Fact]
    public async Task Token_scoped_status_updates_cannot_resurrect_an_unlinked_link()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var repository = new SqliteCloudLinkRepository(db.ContextFactory);
        var link = CloudLink.CreateDefault(Now);
        link.RecordConsent(Now);
        link.BeginPairing(CloudBaseUrl, SessionId, "ABCD2345", Token, Now.AddMinutes(10), Now);
        link.CompletePairing(FamilyId, DeviceId, Now, Now);
        await repository.AddAsync(link);

        (await repository.TryRecordSyncAsync("wrong-token", Now.AddSeconds(30), Now.AddSeconds(30)))
            .Should().BeFalse();
        (await repository.TryRecordSyncAsync(Token, Now.AddSeconds(30), Now.AddSeconds(30)))
            .Should().BeTrue();
        (await repository.TryRecordErrorAsync(Token, "network", Now.AddSeconds(40)))
            .Should().BeTrue();

        link.Unlink(Now.AddMinutes(1));
        await repository.UpdateAsync(link);

        (await repository.TryRecordSyncAsync(Token, Now.AddMinutes(2), Now.AddMinutes(2)))
            .Should().BeFalse();
        (await repository.TryRecordErrorAsync(Token, "network", Now.AddMinutes(2)))
            .Should().BeFalse();

        var stored = await repository.GetAsync();
        stored!.IsEnabled.Should().BeFalse();
        stored.DesktopToken.Should().BeNull();
        stored.LastSyncAtUtc.Should().BeNull();
        stored.LastError.Should().BeNull();
    }

    [Fact]
    public async Task Writes_keep_a_single_link_row()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var repository = new SqliteCloudLinkRepository(db.ContextFactory);
        var link = CloudLink.CreateDefault(Now);
        await repository.AddAsync(link);

        link.RecordConsent(Now.AddMinutes(1));
        await repository.UpdateAsync(link);

        link.BeginPairing(CloudBaseUrl, SessionId, "ABCD2345", Token, Now.AddMinutes(10), Now.AddMinutes(2));
        await repository.UpdateAsync(link);

        using var connection = db.OpenConnection();
        Convert.ToInt64(TempDatabase.Scalar(connection, "SELECT COUNT(*) FROM CloudLink;"))
            .Should().Be(1);
    }
}
