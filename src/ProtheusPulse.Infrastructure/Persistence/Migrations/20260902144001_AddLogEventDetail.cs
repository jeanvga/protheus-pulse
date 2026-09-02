using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtheusPulse.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLogEventDetail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Detail",
                table: "LogEvents",
                type: "TEXT",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LogEvents_ObservedAt",
                table: "LogEvents",
                column: "ObservedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LogEvents_ObservedAt",
                table: "LogEvents");

            migrationBuilder.DropColumn(
                name: "Detail",
                table: "LogEvents");
        }
    }
}
