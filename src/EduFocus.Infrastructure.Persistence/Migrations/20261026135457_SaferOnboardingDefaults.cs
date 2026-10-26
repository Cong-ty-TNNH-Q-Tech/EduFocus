using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduFocus.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SaferOnboardingDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE AppSettings SET MonitoringEnabled = 0, StartWithWindows = 0 WHERE OnboardingCompleted = 0;");
            migrationBuilder.AlterColumn<bool>(
                name: "StartWithWindows",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "INTEGER",
                oldDefaultValue: true);

            migrationBuilder.AlterColumn<bool>(
                name: "MonitoringEnabled",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "INTEGER",
                oldDefaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "StartWithWindows",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "INTEGER",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<bool>(
                name: "MonitoringEnabled",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "INTEGER",
                oldDefaultValue: false);
        }
    }
}
