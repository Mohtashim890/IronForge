using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace IronForge.Api.Migrations
{
    /// <inheritdoc />
    public partial class CheckInPassesDevicesPhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "LastPassStep",
                table: "MemberProfiles",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PassKeyVersion",
                table: "MemberProfiles",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "PhotoUpdatedAtUtc",
                table: "MemberProfiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CheckInDeviceId",
                table: "CheckIns",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Method",
                table: "CheckIns",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                // Every check-in before this migration was made by staff at the front desk.
                defaultValue: "FrontDesk");

            migrationBuilder.CreateTable(
                name: "CheckInDevices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    KeyHash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PairingCodeHash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PairingExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PairedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSeenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckInDevices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheckInDevices_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MemberPhotos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    MemberProfileId = table.Column<int>(type: "integer", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Data = table.Column<byte[]>(type: "bytea", nullable: false),
                    UploadedByStaff = table.Column<bool>(type: "boolean", nullable: false),
                    UploadedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UploadedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemberPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MemberPhotos_MemberProfiles_MemberProfileId",
                        column: x => x.MemberProfileId,
                        principalTable: "MemberProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MemberPhotos_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CheckInDenials",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    MemberProfileId = table.Column<int>(type: "integer", nullable: true),
                    Method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CheckInDeviceId = table.Column<int>(type: "integer", nullable: true),
                    RecordedByUserId = table.Column<int>(type: "integer", nullable: true),
                    Reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Message = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    AttemptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckInDenials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheckInDenials_CheckInDevices_CheckInDeviceId",
                        column: x => x.CheckInDeviceId,
                        principalTable: "CheckInDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CheckInDenials_MemberProfiles_MemberProfileId",
                        column: x => x.MemberProfileId,
                        principalTable: "MemberProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CheckInDenials_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CheckIns_CheckInDeviceId",
                table: "CheckIns",
                column: "CheckInDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_CheckInDenials_CheckInDeviceId",
                table: "CheckInDenials",
                column: "CheckInDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_CheckInDenials_MemberProfileId_AttemptedAtUtc",
                table: "CheckInDenials",
                columns: new[] { "MemberProfileId", "AttemptedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CheckInDenials_TenantId_AttemptedAtUtc",
                table: "CheckInDenials",
                columns: new[] { "TenantId", "AttemptedAtUtc" });

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
                name: "IX_CheckInDevices_TenantId",
                table: "CheckInDevices",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_MemberPhotos_MemberProfileId",
                table: "MemberPhotos",
                column: "MemberProfileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MemberPhotos_TenantId",
                table: "MemberPhotos",
                column: "TenantId");

            migrationBuilder.AddForeignKey(
                name: "FK_CheckIns_CheckInDevices_CheckInDeviceId",
                table: "CheckIns",
                column: "CheckInDeviceId",
                principalTable: "CheckInDevices",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CheckIns_CheckInDevices_CheckInDeviceId",
                table: "CheckIns");

            migrationBuilder.DropTable(
                name: "CheckInDenials");

            migrationBuilder.DropTable(
                name: "MemberPhotos");

            migrationBuilder.DropTable(
                name: "CheckInDevices");

            migrationBuilder.DropIndex(
                name: "IX_CheckIns_CheckInDeviceId",
                table: "CheckIns");

            migrationBuilder.DropColumn(
                name: "LastPassStep",
                table: "MemberProfiles");

            migrationBuilder.DropColumn(
                name: "PassKeyVersion",
                table: "MemberProfiles");

            migrationBuilder.DropColumn(
                name: "PhotoUpdatedAtUtc",
                table: "MemberProfiles");

            migrationBuilder.DropColumn(
                name: "CheckInDeviceId",
                table: "CheckIns");

            migrationBuilder.DropColumn(
                name: "Method",
                table: "CheckIns");
        }
    }
}
