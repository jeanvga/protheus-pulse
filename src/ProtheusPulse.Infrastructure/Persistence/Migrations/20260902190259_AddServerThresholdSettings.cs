using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtheusPulse.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddServerThresholdSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ServerThresholdSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CpuWarningPercent = table.Column<double>(type: "REAL", nullable: false),
                    CpuCriticalPercent = table.Column<double>(type: "REAL", nullable: false),
                    MemoryWarningPercent = table.Column<double>(type: "REAL", nullable: false),
                    MemoryCriticalPercent = table.Column<double>(type: "REAL", nullable: false),
                    DiskFreeWarningPercent = table.Column<double>(type: "REAL", nullable: false),
                    DiskFreeCriticalPercent = table.Column<double>(type: "REAL", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerThresholdSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServerThresholdSettings");
        }
    }
}
