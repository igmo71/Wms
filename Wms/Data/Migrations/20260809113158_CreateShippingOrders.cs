using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Data.Migrations
{
    /// <inheritdoc />
    public partial class CreateShippingOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ReceivingOrderItems_StockKeepingUnits_StockKeepingUnitId",
                table: "ReceivingOrderItems");

            migrationBuilder.CreateTable(
                name: "ShippingOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeletionMark = table.Column<bool>(type: "bit", nullable: false),
                    Posted = table.Column<bool>(type: "bit", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShippingLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Queue = table.Column<int>(type: "int", nullable: false),
                    PlannedShippingDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeliveryDirectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WarehouseOperation = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PickingStartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReadyForShipmentAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ShippedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PickingStartedBy = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true),
                    ReadyForShipmentBy = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true),
                    ShippedBy = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true),
                    ExternalChangeDetected = table.Column<bool>(type: "bit", nullable: false),
                    RecipientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipientType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShippingOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShippingOrders_DeliveryDirections_DeliveryDirectionId",
                        column: x => x.DeliveryDirectionId,
                        principalTable: "DeliveryDirections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShippingOrders_StorageLocations_ShippingLocationId",
                        column: x => x.ShippingLocationId,
                        principalTable: "StorageLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShippingOrders_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShippingOrderBaseItems",
                columns: table => new
                {
                    ShippingOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    StockKeepingUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanQuantity = table.Column<double>(type: "float", nullable: false),
                    BaseOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BaseOrderType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShippingOrderBaseItems", x => new { x.ShippingOrderId, x.LineNumber });
                    table.ForeignKey(
                        name: "FK_ShippingOrderBaseItems_ShippingOrders_ShippingOrderId",
                        column: x => x.ShippingOrderId,
                        principalTable: "ShippingOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ShippingOrderBaseItems_StockKeepingUnits_StockKeepingUnitId",
                        column: x => x.StockKeepingUnitId,
                        principalTable: "StockKeepingUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShippingOrderItems",
                columns: table => new
                {
                    ShippingOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    StockKeepingUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanQuantity = table.Column<double>(type: "float", nullable: false),
                    FactQuantity = table.Column<double>(type: "float", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Action = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShippingOrderItems", x => new { x.ShippingOrderId, x.LineNumber });
                    table.ForeignKey(
                        name: "FK_ShippingOrderItems_ShippingOrders_ShippingOrderId",
                        column: x => x.ShippingOrderId,
                        principalTable: "ShippingOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ShippingOrderItems_StockKeepingUnits_StockKeepingUnitId",
                        column: x => x.StockKeepingUnitId,
                        principalTable: "StockKeepingUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShippingOrderBaseItems_StockKeepingUnitId",
                table: "ShippingOrderBaseItems",
                column: "StockKeepingUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_ShippingOrderItems_StockKeepingUnitId",
                table: "ShippingOrderItems",
                column: "StockKeepingUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_ShippingOrders_DeliveryDirectionId",
                table: "ShippingOrders",
                column: "DeliveryDirectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ShippingOrders_ShippingLocationId",
                table: "ShippingOrders",
                column: "ShippingLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_ShippingOrders_WarehouseId",
                table: "ShippingOrders",
                column: "WarehouseId");

            migrationBuilder.AddForeignKey(
                name: "FK_ReceivingOrderItems_StockKeepingUnits_StockKeepingUnitId",
                table: "ReceivingOrderItems",
                column: "StockKeepingUnitId",
                principalTable: "StockKeepingUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ReceivingOrderItems_StockKeepingUnits_StockKeepingUnitId",
                table: "ReceivingOrderItems");

            migrationBuilder.DropTable(
                name: "ShippingOrderBaseItems");

            migrationBuilder.DropTable(
                name: "ShippingOrderItems");

            migrationBuilder.DropTable(
                name: "ShippingOrders");

            migrationBuilder.AddForeignKey(
                name: "FK_ReceivingOrderItems_StockKeepingUnits_StockKeepingUnitId",
                table: "ReceivingOrderItems",
                column: "StockKeepingUnitId",
                principalTable: "StockKeepingUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
