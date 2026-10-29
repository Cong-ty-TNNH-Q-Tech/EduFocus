using EduFocus.SqliteDataAccess.Tests.TestSupport;
using FluentAssertions;
using Microsoft.Data.Sqlite;

namespace EduFocus.SqliteDataAccess.Tests;

public sealed class SchemaTests
{
    private static readonly string[] ExpectedTables =
    [
        "__EFMigrationsHistory",
        "__EFMigrationsLock",
        "ActivitySamples",
        "AppSettings",
        "AttentionEvents",
        "AuditLogs",
        "BlockedAttempts",
        "BlockRules",
        "ParentCredentials",
        "ScheduleWindows",
        "StudySessions"
    ];

    private static readonly string[] ExpectedIndexes =
    [
        "IX_ActivitySamples_Category",
        "IX_ActivitySamples_Domain",
        "IX_ActivitySamples_SessionId_SampledAtUtc",
        "IX_AttentionEvents_SessionId_StartedAtUtc",
        "IX_AttentionEvents_State",
        "IX_AuditLogs_EventType",
        "IX_AuditLogs_OccurredAtUtc",
        "IX_BlockedAttempts_Domain",
        "IX_BlockedAttempts_OccurredAtUtc",
        "IX_BlockedAttempts_SessionId",
        "IX_BlockRules_Category",
        "IX_BlockRules_Effect_IsEnabled",
        "IX_BlockRules_ExpiresAtUtc",
        "IX_ScheduleWindows_IsEnabled",
        "IX_StudySessions_LastHeartbeatAtUtc",
        "IX_StudySessions_StartLocalDate",
        "IX_StudySessions_StartedAtUtc",
        "IX_StudySessions_Status",
        "UX_BlockRules_RuleType_MatchType_NormalizedValue_Effect",
        "UX_StudySessions_Active"
    ];

    [Fact]
    public async Task Migration_creates_all_tables_and_indexes()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        using var connection = db.OpenConnection();

        TempDatabase.GetTableNames(connection).Should().BeEquivalentTo(ExpectedTables);
        TempDatabase.GetIndexNames(connection).Should().BeEquivalentTo(ExpectedIndexes);
    }

    [Fact]
    public async Task Migrations_are_recorded_once_in_history()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        using var connection = db.OpenConnection();

        var migrations = TempDatabase.QueryStrings(
            connection,
            "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId;");

        migrations.Should().HaveCount(4)
            .And.Contain(migration => migration.EndsWith("_InitialEduFocusSchema"))
            .And.Contain(migration => migration.EndsWith("_AddOnboardingConsent"))
            .And.Contain(migration => migration.EndsWith("_AddProtectionSource"))
            .And.Contain(migration => migration.EndsWith("_AddUpdateCheckOptIn"));
    }

    [Fact]
    public async Task Active_session_index_is_unique_and_partial()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        using var connection = db.OpenConnection();

        var sql = TempDatabase.QueryStrings(
            connection,
            "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = 'UX_StudySessions_Active';")
            .Should().ContainSingle().Subject;

        sql.Should().Contain("UNIQUE");
        sql.Should().Contain("WHERE");
        sql.Should().Contain("Active");
    }

    [Fact]
    public async Task Column_types_follow_storage_conventions()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        using var connection = db.OpenConnection();
        var columns = ReadColumns(connection, "StudySessions");
        var createSql = TempDatabase.QueryStrings(
            connection,
            "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'StudySessions';").Single();

        columns["Id"].Type.Should().Be("TEXT(36)");
        columns["StartedAtUtc"].Type.Should().Be("TEXT(28)");
        columns["StartLocalDate"].Type.Should().Be("TEXT(10)");
        columns["Status"].Type.Should().Be("TEXT(16)");
        columns["Status"].MaxLength.Should().Be(16);
        columns["MonitoredSeconds"].IsGenerated.Should().BeTrue(because: createSql);
        columns["MonitoredSeconds"].IsStored.Should().BeTrue(because: createSql);
        columns["AttentionPercent"].IsGenerated.Should().BeTrue(because: createSql);
        columns["CoveragePercent"].IsGenerated.Should().BeTrue(because: createSql);
    }

    [Fact]
    public async Task Defaults_match_schema_document()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        using var connection = db.OpenConnection();

        var settings = ReadColumns(connection, "AppSettings");
        settings["ProtectionEnabled"].DefaultValue.Should().Be("0");
        settings["ProtectionSource"].Type.Should().Be("TEXT(16)");
        settings["ProtectionSource"].NotNull.Should().BeTrue();
        settings["ProtectionSource"].DefaultValue.Should().Be("'None'");
        settings["MonitoringEnabled"].DefaultValue.Should().Be("1");
        settings["Theme"].DefaultValue.Should().Be("'dark'");
        settings["Language"].DefaultValue.Should().Be("'vi'");
        settings["SampleIntervalSeconds"].DefaultValue.Should().Be("15");
        settings["UpdateCheckEnabled"].DefaultValue.Should().Be("0");
        settings["UpdateCheckEnabled"].NotNull.Should().BeTrue();
        settings["PrivacyAcceptedAtUtc"].Type.Should().Be("TEXT(28)");
        settings["PrivacyAcceptedAtUtc"].NotNull.Should().BeFalse();
        settings["GuardianAcknowledgedAtUtc"].Type.Should().Be("TEXT(28)");
        settings["GuardianAcknowledgedAtUtc"].NotNull.Should().BeFalse();

        var rules = ReadColumns(connection, "BlockRules");
        rules["IsEnabled"].DefaultValue.Should().Be("1");
        rules["EnforcementStatus"].DefaultValue.Should().Be("'Enforced'");
    }

    [Fact]
    public async Task Check_constraint_rejects_invalid_schedule_window()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        using var connection = db.OpenConnectionWithPragmas();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ScheduleWindows
                (Id, DaysMask, StartMinutes, EndMinutes, Label, IsEnabled, CreatedAtUtc, UpdatedAtUtc)
            VALUES
                ($id, 0, 480, 690, NULL, 1, '2026-10-29T00:00:00.000Z', '2026-10-29T00:00:00.000Z');
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());

        var exception = Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());

        exception.Message.Should().Contain("CK_ScheduleWindows_DaysMask");
    }

    [Fact]
    public async Task Check_constraint_rejects_unknown_block_rule_effect()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        using var connection = db.OpenConnectionWithPragmas();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO BlockRules
                (Id, RuleType, MatchType, Value, NormalizedValue, Category, Effect, IsEnabled, IsBuiltIn,
                 EnforcementStatus, CreatedAtUtc, UpdatedAtUtc)
            VALUES
                ($id, 'Domain', 'Exact', 'example.com', 'example.com', 'Custom', 'Pause', 1, 0,
                 'Enforced', '2026-10-29T00:00:00.000Z', '2026-10-29T00:00:00.000Z');
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());

        var exception = Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());

        exception.Message.Should().Contain("CK_BlockRules_Effect");
    }

    private static Dictionary<string, ColumnInfo> ReadColumns(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_xinfo(\"{table}\");";

        var columns = new Dictionary<string, ColumnInfo>(StringComparer.Ordinal);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var name = reader.GetString(1);
            var type = reader.GetString(2);
            var isNotNull = reader.GetInt64(3) == 1;
            var defaultValue = reader.IsDBNull(4) ? null : reader.GetString(4);
            var isPrimaryKey = reader.GetInt64(5) == 1;
            var hidden = reader.FieldCount > 6 ? (int)reader.GetInt64(6) : 0;

            columns[name] = new ColumnInfo(type, isNotNull, defaultValue, isPrimaryKey, hidden);
        }

        return columns;
    }

    private sealed record ColumnInfo(
        string Type,
        bool NotNull,
        string? DefaultValue,
        bool PrimaryKey,
        int Hidden)
    {
        public bool IsGenerated => Hidden is 2 or 3;

        public bool IsStored => Hidden == 3;

        public int? MaxLength => Type.StartsWith("TEXT(", StringComparison.Ordinal)
            ? int.Parse(Type[5..^1], System.Globalization.CultureInfo.InvariantCulture)
            : null;
    }
}
