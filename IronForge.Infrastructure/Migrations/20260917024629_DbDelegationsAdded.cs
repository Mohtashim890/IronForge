using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IronForge.Api.Migrations
{
    /// <inheritdoc />
    public partial class DbDelegationsAdded : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgentDelegations",
                columns: table => new
                {
                    DelegationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    AgentId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Revoked = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentDelegations", x => x.DelegationId);
                });

            migrationBuilder.CreateTable(
                name: "AgentDelegationScopes",
                columns: table => new
                {
                    DelegationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Scope = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentDelegationScopes", x => new { x.DelegationId, x.Scope });
                    table.ForeignKey(
                        name: "FK_AgentDelegationScopes_AgentDelegations_DelegationId",
                        column: x => x.DelegationId,
                        principalTable: "AgentDelegations",
                        principalColumn: "DelegationId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentDelegations_AgentId",
                table: "AgentDelegations",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentDelegations_ExpiresAtUtc",
                table: "AgentDelegations",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AgentDelegations_UserId",
                table: "AgentDelegations",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentDelegationScopes");

            migrationBuilder.DropTable(
                name: "AgentDelegations");

            migrationBuilder.DropIndex(
                name: "IX_Users_Email",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_Username",
                table: "Users");
        }
    }
}
