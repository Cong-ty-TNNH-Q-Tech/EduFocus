using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduFocus.SqliteDataAccess.Migrations
{
    /// <inheritdoc />
    public partial class CloudOutboxTextClientEventId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ClientEventId",
                table: "CloudOutbox",
                type: "TEXT(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "ClientEventId",
                table: "CloudOutbox",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT(64)",
                oldMaxLength: 64);
        }
    }
}
