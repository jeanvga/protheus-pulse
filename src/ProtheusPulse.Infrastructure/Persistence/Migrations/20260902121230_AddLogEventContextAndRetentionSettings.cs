using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtheusPulse.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLogEventContextAndRetentionSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Company",
                table: "LogEvents",
                type: "TEXT",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Computer",
                table: "LogEvents",
                type: "TEXT",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Environment",
                table: "LogEvents",
                type: "TEXT",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Module",
                table: "LogEvents",
                type: "TEXT",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Routine",
                table: "LogEvents",
                type: "TEXT",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceFile",
                table: "LogEvents",
                type: "TEXT",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceLine",
                table: "LogEvents",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThreadId",
                table: "LogEvents",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "User",
                table: "LogEvents",
                type: "TEXT",
                maxLength: 120,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RetentionSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    HistoryRetentionDays = table.Column<int>(type: "INTEGER", nullable: false),
                    MetricAggregationAfterDays = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetentionSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RetentionSettings");

            migrationBuilder.DropColumn(
                name: "Company",
                table: "LogEvents");

            migrationBuilder.DropColumn(
                name: "Computer",
                table: "LogEvents");

            migrationBuilder.DropColumn(
                name: "Environment",
                table: "LogEvents");

            migrationBuilder.DropColumn(
                name: "Module",
                table: "LogEvents");

            migrationBuilder.DropColumn(
                name: "Routine",
                table: "LogEvents");

            migrationBuilder.DropColumn(
                name: "SourceFile",
                table: "LogEvents");

            migrationBuilder.DropColumn(
                name: "SourceLine",
                table: "LogEvents");

            migrationBuilder.DropColumn(
                name: "ThreadId",
                table: "LogEvents");

            migrationBuilder.DropColumn(
                name: "User",
                table: "LogEvents");
        }
    }
}
