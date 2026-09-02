using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Data.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyOrderSynchronizationState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExternalSynchronizationAcknowledgedFingerprint",
                table: "ShippingOrders");

            migrationBuilder.DropColumn(
                name: "ExternalSynchronizationAcknowledgedFingerprint",
                table: "ReceivingOrders");

            migrationBuilder.RenameColumn(
                name: "ExternalSynchronizationLevel",
                table: "ShippingOrders",
                newName: "SynchronizationLevel");

            migrationBuilder.RenameColumn(
                name: "ExternalSynchronizationFingerprint",
                table: "ShippingOrders",
                newName: "SynchronizationFingerprint");

            migrationBuilder.RenameColumn(
                name: "ExternalSynchronizationDetectedAtUtc",
                table: "ShippingOrders",
                newName: "SynchronizationDetectedAtUtc");

            migrationBuilder.RenameColumn(
                name: "ExternalSynchronizationAcknowledgedBy",
                table: "ShippingOrders",
                newName: "SynchronizationAcknowledgedBy");

            migrationBuilder.RenameColumn(
                name: "ExternalSynchronizationAcknowledgedAtUtc",
                table: "ShippingOrders",
                newName: "SynchronizationAcknowledgedAtUtc");

            migrationBuilder.RenameColumn(
                name: "ExternalSynchronizationLevel",
                table: "ReceivingOrders",
                newName: "SynchronizationLevel");

            migrationBuilder.RenameColumn(
                name: "ExternalSynchronizationFingerprint",
                table: "ReceivingOrders",
                newName: "SynchronizationFingerprint");

            migrationBuilder.RenameColumn(
                name: "ExternalSynchronizationDetectedAtUtc",
                table: "ReceivingOrders",
                newName: "SynchronizationDetectedAtUtc");

            migrationBuilder.RenameColumn(
                name: "ExternalSynchronizationAcknowledgedBy",
                table: "ReceivingOrders",
                newName: "SynchronizationAcknowledgedBy");

            migrationBuilder.RenameColumn(
                name: "ExternalSynchronizationAcknowledgedAtUtc",
                table: "ReceivingOrders",
                newName: "SynchronizationAcknowledgedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "SynchronizationLevel",
                table: "ShippingOrders",
                newName: "ExternalSynchronizationLevel");

            migrationBuilder.RenameColumn(
                name: "SynchronizationFingerprint",
                table: "ShippingOrders",
                newName: "ExternalSynchronizationFingerprint");

            migrationBuilder.RenameColumn(
                name: "SynchronizationDetectedAtUtc",
                table: "ShippingOrders",
                newName: "ExternalSynchronizationDetectedAtUtc");

            migrationBuilder.RenameColumn(
                name: "SynchronizationAcknowledgedBy",
                table: "ShippingOrders",
                newName: "ExternalSynchronizationAcknowledgedBy");

            migrationBuilder.RenameColumn(
                name: "SynchronizationAcknowledgedAtUtc",
                table: "ShippingOrders",
                newName: "ExternalSynchronizationAcknowledgedAtUtc");

            migrationBuilder.RenameColumn(
                name: "SynchronizationLevel",
                table: "ReceivingOrders",
                newName: "ExternalSynchronizationLevel");

            migrationBuilder.RenameColumn(
                name: "SynchronizationFingerprint",
                table: "ReceivingOrders",
                newName: "ExternalSynchronizationFingerprint");

            migrationBuilder.RenameColumn(
                name: "SynchronizationDetectedAtUtc",
                table: "ReceivingOrders",
                newName: "ExternalSynchronizationDetectedAtUtc");

            migrationBuilder.RenameColumn(
                name: "SynchronizationAcknowledgedBy",
                table: "ReceivingOrders",
                newName: "ExternalSynchronizationAcknowledgedBy");

            migrationBuilder.RenameColumn(
                name: "SynchronizationAcknowledgedAtUtc",
                table: "ReceivingOrders",
                newName: "ExternalSynchronizationAcknowledgedAtUtc");

            migrationBuilder.AddColumn<string>(
                name: "ExternalSynchronizationAcknowledgedFingerprint",
                table: "ShippingOrders",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalSynchronizationAcknowledgedFingerprint",
                table: "ReceivingOrders",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);
        }
    }
}
