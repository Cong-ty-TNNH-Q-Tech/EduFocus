using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduFocus.Cloud.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceFcmWithUnifiedPush : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PushRegistrations_FcmToken",
                table: "PushRegistrations");

            migrationBuilder.DropColumn(
                name: "FcmToken",
                table: "PushRegistrations");

            migrationBuilder.AddColumn<string>(
                name: "Auth",
                table: "PushRegistrations",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Endpoint",
                table: "PushRegistrations",
                type: "TEXT",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "P256dh",
                table: "PushRegistrations",
                type: "TEXT",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_PushRegistrations_Endpoint",
                table: "PushRegistrations",
                column: "Endpoint");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PushRegistrations_Endpoint",
                table: "PushRegistrations");

            migrationBuilder.DropColumn(
                name: "Auth",
                table: "PushRegistrations");

            migrationBuilder.DropColumn(
                name: "Endpoint",
                table: "PushRegistrations");

            migrationBuilder.DropColumn(
                name: "P256dh",
                table: "PushRegistrations");

            migrationBuilder.AddColumn<string>(
                name: "FcmToken",
                table: "PushRegistrations",
                type: "TEXT",
                maxLength: 512,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_PushRegistrations_FcmToken",
                table: "PushRegistrations",
                column: "FcmToken");
        }
    }
}
