using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace IronForge.Api.Migrations
{
    /// <inheritdoc />
    public partial class AgentCapabilitiesFromToolsAndModelProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Agent permissions are now derived from tool bindings. The stored
            // lists were hand-edited and unvalidated; they are not migrated.
            migrationBuilder.DropTable(
                name: "AgentPermissions");

            migrationBuilder.AddColumn<List<string>>(
                name: "AddOnModelProfiles",
                table: "Tenants",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AddOnModelProfiles",
                table: "Tenants");

            migrationBuilder.CreateTable(
                name: "AgentPermissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AgentId = table.Column<int>(type: "integer", nullable: false),
                    Permission = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentPermissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentPermissions_Agents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentPermissions_AgentId_Permission",
                table: "AgentPermissions",
                columns: new[] { "AgentId", "Permission" },
                unique: true);
        }
    }
}
