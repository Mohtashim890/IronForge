using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IronForge.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddProductOwnerId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OwnerUserId",
                table: "Products",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_Products_OwnerUserId",
                table: "Products",
                column: "OwnerUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Products_Users_OwnerUserId",
                table: "Products",
                column: "OwnerUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Products_Users_OwnerUserId",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_OwnerUserId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                table: "Products");
        }
    }
}
