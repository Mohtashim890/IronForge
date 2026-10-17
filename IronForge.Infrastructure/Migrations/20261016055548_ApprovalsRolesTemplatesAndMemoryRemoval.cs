using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace IronForge.Api.Migrations
{
    /// <inheritdoc />
    public partial class ApprovalsRolesTemplatesAndMemoryRemoval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The agent memory feature was never wired to an endpoint or tool.
            migrationBuilder.DropTable(
                name: "AgentMemories");

            migrationBuilder.AddColumn<List<string>>(
                name: "AllowedRoles",
                table: "Agents",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<string>(
                name: "TemplateId",
                table: "Agents",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TemplateVersion",
                table: "Agents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DecidedByUserId",
                table: "AgentApprovals",
                type: "integer",
                nullable: true);

            // Agents installed from the templates become managed at version 1:
            // their content is unchanged, and template rollouts of later
            // versions now reach them. Tenants can detach to opt out.
            migrationBuilder.Sql(
                "UPDATE \"Agents\" SET \"TemplateId\" = \"AgentId\", \"TemplateVersion\" = 1 " +
                "WHERE \"AgentId\" IN ('product-agent', 'insights-agent');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowedRoles",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "TemplateId",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "TemplateVersion",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "DecidedByUserId",
                table: "AgentApprovals");

            migrationBuilder.CreateTable(
                name: "AgentMemories",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AgentId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Content = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Scope = table.Column<int>(type: "integer", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentMemories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentMemories_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AgentMemories_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentMemories_TenantId",
                table: "AgentMemories",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentMemories_UserId",
                table: "AgentMemories",
                column: "UserId");
        }
    }
}
