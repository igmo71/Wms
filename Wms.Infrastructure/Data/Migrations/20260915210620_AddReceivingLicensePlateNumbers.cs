using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReceivingLicensePlateNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAtUtc",
                table: "LicensePlateNumbers",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancelledBy",
                table: "LicensePlateNumbers",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAtUtc",
                table: "LicensePlateNumbers",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "LicensePlateNumbers",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "OperationalRevision",
                table: "LicensePlateNumbers",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "ReceivingOrderId",
                table: "LicensePlateNumbers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResponsibleUserId",
                table: "LicensePlateNumbers",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "LicensePlateNumbers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_LicensePlateNumbers_ReceivingOrderId",
                table: "LicensePlateNumbers",
                column: "ReceivingOrderId");

            migrationBuilder.CreateIndex(
                name: "UX_LicensePlateNumbers_OpenOwner",
                table: "LicensePlateNumbers",
                columns: new[] { "ReceivingOrderId", "ResponsibleUserId" },
                unique: true,
                filter: "[Status] = 1");

            migrationBuilder.AddForeignKey(
                name: "FK_LicensePlateNumbers_ReceivingOrders_ReceivingOrderId",
                table: "LicensePlateNumbers",
                column: "ReceivingOrderId",
                principalTable: "ReceivingOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LicensePlateNumbers_ReceivingOrders_ReceivingOrderId",
                table: "LicensePlateNumbers");

            migrationBuilder.DropIndex(
                name: "IX_LicensePlateNumbers_ReceivingOrderId",
                table: "LicensePlateNumbers");

            migrationBuilder.DropIndex(
                name: "UX_LicensePlateNumbers_OpenOwner",
                table: "LicensePlateNumbers");

            migrationBuilder.DropColumn(
                name: "CancelledAtUtc",
                table: "LicensePlateNumbers");

            migrationBuilder.DropColumn(
                name: "CancelledBy",
                table: "LicensePlateNumbers");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "LicensePlateNumbers");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "LicensePlateNumbers");

            migrationBuilder.DropColumn(
                name: "OperationalRevision",
                table: "LicensePlateNumbers");

            migrationBuilder.DropColumn(
                name: "ReceivingOrderId",
                table: "LicensePlateNumbers");

            migrationBuilder.DropColumn(
                name: "ResponsibleUserId",
                table: "LicensePlateNumbers");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "LicensePlateNumbers");
        }
    }
}
