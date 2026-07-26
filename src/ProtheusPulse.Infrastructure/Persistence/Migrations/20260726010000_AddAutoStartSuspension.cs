using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtheusPulse.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAutoStartSuspension : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoStartSuspended",
                table: "WindowsServiceTargets",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoStartSuspended",
                table: "WindowsServiceTargets");
        }
    }
}
