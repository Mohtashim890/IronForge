using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace IronForge.Api.Migrations
{
    /// <inheritdoc />
    public partial class AccessTerminalsAndPhoneBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PassPhoneBoundAtUtc",
                table: "MemberProfiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PassPhoneIdHash",
                table: "MemberProfiles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequirePhoneUnlock",
                table: "GymSettings",
                type: "boolean",
                nullable: false,
                // Existing gyms get the safer default: unlock the phone before the pass shows.
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "Warning",
                table: "CheckIns",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AllowedIp",
                table: "CheckInDevices",
                type: "character varying(45)",
                maxLength: 45,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "CheckInDevices",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                // Every device before terminals existed is a kiosk.
                defaultValue: "Kiosk");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSyncedAtUtc",
                table: "CheckInDevices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SerialNumber",
                table: "CheckInDevices",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TerminalCommands",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    CheckInDeviceId = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Pin = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReturnCode = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TerminalCommands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TerminalCommands_CheckInDevices_CheckInDeviceId",
                        column: x => x.CheckInDeviceId,
                        principalTable: "CheckInDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TerminalCommands_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TerminalUsers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    Pin = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    TerminalName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    HasFingerprint = table.Column<bool>(type: "boolean", nullable: false),
                    MemberProfileId = table.Column<int>(type: "integer", nullable: true),
                    IgnoredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FirstSeenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LinkedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastEntryAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TerminalUsers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TerminalUsers_MemberProfiles_MemberProfileId",
                        column: x => x.MemberProfileId,
                        principalTable: "MemberProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TerminalUsers_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TerminalUserSyncs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    CheckInDeviceId = table.Column<int>(type: "integer", nullable: false),
                    Pin = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    ValidUntil = table.Column<DateOnly>(type: "date", nullable: false),
                    QueuedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TerminalUserSyncs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TerminalUserSyncs_CheckInDevices_CheckInDeviceId",
                        column: x => x.CheckInDeviceId,
                        principalTable: "CheckInDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TerminalUserSyncs_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CheckInDevices_SerialNumber",
                table: "CheckInDevices",
                column: "SerialNumber",
                unique: true,
                filter: "\"SerialNumber\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TerminalCommands_CheckInDeviceId_CompletedAtUtc",
                table: "TerminalCommands",
                columns: new[] { "CheckInDeviceId", "CompletedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TerminalCommands_TenantId",
                table: "TerminalCommands",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TerminalUsers_MemberProfileId",
                table: "TerminalUsers",
                column: "MemberProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_TerminalUsers_TenantId_MemberProfileId",
                table: "TerminalUsers",
                columns: new[] { "TenantId", "MemberProfileId" },
                unique: true,
                filter: "\"MemberProfileId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TerminalUsers_TenantId_Pin",
                table: "TerminalUsers",
                columns: new[] { "TenantId", "Pin" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TerminalUserSyncs_CheckInDeviceId_Pin",
                table: "TerminalUserSyncs",
                columns: new[] { "CheckInDeviceId", "Pin" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TerminalUserSyncs_TenantId",
                table: "TerminalUserSyncs",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TerminalCommands");

            migrationBuilder.DropTable(
                name: "TerminalUsers");

            migrationBuilder.DropTable(
                name: "TerminalUserSyncs");

            migrationBuilder.DropIndex(
                name: "IX_CheckInDevices_SerialNumber",
                table: "CheckInDevices");

            migrationBuilder.DropColumn(
                name: "PassPhoneBoundAtUtc",
                table: "MemberProfiles");

            migrationBuilder.DropColumn(
                name: "PassPhoneIdHash",
                table: "MemberProfiles");

            migrationBuilder.DropColumn(
                name: "RequirePhoneUnlock",
                table: "GymSettings");

            migrationBuilder.DropColumn(
                name: "Warning",
                table: "CheckIns");

            migrationBuilder.DropColumn(
                name: "AllowedIp",
                table: "CheckInDevices");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "CheckInDevices");

            migrationBuilder.DropColumn(
                name: "LastSyncedAtUtc",
                table: "CheckInDevices");

            migrationBuilder.DropColumn(
                name: "SerialNumber",
                table: "CheckInDevices");
        }
    }
}
