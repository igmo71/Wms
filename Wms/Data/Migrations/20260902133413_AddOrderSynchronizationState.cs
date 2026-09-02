using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderSynchronizationState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExternalSynchronizationAcknowledgedAtUtc",
                table: "ShippingOrders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalSynchronizationAcknowledgedBy",
                table: "ShippingOrders",
                type: "nvarchar(36)",
                maxLength: 36,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalSynchronizationAcknowledgedFingerprint",
                table: "ShippingOrders",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExternalSynchronizationCheckedAtUtc",
                table: "ShippingOrders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExternalSynchronizationDetectedAtUtc",
                table: "ShippingOrders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalSynchronizationFingerprint",
                table: "ShippingOrders",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExternalSynchronizationLevel",
                table: "ShippingOrders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExternalSynchronizationAcknowledgedAtUtc",
                table: "ReceivingOrders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalSynchronizationAcknowledgedBy",
                table: "ReceivingOrders",
                type: "nvarchar(36)",
                maxLength: 36,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalSynchronizationAcknowledgedFingerprint",
                table: "ReceivingOrders",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExternalSynchronizationCheckedAtUtc",
                table: "ReceivingOrders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExternalSynchronizationDetectedAtUtc",
                table: "ReceivingOrders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalSynchronizationFingerprint",
                table: "ReceivingOrders",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExternalSynchronizationLevel",
                table: "ReceivingOrders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                """
                UPDATE [ShippingOrders]
                SET [ExternalSynchronizationLevel] = 2,
                    [ExternalSynchronizationCheckedAtUtc] = SYSDATETIMEOFFSET(),
                    [ExternalSynchronizationDetectedAtUtc] = SYSDATETIMEOFFSET()
                WHERE [ExternalChangeDetected] = 1;

                UPDATE [ReceivingOrders]
                SET [ExternalSynchronizationLevel] = 2,
                    [ExternalSynchronizationCheckedAtUtc] = SYSDATETIMEOFFSET(),
                    [ExternalSynchronizationDetectedAtUtc] = SYSDATETIMEOFFSET()
                WHERE [ExternalChangeDetected] = 1;
                """);

            migrationBuilder.DropColumn(
                name: "ExternalChangeDetected",
                table: "ShippingOrders");

            migrationBuilder.DropColumn(
                name: "ExternalChangeDetected",
                table: "ReceivingOrders");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ExternalChangeDetected",
                table: "ShippingOrders",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ExternalChangeDetected",
                table: "ReceivingOrders",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                """
                UPDATE [ShippingOrders]
                SET [ExternalChangeDetected] = 1
                WHERE [ExternalSynchronizationLevel] <> 0;

                UPDATE [ReceivingOrders]
                SET [ExternalChangeDetected] = 1
                WHERE [ExternalSynchronizationLevel] <> 0;
                """);

            migrationBuilder.DropColumn(
                name: "ExternalSynchronizationAcknowledgedAtUtc",
                table: "ShippingOrders");

            migrationBuilder.DropColumn(
                name: "ExternalSynchronizationAcknowledgedBy",
                table: "ShippingOrders");

            migrationBuilder.DropColumn(
                name: "ExternalSynchronizationAcknowledgedFingerprint",
                table: "ShippingOrders");

            migrationBuilder.DropColumn(
                name: "ExternalSynchronizationCheckedAtUtc",
                table: "ShippingOrders");

            migrationBuilder.DropColumn(
                name: "ExternalSynchronizationDetectedAtUtc",
                table: "ShippingOrders");

            migrationBuilder.DropColumn(
                name: "ExternalSynchronizationFingerprint",
                table: "ShippingOrders");

            migrationBuilder.DropColumn(
                name: "ExternalSynchronizationLevel",
                table: "ShippingOrders");

            migrationBuilder.DropColumn(
                name: "ExternalSynchronizationAcknowledgedAtUtc",
                table: "ReceivingOrders");

            migrationBuilder.DropColumn(
                name: "ExternalSynchronizationAcknowledgedBy",
                table: "ReceivingOrders");

            migrationBuilder.DropColumn(
                name: "ExternalSynchronizationAcknowledgedFingerprint",
                table: "ReceivingOrders");

            migrationBuilder.DropColumn(
                name: "ExternalSynchronizationCheckedAtUtc",
                table: "ReceivingOrders");

            migrationBuilder.DropColumn(
                name: "ExternalSynchronizationDetectedAtUtc",
                table: "ReceivingOrders");

            migrationBuilder.DropColumn(
                name: "ExternalSynchronizationFingerprint",
                table: "ReceivingOrders");

            migrationBuilder.DropColumn(
                name: "ExternalSynchronizationLevel",
                table: "ReceivingOrders");
        }
    }
}
