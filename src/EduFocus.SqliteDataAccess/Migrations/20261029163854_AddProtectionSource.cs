using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduFocus.SqliteDataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddProtectionSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProtectionSource",
                table: "AppSettings",
                type: "TEXT(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.Sql(
                "UPDATE \"AppSettings\" SET \"ProtectionSource\" = 'Manual' WHERE \"ProtectionEnabled\" = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProtectionSource",
                table: "AppSettings");
        }
    }
}
