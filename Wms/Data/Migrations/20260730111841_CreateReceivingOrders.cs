using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Data.Migrations
{
    /// <inheritdoc />
    public partial class CreateReceivingOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReceivingOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DataVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Posted = table.Column<bool>(type: "bit", nullable: false),
                    DeletionMark = table.Column<bool>(type: "bit", nullable: false),
                    DateTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReceivingLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    WarehouseOperation = table.Column<int>(type: "int", nullable: false),
                    BusinessOperation = table.Column<int>(type: "int", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SenderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SenderType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BaseOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BaseOrderType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceivingOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReceivingOrders_StorageLocations_ReceivingLocationId",
                        column: x => x.ReceivingLocationId,
                        principalTable: "StorageLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceivingOrders_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReceivingOrderItems",
                columns: table => new
                {
                    ReceivingOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    StockKeepingUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PlannQuantity = table.Column<double>(type: "float", nullable: false),
                    FactQuantity = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceivingOrderItems", x => new { x.ReceivingOrderId, x.LineNumber });
                    table.ForeignKey(
                        name: "FK_ReceivingOrderItems_ReceivingOrders_ReceivingOrderId",
                        column: x => x.ReceivingOrderId,
                        principalTable: "ReceivingOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReceivingOrderItems_StockKeepingUnits_StockKeepingUnitId",
                        column: x => x.StockKeepingUnitId,
                        principalTable: "StockKeepingUnits",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingOrderItems_StockKeepingUnitId",
                table: "ReceivingOrderItems",
                column: "StockKeepingUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingOrders_ReceivingLocationId",
                table: "ReceivingOrders",
                column: "ReceivingLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingOrders_WarehouseId",
                table: "ReceivingOrders",
                column: "WarehouseId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReceivingOrderItems");

            migrationBuilder.DropTable(
                name: "ReceivingOrders");
        }
    }
}
