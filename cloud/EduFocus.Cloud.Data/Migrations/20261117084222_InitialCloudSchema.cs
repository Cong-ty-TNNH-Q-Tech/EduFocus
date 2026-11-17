using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduFocus.Cloud.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCloudSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Families",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DeletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Families", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Devices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Platform = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AppVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Devices", x => x.Id);
                    table.CheckConstraint("CK_Devices_Kind", "\"Kind\" IN ('Desktop', 'Mobile')");
                    table.ForeignKey(
                        name: "FK_Devices_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NotificationPreferences",
                columns: table => new
                {
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttentionAlert = table.Column<bool>(type: "boolean", nullable: false),
                    DailySummary = table.Column<bool>(type: "boolean", nullable: false),
                    ProtectionOff = table.Column<bool>(type: "boolean", nullable: false),
                    SecurityAlert = table.Column<bool>(type: "boolean", nullable: false),
                    SecurityAlertBypassQuietHours = table.Column<bool>(type: "boolean", nullable: false),
                    QuietStartMinutes = table.Column<int>(type: "integer", nullable: false),
                    QuietEndMinutes = table.Column<int>(type: "integer", nullable: false),
                    DailySummaryMinutes = table.Column<int>(type: "integer", nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationPreferences", x => x.FamilyId);
                    table.CheckConstraint("CK_NotificationPreferences_Minutes", "\"QuietStartMinutes\" BETWEEN 0 AND 1439 AND \"QuietEndMinutes\" BETWEEN 0 AND 1439 AND \"DailySummaryMinutes\" BETWEEN 0 AND 1439");
                    table.ForeignKey(
                        name: "FK_NotificationPreferences_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DedupeKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    LocalDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SentAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    FailureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.CheckConstraint("CK_Notifications_Category", "\"Category\" IN ('DailySummary', 'AttentionAlert', 'ProtectionOff', 'SecurityAlert')");
                    table.CheckConstraint("CK_Notifications_Status", "\"Status\" IN ('Pending', 'Sent', 'Suppressed', 'Failed')");
                    table.ForeignKey(
                        name: "FK_Notifications_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SyncEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientEventId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncEvents", x => x.Id);
                    table.CheckConstraint("CK_SyncEvents_Status", "\"Status\" IN ('Pending', 'Processed', 'Rejected')");
                    table.CheckConstraint("CK_SyncEvents_Type", "\"Type\" IN ('DailySummary', 'AttentionAlert', 'ProtectionOff', 'SecurityAlert')");
                    table.ForeignKey(
                        name: "FK_SyncEvents_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CloudAuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorDeviceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MetadataJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CloudAuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CloudAuditLogs_Devices_ActorDeviceId",
                        column: x => x.ActorDeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CloudAuditLogs_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "DailySummaries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    DesktopDeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SessionsCount = table.Column<int>(type: "integer", nullable: false),
                    ActiveSeconds = table.Column<long>(type: "bigint", nullable: false),
                    StudySeconds = table.Column<long>(type: "bigint", nullable: false),
                    IdleSeconds = table.Column<long>(type: "bigint", nullable: false),
                    OnlineClassSeconds = table.Column<long>(type: "bigint", nullable: false),
                    MonitoringEligibleSeconds = table.Column<long>(type: "bigint", nullable: false),
                    MonitoredSeconds = table.Column<long>(type: "bigint", nullable: false),
                    FocusedSeconds = table.Column<long>(type: "bigint", nullable: false),
                    DistractedSeconds = table.Column<long>(type: "bigint", nullable: false),
                    AbsentSeconds = table.Column<long>(type: "bigint", nullable: false),
                    DrowsySeconds = table.Column<long>(type: "bigint", nullable: false),
                    BlockedAttempts = table.Column<int>(type: "integer", nullable: false),
                    AttentionPercent = table.Column<int>(type: "integer", nullable: true),
                    CoveragePercent = table.Column<int>(type: "integer", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailySummaries", x => x.Id);
                    table.CheckConstraint("CK_DailySummaries_AttentionPercent", "\"AttentionPercent\" IS NULL OR \"AttentionPercent\" BETWEEN 0 AND 100");
                    table.CheckConstraint("CK_DailySummaries_CoveragePercent", "\"CoveragePercent\" IS NULL OR \"CoveragePercent\" BETWEEN 0 AND 100");
                    table.CheckConstraint("CK_DailySummaries_NonNegative", "\"SessionsCount\" >= 0 AND \"ActiveSeconds\" >= 0 AND \"StudySeconds\" >= 0 AND \"IdleSeconds\" >= 0 AND \"OnlineClassSeconds\" >= 0 AND \"MonitoringEligibleSeconds\" >= 0 AND \"MonitoredSeconds\" >= 0 AND \"FocusedSeconds\" >= 0 AND \"DistractedSeconds\" >= 0 AND \"AbsentSeconds\" >= 0 AND \"DrowsySeconds\" >= 0 AND \"BlockedAttempts\" >= 0");
                    table.ForeignKey(
                        name: "FK_DailySummaries_Devices_DesktopDeviceId",
                        column: x => x.DesktopDeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DailySummaries_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeviceTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "char(64)", maxLength: 64, nullable: false),
                    Scopes = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastUsedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceTokens_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PairingSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    DesktopDeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeHash = table.Column<string>(type: "char(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClaimedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClaimedByDeviceId = table.Column<Guid>(type: "uuid", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PairingSessions", x => x.Id);
                    table.CheckConstraint("CK_PairingSessions_AttemptCount", "\"AttemptCount\" >= 0");
                    table.ForeignKey(
                        name: "FK_PairingSessions_Devices_ClaimedByDeviceId",
                        column: x => x.ClaimedByDeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PairingSessions_Devices_DesktopDeviceId",
                        column: x => x.DesktopDeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PairingSessions_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PushRegistrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    FcmToken = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Platform = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    InvalidatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushRegistrations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PushRegistrations_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CloudAuditLogs_ActorDeviceId",
                table: "CloudAuditLogs",
                column: "ActorDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_CloudAuditLogs_CreatedAtUtc",
                table: "CloudAuditLogs",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CloudAuditLogs_FamilyId",
                table: "CloudAuditLogs",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_DailySummaries_DesktopDeviceId",
                table: "DailySummaries",
                column: "DesktopDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_DailySummaries_FamilyId_LocalDate",
                table: "DailySummaries",
                columns: new[] { "FamilyId", "LocalDate" });

            migrationBuilder.CreateIndex(
                name: "UX_DailySummaries_Family_Device_Date",
                table: "DailySummaries",
                columns: new[] { "FamilyId", "DesktopDeviceId", "LocalDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Devices_FamilyId",
                table: "Devices",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceTokens_DeviceId",
                table: "DeviceTokens",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "UX_DeviceTokens_TokenHash",
                table: "DeviceTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_FamilyId_CreatedAtUtc",
                table: "Notifications",
                columns: new[] { "FamilyId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_Notifications_Family_DedupeKey",
                table: "Notifications",
                columns: new[] { "FamilyId", "DedupeKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PairingSessions_ClaimedByDeviceId",
                table: "PairingSessions",
                column: "ClaimedByDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_PairingSessions_DesktopDeviceId",
                table: "PairingSessions",
                column: "DesktopDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_PairingSessions_ExpiresAtUtc",
                table: "PairingSessions",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PairingSessions_FamilyId",
                table: "PairingSessions",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "UX_PairingSessions_CodeHash_Unclaimed",
                table: "PairingSessions",
                column: "CodeHash",
                unique: true,
                filter: "\"ClaimedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PushRegistrations_DeviceId",
                table: "PushRegistrations",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_PushRegistrations_FcmToken",
                table: "PushRegistrations",
                column: "FcmToken");

            migrationBuilder.CreateIndex(
                name: "IX_SyncEvents_FamilyId_ReceivedAtUtc",
                table: "SyncEvents",
                columns: new[] { "FamilyId", "ReceivedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_SyncEvents_Family_ClientEventId",
                table: "SyncEvents",
                columns: new[] { "FamilyId", "ClientEventId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CloudAuditLogs");

            migrationBuilder.DropTable(
                name: "DailySummaries");

            migrationBuilder.DropTable(
                name: "DeviceTokens");

            migrationBuilder.DropTable(
                name: "NotificationPreferences");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "PairingSessions");

            migrationBuilder.DropTable(
                name: "PushRegistrations");

            migrationBuilder.DropTable(
                name: "SyncEvents");

            migrationBuilder.DropTable(
                name: "Devices");

            migrationBuilder.DropTable(
                name: "Families");
        }
    }
}
