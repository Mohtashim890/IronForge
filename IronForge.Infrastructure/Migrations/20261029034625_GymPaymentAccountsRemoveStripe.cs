using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace IronForge.Api.Migrations
{
    /// <inheritdoc />
    public partial class GymPaymentAccountsRemoveStripe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StripeChargesEnabled",
                table: "GymSettings");

            migrationBuilder.DropColumn(
                name: "StripeConnectedAccountId",
                table: "GymSettings");

            migrationBuilder.AddColumn<string>(
                name: "CheckoutProvider",
                table: "Invoices",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GymPaymentAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    Provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    IsLive = table.Column<bool>(type: "boolean", nullable: false),
                    ApiKeyEncrypted = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    SecretEncrypted = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    WebhookSecretEncrypted = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ApiKeyHint = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    VerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GymPaymentAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GymPaymentAccounts_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GymPaymentAccounts_TenantId",
                table: "GymPaymentAccounts",
                column: "TenantId",
                unique: true);

            // Simulated checkouts started before this release keep working.
            migrationBuilder.Sql(
                """UPDATE "Invoices" SET "CheckoutProvider" = 'simulated' WHERE "CheckoutSessionId" LIKE 'sim\_%';""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GymPaymentAccounts");

            migrationBuilder.DropColumn(
                name: "CheckoutProvider",
                table: "Invoices");

            migrationBuilder.AddColumn<bool>(
                name: "StripeChargesEnabled",
                table: "GymSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "StripeConnectedAccountId",
                table: "GymSettings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }
    }
}
