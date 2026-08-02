using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtheusPulse.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Remove os agentes externos de log. O coletor incremental do próprio Pulse
    /// passou a alimentar o resumo por e-mail, então a tabela e a marcação de
    /// origem alimentada por agente deixaram de ter uso.
    /// </summary>
    public partial class RemoveLogAgents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LogAgents");

            migrationBuilder.DropColumn(
                name: "IsAgentManaged",
                table: "LogSources");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAgentManaged",
                table: "LogSources",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "LogAgents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ComponentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    AgentKey = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    LastSeenAt = table.Column<long>(type: "INTEGER", nullable: true),
                    ReceivedEventCount = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LogAgents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LogAgents_Components_ComponentId",
                        column: x => x.ComponentId,
                        principalTable: "Components",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LogAgents_AgentKey",
                table: "LogAgents",
                column: "AgentKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LogAgents_ComponentId",
                table: "LogAgents",
                column: "ComponentId");
        }
    }
}
