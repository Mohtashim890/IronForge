using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace IronForge.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentMcpClientsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClientId",
                table: "AgentDelegations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "AgentMcpClientAuthorizations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    AgentEntityId = table.Column<int>(type: "integer", nullable: false),
                    McpClientEntityId = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentMcpClientAuthorizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentMcpClientAuthorizations_Agents_AgentEntityId",
                        column: x => x.AgentEntityId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AgentMcpClientAuthorizations_McpClients_McpClientEntityId",
                        column: x => x.McpClientEntityId,
                        principalTable: "McpClients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AgentMcpClientAuthorizations_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentMcpClientAuthorizations_AgentEntityId",
                table: "AgentMcpClientAuthorizations",
                column: "AgentEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentMcpClientAuthorizations_McpClientEntityId",
                table: "AgentMcpClientAuthorizations",
                column: "McpClientEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentMcpClientAuthorizations_TenantId_AgentEntityId_McpClie~",
                table: "AgentMcpClientAuthorizations",
                columns: new[] { "TenantId", "AgentEntityId", "McpClientEntityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentMcpClientAuthorizations_TenantId_McpClientEntityId_IsA~",
                table: "AgentMcpClientAuthorizations",
                columns: new[] { "TenantId", "McpClientEntityId", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentMcpClientAuthorizations");

            migrationBuilder.DropColumn(
                name: "ClientId",
                table: "AgentDelegations");
        }
    }
}
