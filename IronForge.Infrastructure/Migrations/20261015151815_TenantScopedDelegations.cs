using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IronForge.Api.Migrations
{
    /// <inheritdoc />
    public partial class TenantScopedDelegations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing delegations have no tenant and cannot be attributed to
            // one reliably. They live at most one hour, so they are removed
            // instead of backfilled; users create a new delegation.
            migrationBuilder.Sql("DELETE FROM \"AgentDelegationScopes\";");
            migrationBuilder.Sql("DELETE FROM \"AgentDelegations\";");

            migrationBuilder.DropIndex(
                name: "IX_AgentDelegations_UserId",
                table: "AgentDelegations");

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "AgentDelegations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_AgentDelegations_TenantId_UserId",
                table: "AgentDelegations",
                columns: new[] { "TenantId", "UserId" });

            migrationBuilder.AddForeignKey(
                name: "FK_AgentDelegations_Tenants_TenantId",
                table: "AgentDelegations",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AgentDelegations_Tenants_TenantId",
                table: "AgentDelegations");

            migrationBuilder.DropIndex(
                name: "IX_AgentDelegations_TenantId_UserId",
                table: "AgentDelegations");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AgentDelegations");

            migrationBuilder.CreateIndex(
                name: "IX_AgentDelegations_UserId",
                table: "AgentDelegations",
                column: "UserId");
        }
    }
}
