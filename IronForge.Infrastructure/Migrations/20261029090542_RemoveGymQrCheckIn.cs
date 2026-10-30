using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IronForge.Api.Migrations
{
    /// <inheritdoc />
    public partial class RemoveGymQrCheckIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The member-scans-the-gym's-code flow is gone. Its visits were self
            // check-ins at a device, closest to a kiosk scan; its refusals become
            // the matching pass refusals, so old rows still read.
            migrationBuilder.Sql("UPDATE \"CheckIns\" SET \"Method\" = 'Kiosk' WHERE \"Method\" = 'GymQr';");
            migrationBuilder.Sql("UPDATE \"CheckInDenials\" SET \"Method\" = 'Kiosk' WHERE \"Method\" = 'GymQr';");
            migrationBuilder.Sql("UPDATE \"CheckInDenials\" SET \"Reason\" = 'InvalidPass' WHERE \"Reason\" = 'InvalidGymQr';");
            migrationBuilder.Sql("UPDATE \"CheckInDenials\" SET \"Reason\" = 'ExpiredPass' WHERE \"Reason\" = 'ExpiredGymQr';");

            migrationBuilder.DropColumn(
                name: "GymQrCheckInEnabled",
                table: "GymSettings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "GymQrCheckInEnabled",
                table: "GymSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
