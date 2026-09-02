using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtheusPulse.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSystemInstallation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSystem",
                table: "Installations",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsSystem",
                table: "Installations");
        }
    }
}
