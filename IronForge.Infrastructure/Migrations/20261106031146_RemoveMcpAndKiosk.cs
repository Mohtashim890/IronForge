using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace IronForge.Api.Migrations
{
    /// <inheritdoc />
    public partial class RemoveMcpAndKiosk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Kiosks were removed: their rows stay for visit history, revoked.
            migrationBuilder.Sql(
                "UPDATE \"CheckInDevices\" SET \"RevokedAtUtc\" = now() " +
                "WHERE \"Kind\" = 'Kiosk' AND \"RevokedAtUtc\" IS NULL;");

            migrationBuilder.DropTable(
                name: "AgentDelegationScopes");

            migrationBuilder.DropTable(
                name: "AgentMcpClientAuthorizations");

            migrationBuilder.DropTable(
                name: "AgentDelegations");

            migrationBuilder.DropTable(
                name: "McpClients");

            migrationBuilder.DropIndex(
                name: "IX_CheckInDevices_KeyHash",
                table: "CheckInDevices");

            migrationBuilder.DropIndex(
                name: "IX_CheckInDevices_PairingCodeHash",
                table: "CheckInDevices");

            migrationBuilder.DropColumn(
                name: "KeyHash",
                table: "CheckInDevices");

            migrationBuilder.DropColumn(
                name: "PairedAtUtc",
                table: "CheckInDevices");

            migrationBuilder.DropColumn(
                name: "PairingCodeHash",
                table: "CheckInDevices");

            migrationBuilder.DropColumn(
                name: "PairingExpiresAtUtc",
                table: "CheckInDevices");

            migrationBuilder.DropColumn(
                name: "ClientId",
                table: "AuditEvents");

            migrationBuilder.DropColumn(
                name: "DelegationId",
                table: "AuditEvents");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "KeyHash",
                table: "CheckInDevices",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PairedAtUtc",
                table: "CheckInDevices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PairingCodeHash",
                table: "CheckInDevices",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PairingExpiresAtUtc",
                table: "CheckInDevices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClientId",
                table: "AuditEvents",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DelegationId",
                table: "AuditEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AgentDelegations",
                columns: table => new
                {
                    DelegationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ClientId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Revoked = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentDelegations", x => x.DelegationId);
                    table.ForeignKey(
                        name: "FK_AgentDelegations_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "McpClients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ClientId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ClientType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_McpClients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_McpClients_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
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

            migrationBuilder.CreateTable(
                name: "AgentMcpClientAuthorizations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AgentEntityId = table.Column<int>(type: "integer", nullable: false),
                    McpClientEntityId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
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
                name: "IX_CheckInDevices_KeyHash",
                table: "CheckInDevices",
                column: "KeyHash",
                unique: true,
                filter: "\"KeyHash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CheckInDevices_PairingCodeHash",
                table: "CheckInDevices",
                column: "PairingCodeHash",
                unique: true,
                filter: "\"PairingCodeHash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AgentDelegations_AgentId",
                table: "AgentDelegations",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentDelegations_ExpiresAtUtc",
                table: "AgentDelegations",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AgentDelegations_TenantId_UserId",
                table: "AgentDelegations",
                columns: new[] { "TenantId", "UserId" });

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

            migrationBuilder.CreateIndex(
                name: "IX_McpClients_TenantId_ClientId",
                table: "McpClients",
                columns: new[] { "TenantId", "ClientId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_McpClients_TenantId_IsActive",
                table: "McpClients",
                columns: new[] { "TenantId", "IsActive" });
        }
    }
}
