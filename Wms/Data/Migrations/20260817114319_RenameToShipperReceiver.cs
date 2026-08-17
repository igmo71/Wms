using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameToShipperReceiver : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "RecipientType",
                table: "ShippingOrders",
                newName: "ReceiverType");

            migrationBuilder.RenameColumn(
                name: "RecipientId",
                table: "ShippingOrders",
                newName: "ReceiverId");

            migrationBuilder.RenameColumn(
                name: "SenderType",
                table: "ReceivingOrders",
                newName: "ShipperType");

            migrationBuilder.RenameColumn(
                name: "SenderId",
                table: "ReceivingOrders",
                newName: "ShipperId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ReceiverType",
                table: "ShippingOrders",
                newName: "RecipientType");

            migrationBuilder.RenameColumn(
                name: "ReceiverId",
                table: "ShippingOrders",
                newName: "RecipientId");

            migrationBuilder.RenameColumn(
                name: "ShipperType",
                table: "ReceivingOrders",
                newName: "SenderType");

            migrationBuilder.RenameColumn(
                name: "ShipperId",
                table: "ReceivingOrders",
                newName: "SenderId");
        }
    }
}
