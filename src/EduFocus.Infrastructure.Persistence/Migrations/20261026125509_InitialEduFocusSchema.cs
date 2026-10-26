using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduFocus.Infrastructure.Persistence.Migrations
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
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProtectionEnabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    MonitoringEnabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    ScheduleEnabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    MonitorOnlyDuringClasses = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    CameraDeviceId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    DistractionThresholdSeconds = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 20),
                    AbsenceThresholdSeconds = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 30),
                    SampleIntervalSeconds = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 15),
                    ActivityRetentionDays = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 30),
                    EventRetentionDays = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 90),
                    SessionRetentionDays = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 365),
                    StartWithWindows = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    Theme = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false, defaultValue: "dark"),
                    Language = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false, defaultValue: "vi"),
                    OnboardingCompleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    ScheduleOverrideUntilUtc = table.Column<string>(type: "TEXT", nullable: true),
                    LastCleanShutdownAtUtc = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Id);
                    table.CheckConstraint("CK_AppSettings_SingletonId", "Id = '00000000-0000-0000-0000-000000000001'");
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OccurredAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    Actor = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    MetadataJson = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    CorrelationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                    table.CheckConstraint("CK_AuditLogs_Actor", "Actor IN ('Parent','System','Student')");
                });

            migrationBuilder.CreateTable(
                name: "BlockRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RuleType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    MatchType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    NormalizedValue = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Effect = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    IsBuiltIn = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    EnforcementStatus = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false, defaultValue: "Enforced"),
                    ExpiresAtUtc = table.Column<string>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockRules", x => x.Id);
                    table.CheckConstraint("CK_BlockRules_Effect", "Effect IN ('Block','Allow')");
                    table.CheckConstraint("CK_BlockRules_EnforcementStatus", "EnforcementStatus IN ('Enforced','NotEnforced')");
                    table.CheckConstraint("CK_BlockRules_MatchType", "MatchType IN ('Exact','Suffix','Contains')");
                    table.CheckConstraint("CK_BlockRules_RuleType", "RuleType IN ('Domain','App','Url')");
                });

            migrationBuilder.CreateTable(
                name: "ParentCredentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PinHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    PinSalt = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PinIterations = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 600000),
                    RecoveryCodeHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    RecoveryCodeSalt = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RecoveryCodeIterations = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 600000),
                    FailedAttempts = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    LockoutUntilUtc = table.Column<string>(type: "TEXT", nullable: true),
                    PinUpdatedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParentCredentials", x => x.Id);
                    table.CheckConstraint("CK_ParentCredentials_FailedAttempts", "FailedAttempts BETWEEN 0 AND 5");
                    table.CheckConstraint("CK_ParentCredentials_PinIterations", "PinIterations >= 600000");
                    table.CheckConstraint("CK_ParentCredentials_RecoveryIterations", "RecoveryCodeIterations >= 600000");
                    table.CheckConstraint("CK_ParentCredentials_SingletonId", "Id = '00000000-0000-0000-0000-000000000002'");
                });

            migrationBuilder.CreateTable(
                name: "ScheduleWindows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DaysMask = table.Column<int>(type: "INTEGER", nullable: false),
                    StartMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    EndMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleWindows", x => x.Id);
                    table.CheckConstraint("CK_ScheduleWindows_DaysMask", "DaysMask BETWEEN 1 AND 127");
                    table.CheckConstraint("CK_ScheduleWindows_TimeRange", "StartMinutes BETWEEN 0 AND 1439 AND EndMinutes BETWEEN 1 AND 1440 AND EndMinutes > StartMinutes");
                });

            migrationBuilder.CreateTable(
                name: "StudySessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    EndedAtUtc = table.Column<string>(type: "TEXT", nullable: true),
                    LastHeartbeatAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    StartLocalDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Trigger = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    MonitoringEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ActiveSeconds = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    FocusedSeconds = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    DistractedSeconds = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    AbsentSeconds = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    DrowsySeconds = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    CameraUnavailableSeconds = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    IdleSeconds = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    StudySeconds = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    DistractionSeconds = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    NeutralSeconds = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    OnlineClassSeconds = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    MonitoredSeconds = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    MonitoringEligibleSeconds = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    AttentionPercent = table.Column<double>(type: "REAL", nullable: true),
                    CoveragePercent = table.Column<double>(type: "REAL", nullable: true),
                    ConfidenceAvg = table.Column<double>(type: "REAL", nullable: true),
                    BlockedAttemptCount = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    Note = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudySessions", x => x.Id);
                    table.CheckConstraint("CK_StudySessions_Lifecycle", "(Status = 'Active' AND EndedAtUtc IS NULL) OR (Status <> 'Active' AND EndedAtUtc IS NOT NULL)");
                    table.CheckConstraint("CK_StudySessions_MonitoredSeconds", "MonitoredSeconds = FocusedSeconds + DistractedSeconds + AbsentSeconds + DrowsySeconds");
                    table.CheckConstraint("CK_StudySessions_NonNegativeCounters", "ActiveSeconds >= 0 AND FocusedSeconds >= 0 AND DistractedSeconds >= 0 AND AbsentSeconds >= 0 AND DrowsySeconds >= 0 AND CameraUnavailableSeconds >= 0 AND IdleSeconds >= 0 AND StudySeconds >= 0 AND DistractionSeconds >= 0 AND NeutralSeconds >= 0 AND OnlineClassSeconds >= 0 AND MonitoredSeconds >= 0 AND MonitoringEligibleSeconds >= 0 AND BlockedAttemptCount >= 0");
                    table.CheckConstraint("CK_StudySessions_Percentages", "(AttentionPercent IS NULL OR AttentionPercent BETWEEN 0 AND 100) AND (CoveragePercent IS NULL OR CoveragePercent >= 0) AND (ConfidenceAvg IS NULL OR ConfidenceAvg BETWEEN 0 AND 1)");
                });

            migrationBuilder.CreateTable(
                name: "ActivitySamples",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SampledAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    DurationSeconds = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    AppName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ProcessName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    WindowTitle = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    Url = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    Domain = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    UrlSource = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false, defaultValue: "None"),
                    Category = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false, defaultValue: "Unknown"),
                    IsForeground = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    IsOnlineClass = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    IdleSeconds = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivitySamples", x => x.Id);
                    table.CheckConstraint("CK_ActivitySamples_Durations", "DurationSeconds >= 0 AND IdleSeconds >= 0");
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
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    StartedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    EndedAtUtc = table.Column<string>(type: "TEXT", nullable: true),
                    DurationSeconds = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    Confidence = table.Column<double>(type: "REAL", nullable: true),
                    AppName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Domain = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttentionEvents", x => x.Id);
                    table.CheckConstraint("CK_AttentionEvents_DurationConfidence", "DurationSeconds >= 0 AND (Confidence IS NULL OR Confidence BETWEEN 0 AND 1)");
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
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OccurredAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    TargetType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    TargetValue = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Domain = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Url = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    ProcessName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    WindowTitle = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ObservedVia = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockedAttempts", x => x.Id);
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
                name: "IX_BlockRules_RuleType_MatchType_NormalizedValue_Effect",
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
                name: "UX_StudySessions_Active",
                table: "StudySessions",
                column: "Status",
                unique: true,
                filter: "Status = 'Active'");
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
