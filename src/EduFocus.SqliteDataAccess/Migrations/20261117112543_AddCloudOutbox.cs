using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduFocus.SqliteDataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddCloudOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CloudOutbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientEventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT(32)", maxLength: 32, nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<string>(type: "TEXT(28)", nullable: false),
                    NextAttemptAtUtc = table.Column<string>(type: "TEXT(28)", nullable: false),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SentAtUtc = table.Column<string>(type: "TEXT(28)", nullable: true),
                    FailedAtUtc = table.Column<string>(type: "TEXT(28)", nullable: true),
                    LastError = table.Column<string>(type: "TEXT(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CloudOutbox", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CloudOutbox_CreatedAtUtc",
                table: "CloudOutbox",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CloudOutbox_Due",
                table: "CloudOutbox",
                columns: new[] { "SentAtUtc", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_CloudOutbox_ClientEventId",
                table: "CloudOutbox",
                column: "ClientEventId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CloudOutbox");
        }
    }
}
