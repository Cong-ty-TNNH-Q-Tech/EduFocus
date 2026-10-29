using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduFocus.SqliteDataAccess.Migrations
{
    /// <inheritdoc />
    public partial class InitialEduFocusSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT(36)", nullable: false),
                    ProtectionEnabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    MonitoringEnabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    ScheduleEnabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    MonitorOnlyDuringClasses = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    CameraDeviceId = table.Column<string>(type: "TEXT(256)", maxLength: 256, nullable: true),
                    DistractionThresholdSeconds = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 20),
                    AbsenceThresholdSeconds = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 30),
                    SampleIntervalSeconds = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 15),
                    ActivityRetentionDays = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 30),
                    EventRetentionDays = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 90),
                    SessionRetentionDays = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 365),
                    StartWithWindows = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    Theme = table.Column<string>(type: "TEXT(16)", maxLength: 16, nullable: false, defaultValue: "dark"),
                    Language = table.Column<string>(type: "TEXT(8)", maxLength: 8, nullable: false, defaultValue: "vi"),
                    OnboardingCompleted = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    ScheduleOverrideUntilUtc = table.Column<string>(type: "TEXT(28)", nullable: true),
                    LastCleanShutdownAtUtc = table.Column<string>(type: "TEXT(28)", nullable: true),
                    CreatedAtUtc = table.Column<string>(type: "TEXT(28)", nullable: false),
                    UpdatedAtUtc = table.Column<string>(type: "TEXT(28)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Id);
                    table.CheckConstraint("CK_AppSettings_Language", "\"Language\" IN ('vi', 'en')");
                    table.CheckConstraint("CK_AppSettings_Retention", "\"ActivityRetentionDays\" > 0 AND \"EventRetentionDays\" > 0 AND \"SessionRetentionDays\" > 0");
                    table.CheckConstraint("CK_AppSettings_Theme", "\"Theme\" IN ('dark', 'light', 'system')");
                    table.CheckConstraint("CK_AppSettings_Thresholds", "\"DistractionThresholdSeconds\" > 0 AND \"AbsenceThresholdSeconds\" > 0 AND \"SampleIntervalSeconds\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT(36)", nullable: false),
                    OccurredAtUtc = table.Column<string>(type: "TEXT(28)", nullable: false),
                    Actor = table.Column<string>(type: "TEXT(16)", maxLength: 16, nullable: false),
                    EventType = table.Column<string>(type: "TEXT(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "TEXT(512)", maxLength: 512, nullable: false),
                    MetadataJson = table.Column<string>(type: "TEXT(2048)", maxLength: 2048, nullable: true),
                    CorrelationId = table.Column<Guid>(type: "TEXT(36)", nullable: true),
                    CreatedAtUtc = table.Column<string>(type: "TEXT(28)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                    table.CheckConstraint("CK_AuditLogs_Actor", "\"Actor\" IN ('Parent', 'System', 'Student')");
                });

            migrationBuilder.CreateTable(
                name: "BlockRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT(36)", nullable: false),
                    RuleType = table.Column<string>(type: "TEXT(16)", maxLength: 16, nullable: false),
                    MatchType = table.Column<string>(type: "TEXT(16)", maxLength: 16, nullable: false),
                    Value = table.Column<string>(type: "TEXT(512)", maxLength: 512, nullable: false),
                    NormalizedValue = table.Column<string>(type: "TEXT(512)", maxLength: 512, nullable: false),
                    Category = table.Column<string>(type: "TEXT(32)", maxLength: 32, nullable: false),
                    Effect = table.Column<string>(type: "TEXT(8)", maxLength: 8, nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    IsBuiltIn = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    EnforcementStatus = table.Column<string>(type: "TEXT(16)", maxLength: 16, nullable: false, defaultValue: "Enforced"),
                    ExpiresAtUtc = table.Column<string>(type: "TEXT(28)", nullable: true),
                    Note = table.Column<string>(type: "TEXT(512)", maxLength: 512, nullable: true),
                    CreatedAtUtc = table.Column<string>(type: "TEXT(28)", nullable: false),
                    UpdatedAtUtc = table.Column<string>(type: "TEXT(28)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockRules", x => x.Id);
                    table.CheckConstraint("CK_BlockRules_Category", "\"Category\" IN ('Chatbot', 'Social', 'Game', 'Study', 'System', 'Custom')");
                    table.CheckConstraint("CK_BlockRules_Effect", "\"Effect\" IN ('Block', 'Allow')");
                    table.CheckConstraint("CK_BlockRules_EnforcementStatus", "\"EnforcementStatus\" IN ('Enforced', 'NotEnforced')");
                    table.CheckConstraint("CK_BlockRules_MatchType", "\"MatchType\" IN ('Exact', 'Suffix', 'Contains')");
                    table.CheckConstraint("CK_BlockRules_RuleType", "\"RuleType\" IN ('Domain', 'App', 'Url')");
                });

            migrationBuilder.CreateTable(
                name: "ParentCredentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT(36)", nullable: false),
                    PinHash = table.Column<string>(type: "TEXT(128)", maxLength: 128, nullable: false),
                    PinSalt = table.Column<string>(type: "TEXT(64)", maxLength: 64, nullable: false),
                    PinIterations = table.Column<int>(type: "INTEGER", nullable: false),
                    RecoveryCodeHash = table.Column<string>(type: "TEXT(128)", maxLength: 128, nullable: false),
                    RecoveryCodeSalt = table.Column<string>(type: "TEXT(64)", maxLength: 64, nullable: false),
                    RecoveryCodeIterations = table.Column<int>(type: "INTEGER", nullable: false),
                    FailedAttempts = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    LockoutUntilUtc = table.Column<string>(type: "TEXT(28)", nullable: true),
                    PinUpdatedAtUtc = table.Column<string>(type: "TEXT(28)", nullable: false),
                    CreatedAtUtc = table.Column<string>(type: "TEXT(28)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParentCredentials", x => x.Id);
                    table.CheckConstraint("CK_ParentCredentials_FailedAttempts", "\"FailedAttempts\" >= 0");
                    table.CheckConstraint("CK_ParentCredentials_PinIterations", "\"PinIterations\" >= 600000");
                    table.CheckConstraint("CK_ParentCredentials_RecoveryCodeIterations", "\"RecoveryCodeIterations\" >= 600000");
                });

            migrationBuilder.CreateTable(
                name: "ScheduleWindows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT(36)", nullable: false),
                    DaysMask = table.Column<int>(type: "INTEGER", nullable: false),
                    StartMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    EndMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    Label = table.Column<string>(type: "TEXT(100)", maxLength: 100, nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<string>(type: "TEXT(28)", nullable: false),
                    UpdatedAtUtc = table.Column<string>(type: "TEXT(28)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleWindows", x => x.Id);
                    table.CheckConstraint("CK_ScheduleWindows_DaysMask", "\"DaysMask\" BETWEEN 1 AND 127");
                    table.CheckConstraint("CK_ScheduleWindows_EndMinutes", "\"EndMinutes\" BETWEEN 1 AND 1440");
                    table.CheckConstraint("CK_ScheduleWindows_Range", "\"EndMinutes\" > \"StartMinutes\"");
                    table.CheckConstraint("CK_ScheduleWindows_StartMinutes", "\"StartMinutes\" BETWEEN 0 AND 1439");
                });

            migrationBuilder.CreateTable(
                name: "StudySessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT(36)", nullable: false),
                    StartedAtUtc = table.Column<string>(type: "TEXT(28)", nullable: false),
                    EndedAtUtc = table.Column<string>(type: "TEXT(28)", nullable: true),
                    LastHeartbeatAtUtc = table.Column<string>(type: "TEXT(28)", nullable: false),
                    StartLocalDate = table.Column<DateOnly>(type: "TEXT(10)", nullable: false),
                    Status = table.Column<string>(type: "TEXT(16)", maxLength: 16, nullable: false),
                    Trigger = table.Column<string>(type: "TEXT(16)", maxLength: 16, nullable: false),
                    MonitoringEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ActiveSeconds = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    FocusedSeconds = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    DistractedSeconds = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    AbsentSeconds = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    DrowsySeconds = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    CameraUnavailableSeconds = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    IdleSeconds = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    StudySeconds = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    DistractionSeconds = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    NeutralSeconds = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    OnlineClassSeconds = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    MonitoringEligibleSeconds = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    ConfidenceAvg = table.Column<double>(type: "REAL", nullable: true),
                    BlockedAttemptCount = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    Note = table.Column<string>(type: "TEXT(512)", maxLength: 512, nullable: true),
                    CreatedAtUtc = table.Column<string>(type: "TEXT(28)", nullable: false),
                    UpdatedAtUtc = table.Column<string>(type: "TEXT(28)", nullable: false),
                    AttentionPercent = table.Column<double>(type: "REAL", nullable: true, computedColumnSql: "CASE WHEN (\"FocusedSeconds\" + \"DistractedSeconds\" + \"AbsentSeconds\" + \"DrowsySeconds\") > 0 THEN \"FocusedSeconds\" * 100.0 / (\"FocusedSeconds\" + \"DistractedSeconds\" + \"AbsentSeconds\" + \"DrowsySeconds\") ELSE NULL END", stored: true),
                    CoveragePercent = table.Column<double>(type: "REAL", nullable: true, computedColumnSql: "CASE WHEN \"MonitoringEligibleSeconds\" > 0 THEN (\"FocusedSeconds\" + \"DistractedSeconds\" + \"AbsentSeconds\" + \"DrowsySeconds\") * 100.0 / \"MonitoringEligibleSeconds\" ELSE NULL END", stored: true),
                    MonitoredSeconds = table.Column<int>(type: "INTEGER", nullable: false, computedColumnSql: "(\"FocusedSeconds\" + \"DistractedSeconds\" + \"AbsentSeconds\" + \"DrowsySeconds\")", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudySessions", x => x.Id);
                    table.CheckConstraint("CK_StudySessions_Status", "\"Status\" IN ('Active', 'Completed', 'Aborted', 'Interrupted')");
                    table.CheckConstraint("CK_StudySessions_Trigger", "\"Trigger\" IN ('Manual', 'Schedule', 'Recovery')");
                });

            migrationBuilder.CreateTable(
                name: "ActivitySamples",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT(36)", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT(36)", nullable: false),
                    SampledAtUtc = table.Column<string>(type: "TEXT(28)", nullable: false),
                    DurationSeconds = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    AppName = table.Column<string>(type: "TEXT(128)", maxLength: 128, nullable: true),
                    ProcessName = table.Column<string>(type: "TEXT(128)", maxLength: 128, nullable: true),
                    WindowTitle = table.Column<string>(type: "TEXT(512)", maxLength: 512, nullable: true),
                    Url = table.Column<string>(type: "TEXT(2048)", maxLength: 2048, nullable: true),
                    Domain = table.Column<string>(type: "TEXT(256)", maxLength: 256, nullable: true),
                    UrlSource = table.Column<string>(type: "TEXT(16)", maxLength: 16, nullable: false, defaultValue: "None"),
                    Category = table.Column<string>(type: "TEXT(32)", maxLength: 32, nullable: false, defaultValue: "Unknown"),
                    IsForeground = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    IsOnlineClass = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    IdleSeconds = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    CreatedAtUtc = table.Column<string>(type: "TEXT(28)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivitySamples", x => x.Id);
                    table.CheckConstraint("CK_ActivitySamples_Category", "\"Category\" IN ('Study', 'Distraction', 'Neutral', 'Unknown')");
                    table.CheckConstraint("CK_ActivitySamples_Duration", "\"DurationSeconds\" >= 0 AND \"IdleSeconds\" >= 0");
                    table.CheckConstraint("CK_ActivitySamples_UrlSource", "\"UrlSource\" IN ('UIA', 'Extension', 'None')");
                    table.ForeignKey(
                        name: "FK_ActivitySamples_StudySessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "StudySessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AttentionEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT(36)", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT(36)", nullable: false),
                    State = table.Column<string>(type: "TEXT(24)", maxLength: 24, nullable: false),
                    StartedAtUtc = table.Column<string>(type: "TEXT(28)", nullable: false),
                    EndedAtUtc = table.Column<string>(type: "TEXT(28)", nullable: true),
                    DurationSeconds = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    Confidence = table.Column<double>(type: "REAL", nullable: true),
                    AppName = table.Column<string>(type: "TEXT(128)", maxLength: 128, nullable: true),
                    Domain = table.Column<string>(type: "TEXT(256)", maxLength: 256, nullable: true),
                    Note = table.Column<string>(type: "TEXT(256)", maxLength: 256, nullable: true),
                    CreatedAtUtc = table.Column<string>(type: "TEXT(28)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttentionEvents", x => x.Id);
                    table.CheckConstraint("CK_AttentionEvents_Confidence", "\"Confidence\" IS NULL OR (\"Confidence\" >= 0 AND \"Confidence\" <= 1)");
                    table.CheckConstraint("CK_AttentionEvents_Duration", "\"DurationSeconds\" >= 0");
                    table.CheckConstraint("CK_AttentionEvents_State", "\"State\" IN ('Focused', 'Distracted', 'Absent', 'Drowsy', 'CameraUnavailable')");
                    table.ForeignKey(
                        name: "FK_AttentionEvents_StudySessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "StudySessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BlockedAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT(36)", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT(36)", nullable: true),
                    OccurredAtUtc = table.Column<string>(type: "TEXT(28)", nullable: false),
                    TargetType = table.Column<string>(type: "TEXT(16)", maxLength: 16, nullable: false),
                    TargetValue = table.Column<string>(type: "TEXT(512)", maxLength: 512, nullable: false),
                    Domain = table.Column<string>(type: "TEXT(256)", maxLength: 256, nullable: true),
                    Url = table.Column<string>(type: "TEXT(2048)", maxLength: 2048, nullable: true),
                    ProcessName = table.Column<string>(type: "TEXT(128)", maxLength: 128, nullable: true),
                    WindowTitle = table.Column<string>(type: "TEXT(512)", maxLength: 512, nullable: true),
                    ObservedVia = table.Column<string>(type: "TEXT(24)", maxLength: 24, nullable: false),
                    CreatedAtUtc = table.Column<string>(type: "TEXT(28)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockedAttempts", x => x.Id);
                    table.CheckConstraint("CK_BlockedAttempts_ObservedVia", "\"ObservedVia\" IN ('BlockPage', 'ProcessWatcher', 'Extension')");
                    table.CheckConstraint("CK_BlockedAttempts_TargetType", "\"TargetType\" IN ('Domain', 'App', 'Url')");
                    table.ForeignKey(
                        name: "FK_BlockedAttempts_StudySessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "StudySessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivitySamples_Category",
                table: "ActivitySamples",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_ActivitySamples_Domain",
                table: "ActivitySamples",
                column: "Domain");

            migrationBuilder.CreateIndex(
                name: "IX_ActivitySamples_SessionId_SampledAtUtc",
                table: "ActivitySamples",
                columns: new[] { "SessionId", "SampledAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AttentionEvents_SessionId_StartedAtUtc",
                table: "AttentionEvents",
                columns: new[] { "SessionId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AttentionEvents_State",
                table: "AttentionEvents",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EventType",
                table: "AuditLogs",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_OccurredAtUtc",
                table: "AuditLogs",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_BlockedAttempts_Domain",
                table: "BlockedAttempts",
                column: "Domain");

            migrationBuilder.CreateIndex(
                name: "IX_BlockedAttempts_OccurredAtUtc",
                table: "BlockedAttempts",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_BlockedAttempts_SessionId",
                table: "BlockedAttempts",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_BlockRules_Category",
                table: "BlockRules",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_BlockRules_Effect_IsEnabled",
                table: "BlockRules",
                columns: new[] { "Effect", "IsEnabled" });

            migrationBuilder.CreateIndex(
                name: "IX_BlockRules_ExpiresAtUtc",
                table: "BlockRules",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "UX_BlockRules_RuleType_MatchType_NormalizedValue_Effect",
                table: "BlockRules",
                columns: new[] { "RuleType", "MatchType", "NormalizedValue", "Effect" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleWindows_IsEnabled",
                table: "ScheduleWindows",
                column: "IsEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_StudySessions_LastHeartbeatAtUtc",
                table: "StudySessions",
                column: "LastHeartbeatAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_StudySessions_StartedAtUtc",
                table: "StudySessions",
                column: "StartedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_StudySessions_StartLocalDate",
                table: "StudySessions",
                column: "StartLocalDate");

            migrationBuilder.CreateIndex(
                name: "IX_StudySessions_Status",
                table: "StudySessions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "UX_StudySessions_Active",
                table: "StudySessions",
                column: "Status",
                unique: true,
                filter: "\"Status\" = 'Active'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivitySamples");

            migrationBuilder.DropTable(
                name: "AppSettings");

            migrationBuilder.DropTable(
                name: "AttentionEvents");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "BlockedAttempts");

            migrationBuilder.DropTable(
                name: "BlockRules");

            migrationBuilder.DropTable(
                name: "ParentCredentials");

            migrationBuilder.DropTable(
                name: "ScheduleWindows");

            migrationBuilder.DropTable(
                name: "StudySessions");
        }
    }
}
