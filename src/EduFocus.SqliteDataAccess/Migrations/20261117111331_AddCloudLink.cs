using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduFocus.SqliteDataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddCloudLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CloudLink",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT(36)", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    ConsentAcceptedAtUtc = table.Column<string>(type: "TEXT(28)", nullable: true),
                    CloudBaseUrl = table.Column<string>(type: "TEXT(512)", maxLength: 512, nullable: true),
                    PairingSessionId = table.Column<Guid>(type: "TEXT(36)", nullable: true),
                    PairingCode = table.Column<string>(type: "TEXT(32)", maxLength: 32, nullable: true),
                    PairingExpiresAtUtc = table.Column<string>(type: "TEXT(28)", nullable: true),
                    FamilyId = table.Column<Guid>(type: "TEXT(36)", nullable: true),
                    DesktopDeviceId = table.Column<Guid>(type: "TEXT(36)", nullable: true),
                    DesktopToken = table.Column<string>(type: "TEXT(256)", maxLength: 256, nullable: true),
                    LinkedAtUtc = table.Column<string>(type: "TEXT(28)", nullable: true),
                    LastSyncAtUtc = table.Column<string>(type: "TEXT(28)", nullable: true),
                    LastError = table.Column<string>(type: "TEXT(512)", maxLength: 512, nullable: true),
                    CreatedAtUtc = table.Column<string>(type: "TEXT(28)", nullable: false),
                    UpdatedAtUtc = table.Column<string>(type: "TEXT(28)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CloudLink", x => x.Id);
                    table.CheckConstraint("CK_CloudLink_EnabledRequiresConsent", "\"IsEnabled\" = 0 OR \"ConsentAcceptedAtUtc\" IS NOT NULL");
                    table.CheckConstraint("CK_CloudLink_TokenClearedWhenDisabled", "\"DesktopToken\" IS NULL OR \"IsEnabled\" = 1");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CloudLink");
        }
    }
}
