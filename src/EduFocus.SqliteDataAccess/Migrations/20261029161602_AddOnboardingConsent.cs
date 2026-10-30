using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduFocus.SqliteDataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddOnboardingConsent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GuardianAcknowledgedAtUtc",
                table: "AppSettings",
                type: "TEXT(28)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrivacyAcceptedAtUtc",
                table: "AppSettings",
                type: "TEXT(28)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GuardianAcknowledgedAtUtc",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "PrivacyAcceptedAtUtc",
                table: "AppSettings");
        }
    }
}
