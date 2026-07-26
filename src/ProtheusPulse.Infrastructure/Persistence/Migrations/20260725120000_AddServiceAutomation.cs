using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtheusPulse.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceAutomation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoStartEnabled",
                table: "Installations",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsExclusive",
                table: "Installations",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LastStatus",
                table: "WindowsServiceTargets",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LastStatusAt",
                table: "WindowsServiceTargets",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoStartEnabled",
                table: "Installations");

            migrationBuilder.DropColumn(
                name: "IsExclusive",
                table: "Installations");

            migrationBuilder.DropColumn(
                name: "LastStatus",
                table: "WindowsServiceTargets");

            migrationBuilder.DropColumn(
                name: "LastStatusAt",
                table: "WindowsServiceTargets");
        }
    }
}
