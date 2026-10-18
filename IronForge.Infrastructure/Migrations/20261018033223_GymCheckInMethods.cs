using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IronForge.Api.Migrations
{
    /// <inheritdoc />
    public partial class GymCheckInMethods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "GymQrCheckInEnabled",
                table: "GymSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "MemberPassCheckInEnabled",
                table: "GymSettings",
                type: "boolean",
                nullable: false,
                // Gyms that existed before the switch keep the pass they already had.
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GymQrCheckInEnabled",
                table: "GymSettings");

            migrationBuilder.DropColumn(
                name: "MemberPassCheckInEnabled",
                table: "GymSettings");
        }
    }
}
